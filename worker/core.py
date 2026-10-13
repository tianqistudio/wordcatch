"""Pure selection and read-only dictionary logic. No screen or network access."""
import math
import json
import unicodedata
import re
import sqlite3
from pathlib import Path


def normalize(text):
    return text.strip().strip('.,!?;:()[]{}<>"“”‘’').replace('’', "'").replace('–', '-').lower()


def hit_word(words, x, y, min_score=0.55):
    """Convex quadrilateral hit test; never choose an unrelated nearest word."""
    hits = []
    for word in words:
        box = word['box']
        if word['score'] < min_score or len(box) != 4:
            continue
        cross = []
        for a, b in zip(box, box[1:] + box[:1]):
            cross.append((b[0]-a[0])*(y-a[1]) - (b[1]-a[1])*(x-a[0]))
        if not (all(v >= -0.001 for v in cross) or all(v <= 0.001 for v in cross)):
            continue
        area = abs(sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(box,box[1:]+box[:1]))) / 2
        if area > 0 and math.isfinite(area):
            hits.append((area, -word['score'], word))
    return min(hits, key=lambda v: v[:2])[2] if hits else None


class PhraseDictionary:
    def __init__(self, path):
        self.entries = {}
        if not Path(path).is_file(): return
        with sqlite3.connect(Path(path).resolve().as_uri() + '?mode=ro', uri=True) as db:
            for pid, text, key in db.execute('SELECT id,text,normalized FROM recognition_phrases'):
                meanings = []
                for translation, decks in db.execute('SELECT DISTINCT translation,decks_json FROM occurrences WHERE phrase_id=?', (pid,)):
                    if translation:
                        label = ' / '.join(json.loads(decks))
                        value = translation + ('\n来源：' + label if label else '')
                        if value not in meanings: meanings.append(value)
                contexts = [row[0] for row in db.execute('SELECT deck FROM deck_sources WHERE phrase_id=?', (pid,))]
                self.entries[key] = dict(word=text, matched=text, phonetic='', translation='\n\n'.join(meanings),
                    definition='' if meanings else '已收录名称；卡牌中没有对应释义。' + ('\n来源：' + ' / '.join(contexts) if contexts else ''), source='Anki 本地短语库')
    @staticmethod
    def key(text):
        return ' '.join(unicodedata.normalize('NFKC', text).replace('’', "'").split()).casefold()
    def lookup(self, text):
        entry = self.entries.get(self.key(text))
        return dict(entry, query=text) if entry else None


def hit_phrase(words, x, y, phrases):
    selected = hit_word(words, x, y)
    if not selected or not phrases.entries: return selected
    def bounds(w):
        xs, ys = zip(*w['box']); return min(xs), min(ys), max(xs), max(ys)
    sx, sy, ex, ey = bounds(selected)
    height = ey-sy
    # Restrict expansion to nearby words on the same horizontal line.
    line = [w for w in words if w['score'] >= .55 and len(w['box']) == 4
            and abs((bounds(w)[1]+bounds(w)[3])/2-(sy+ey)/2) <= height*.4
            and min(bounds(w)[3],ey)-max(bounds(w)[1],sy) >= height*.5]
    line.sort(key=lambda w: bounds(w)[0])
    anchor = next(i for i,w in enumerate(line) if w is selected)
    best = selected; best_length = 0
    for start in range(max(0,anchor-9),anchor+1):
        for end in range(anchor+1,min(len(line),start+10)+1):
            span=line[start:end]
            if any(bounds(b)[0]-bounds(a)[2] > height*1.5 or bounds(b)[0]<bounds(a)[2]-height*.2 for a,b in zip(span,span[1:])): continue
            text=' '.join(w['text'] for w in span)
            if len(text)<=best_length or not phrases.lookup(text): continue
            boxes=[bounds(w) for w in span]; l=min(b[0] for b in boxes);t=min(b[1] for b in boxes);r=max(b[2] for b in boxes);b=max(b[3] for b in boxes)
            best=dict(text=text,score=min(w['score'] for w in span),box=[[l,t],[r,t],[r,b],[l,b]])
            best_length=len(text)
    return best


class Dictionary:
    def __init__(self, path):
        self.db = sqlite3.connect(Path(path).resolve().as_uri() + '?mode=ro', uri=True)
        self.db.row_factory = sqlite3.Row

    def lookup(self, text):
        key = normalize(text)
        if not key or len(key) > 120:
            return None
        row = self.db.execute('SELECT * FROM entries WHERE word = ? COLLATE NOCASE', (key,)).fetchone()
        matched = key
        if row is None:
            alias = self.db.execute('SELECT base FROM forms WHERE form = ? COLLATE NOCASE ORDER BY base LIMIT 1', (key,)).fetchone()
            if alias:
                matched = alias[0]
                row = self.db.execute('SELECT * FROM entries WHERE word = ? COLLATE NOCASE', (matched,)).fetchone()
        if row is None:
            return None
        result = dict(row)
        result['query'] = text
        result['matched'] = matched
        for name in ('translation', 'definition'):
            result[name] = (result[name] or '').replace('\\n', '\n')
        return result
