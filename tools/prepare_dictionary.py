"""Download pinned ECDICT input and produce the project's read-only dictionary."""
import csv
import hashlib
import json
import sqlite3
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    data = ROOT/'data'
    data.mkdir(exist_ok=True)
    # Pinned upstream revision, verified with git ls-remote during initial setup.
    commit = 'bc015ed2e24a7abef49fc6dbbb7fe32c1dadaf8b'
    manifest_path = data/'source.json'
    if manifest_path.exists():
        commit = json.loads(manifest_path.read_text('utf-8'))['commit']
    for name in ('ecdict.csv', 'LICENSE'):
        target = data/name
        if not target.exists():
            with urllib.request.urlopen(f'https://raw.githubusercontent.com/skywind3000/ECDICT/{commit}/{name}', timeout=120) as response:
                target.write_bytes(response.read())
    db_path = data/'ecdict.sqlite3'
    if db_path.exists():
        print('Dictionary already exists; refusing to overwrite.')
        return
    db = sqlite3.connect(db_path)
    db.executescript('CREATE TABLE entries(word TEXT PRIMARY KEY COLLATE NOCASE, phonetic TEXT, translation TEXT, definition TEXT, exchange TEXT); CREATE TABLE forms(form TEXT COLLATE NOCASE, base TEXT, PRIMARY KEY(form,base));')
    count = 0
    with (data/'ecdict.csv').open(encoding='utf-8-sig', newline='') as stream:
        for row in csv.DictReader(stream):
            word = row['word'].strip()
            if not word:
                continue
            db.execute('INSERT OR IGNORE INTO entries VALUES(?,?,?,?,?)', (word,row['phonetic'],row['translation'],row['definition'],row['exchange']))
            for part in row['exchange'].split('/'):
                if ':' not in part:
                    continue
                tag, forms = part.split(':', 1)
                if tag in {'p','d','i','3','r','t','s'}:
                    for form in forms.split(','):
                        if form.strip():
                            db.execute('INSERT OR IGNORE INTO forms VALUES(?,?)', (form.strip(),word))
            count += 1
    db.commit()
    total = db.execute('SELECT COUNT(*) FROM entries').fetchone()[0]
    db.close()
    manifest_path.write_text(json.dumps({'repository':'https://github.com/skywind3000/ECDICT','commit':commit,'license':'MIT (upstream declaration)','entries':total,'csv_sha256':hashlib.sha256((data/'ecdict.csv').read_bytes()).hexdigest()}, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'Imported {total} dictionary entries')


if __name__ == '__main__':
    main()
