"""Read AnkiConnect notes into a private, provenance-preserving phrase database."""
import argparse
import collections
import hashlib
import html
from html.parser import HTMLParser
import json
import re
import sqlite3
import unicodedata
import urllib.request
from urllib.parse import urlparse
from datetime import datetime, timezone
from pathlib import Path

class Plain(HTMLParser):
    def __init__(self): super().__init__(); self.parts=[]; self.hidden=0
    def handle_starttag(self, tag, attrs):
        if tag in ('script','style'): self.hidden+=1
        elif tag in ('br','div','p','li'): self.parts.append('\n')
    def handle_endtag(self, tag):
        if tag in ('script','style'): self.hidden=max(0,self.hidden-1)
        elif tag in ('div','p','li'): self.parts.append('\n')
    def handle_data(self,data):
        if not self.hidden: self.parts.append(data)
def plain(value):
    parser=Plain();parser.feed(value);return html.unescape(''.join(parser.parts)).strip()
def normalize(value):
    return ' '.join(unicodedata.normalize('NFKC',value).replace('’',"'").split()).casefold()
def eligible(value):
    value=re.sub(r'\b(?:Mr|Mrs|Ms|Dr|St|[A-Z])\.(?=\s)',lambda m:m.group()[:-1],value)
    if len(value)>120 or '\n' in value or re.search(r'[.!?。！？;；{}<>/\\=,()]',value): return False
    if not re.fullmatch(r"[A-Za-zÀ-ž0-9 '&’:+,()\-–™®]+",value): return False
    words=re.findall(r"[^\W_]+(?:['’][^\W_]+)*",value,re.UNICODE)
    return 2<=len(words)<=10 and bool(re.search('[A-Za-z]',value))
def connect(env_path):
    values={}
    for line in env_path.read_text(encoding='utf-8-sig').splitlines():
        key,_,value=line.partition('=')
        if key.strip() in ('ANKI_CONNECT_API_KEY','ANKI_CONNECT_URL'): values[key.strip()]=value.strip().strip('"').strip("'")
    url=values.get('ANKI_CONNECT_URL','http://127.0.0.1:8765')
    if urlparse(url).hostname not in ('127.0.0.1','localhost','::1'): raise ValueError('AnkiConnect must be loopback')
    key=values['ANKI_CONNECT_API_KEY']; opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
    def invoke(action,params=None):
        if action not in ('findNotes','notesInfo','cardsInfo','deckNames','version'): raise ValueError('Read-only action required')
        request=urllib.request.Request(url,json.dumps({'action':action,'version':6,'key':key,'params':params or {}}).encode(),{'Content-Type':'application/json'})
        with opener.open(request,timeout=40) as response: result=json.load(response)
        if result.get('error'): raise RuntimeError('AnkiConnect read failed: '+str(result['error']))
        return result['result']
    return invoke
