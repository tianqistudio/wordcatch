"""Pure selection and read-only dictionary logic. No screen or network access."""
import math
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
