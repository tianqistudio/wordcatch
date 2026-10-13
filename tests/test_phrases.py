import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'worker'))
from core import PhraseDictionary,hit_phrase

class PhraseTests(unittest.TestCase):
    def setUp(self):
        self.phrases=PhraseDictionary('missing-fixture.sqlite3')
        self.phrases.entries={'apex legends':{'matched':'Apex Legends'},'apex legends mobile':{'matched':'Apex Legends Mobile'}}
    def word(self,text,x,y=0,score=.99):
        return dict(text=text,score=score,box=[[x,y],[x+40,y],[x+40,y+20],[x,y+20]])
    def test_each_word(self):
        words=[self.word('Apex',0),self.word('Legends',45)]
        for x in [20,65]: self.assertEqual(hit_phrase(words,x,10,self.phrases)['text'],'Apex Legends')
    def test_longest(self):
        words=[self.word('Apex',0),self.word('Legends',45),self.word('Mobile',90)]
        self.assertEqual(hit_phrase(words,65,10,self.phrases)['text'],'Apex Legends Mobile')
    def test_blank(self): self.assertIsNone(hit_phrase([self.word('Apex',0)],42,10,self.phrases))
    def test_different_line(self): self.assertEqual(hit_phrase([self.word('Apex',0),self.word('Legends',45,30)],20,10,self.phrases)['text'],'Apex')
    def test_wide_gap(self): self.assertEqual(hit_phrase([self.word('Apex',0),self.word('Legends',100)],20,10,self.phrases)['text'],'Apex')
    def test_low_confidence(self): self.assertEqual(hit_phrase([self.word('Apex',0),self.word('Legends',45,score=.3)],20,10,self.phrases)['text'],'Apex')
    def test_normalized(self): self.assertEqual(self.phrases.lookup(' APEX   Legends ')['matched'],'Apex Legends')
    def test_unrelated_sentence(self): self.assertIsNone(self.phrases.lookup('play Apex Legends today'))
    def test_absent_db(self): self.assertFalse(PhraseDictionary('missing-fixture.sqlite3').entries)

if __name__=='__main__': unittest.main()