SCHEMA="""
PRAGMA foreign_keys=ON;
CREATE TABLE metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
CREATE TABLE phrases(id INTEGER PRIMARY KEY, normalized TEXT NOT NULL UNIQUE, text TEXT NOT NULL, enabled INTEGER NOT NULL CHECK(enabled IN (0,1)));
CREATE TABLE occurrences(id INTEGER PRIMARY KEY,phrase_id INTEGER NOT NULL REFERENCES phrases(id),note_id INTEGER NOT NULL,model TEXT NOT NULL,field TEXT NOT NULL,original TEXT NOT NULL,translation TEXT NOT NULL,decks_json TEXT NOT NULL,UNIQUE(note_id,field,original));
CREATE INDEX occurrences_phrase ON occurrences(phrase_id);
CREATE TABLE review_candidates(id INTEGER PRIMARY KEY,note_id INTEGER NOT NULL,model TEXT NOT NULL,field TEXT NOT NULL,text TEXT NOT NULL,translation TEXT NOT NULL,reason TEXT NOT NULL);
CREATE TABLE deck_sources(phrase_id INTEGER NOT NULL REFERENCES phrases(id),deck TEXT NOT NULL,segment TEXT NOT NULL,PRIMARY KEY(phrase_id,deck));
CREATE VIEW recognition_phrases AS SELECT p.id,p.text,p.normalized,(SELECT count(*) FROM occurrences o WHERE o.phrase_id=p.id)+(SELECT count(*) FROM deck_sources d WHERE d.phrase_id=p.id) AS source_count FROM phrases p WHERE p.enabled=1;
"""
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--env-file',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args()
    if args.output.exists(): raise FileExistsError('Use a fresh output path; existing curated databases are preserved')
    invoke=connect(args.env_file); ids=invoke('findNotes',{'query':''}); notes=[]
    for i in range(0,len(ids),200): notes.extend(invoke('notesInfo',{'notes':ids[i:i+200]}))
    card_ids=[c for n in notes for c in n['cards']]; decks={}
    for i in range(0,len(card_ids),300):
        for c in invoke('cardsInfo',{'cards':card_ids[i:i+300]}): decks[c['cardId']]=c['deckName']
    args.output.parent.mkdir(parents=True,exist_ok=True)
    db=sqlite3.connect(args.output);db.executescript(SCHEMA)
    mapped={'EN','EN_US','EN_JP','Official EN','英文名'}
    skipped=collections.Counter()
    for note in notes:
        fields={name:plain(info['value']) for name,info in note['fields'].items()}
        translation=fields.get('SC') or fields.get('中文名') or fields.get('TC') or ''
        note_decks=sorted({decks[c] for c in note['cards'] if c in decks})
        for field,value in fields.items():
            if field in ('Image','图片','图像','遮盖','笔记','注释','SourceID','ID','Code','SC','TC','中文名','日文名'): continue
            direct=field in mapped or (field=='Name' and note['modelName']=='Games::Counter-Strike 2::Map Name and Icon')
            if not value: continue
            # Generic cards and example fields are review-only: never infer semantic roles.
            if not direct:
                if eligible(value): db.execute('INSERT INTO review_candidates(note_id,model,field,text,translation,reason) VALUES(?,?,?,?,?,?)',(note['noteId'],note['modelName'],field,value,translation,'Field is not an explicit English name; review before enabling'))
                continue
            if not eligible(value):
                skipped['non_phrase_or_complex']+=1
                if len(re.findall(r'[^\W_]+',value,re.UNICODE))>=2 and len(value)<=240:
                    db.execute('INSERT INTO review_candidates(note_id,model,field,text,translation,reason) VALUES(?,?,?,?,?,?)',(note['noteId'],note['modelName'],field,value,translation,'Complex punctuation, multiline or possible sentence'))
                continue
            key=normalize(value)
            db.execute('INSERT OR IGNORE INTO phrases(normalized,text,enabled) VALUES(?,?,1)',(key,value))
            pid=db.execute('SELECT id FROM phrases WHERE normalized=?',(key,)).fetchone()[0]
            db.execute('INSERT INTO occurrences(phrase_id,note_id,model,field,original,translation,decks_json) VALUES(?,?,?,?,?,?,?)',(pid,note['noteId'],note['modelName'],field,value,translation,json.dumps(note_decks,ensure_ascii=False)))
    # Deck names provide recognition context, not invented bilingual definitions.
    for deck in invoke('deckNames'):
        for segment in deck.split('::')[1:]:
            if eligible(segment):
                key=normalize(segment)
                enabled=int(segment!='Image Occlusion Template')
                db.execute('INSERT OR IGNORE INTO phrases(normalized,text,enabled) VALUES(?,?,?)',(key,segment,enabled))
                pid=db.execute('SELECT id FROM phrases WHERE normalized=?',(key,)).fetchone()[0]
                db.execute('INSERT OR IGNORE INTO deck_sources VALUES(?,?,?)',(pid,deck,segment))
    meta={'created_at':datetime.now(timezone.utc).isoformat(),'source':'AnkiConnect read-only','notes_scanned':str(len(notes)),'scope':'Explicit English fields and structured deck-name terms; no invented translations','private':'true','schema_version':'1'}
    db.executemany('INSERT INTO metadata VALUES(?,?)',meta.items());db.commit()
    assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
    assert not db.execute('PRAGMA foreign_key_check').fetchall()
    counts={name:db.execute('SELECT COUNT(*) FROM '+name).fetchone()[0] for name in ('phrases','occurrences','review_candidates','recognition_phrases','deck_sources')}
    examples=db.execute('SELECT text FROM recognition_phrases ORDER BY text LIMIT 15').fetchall()
    apex=db.execute("SELECT text,enabled FROM phrases WHERE normalized='apex legends'").fetchall()
    db.close();report={**counts,'notes_scanned':len(notes),'examples':[r[0] for r in examples],'apex_legends':apex,'sha256':hashlib.file_digest(args.output.open('rb'),'sha256').hexdigest(),'integrity_check':'ok','anki_modified':False}
    args.output.with_suffix('.report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(json.dumps(report,ensure_ascii=False))
if __name__=='__main__':main()
