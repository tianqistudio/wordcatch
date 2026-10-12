import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]/'worker'))
from core import hit_word, normalize, Dictionary


def word(text, x=0, y=0, score=.99):
    return {'text':text,'score':score,'box':[[x,y],[x+40,y],[x+40,y+20],[x,y+20]]}


class SelectionTests(unittest.TestCase):
    def test_inside(self): self.assertEqual(hit_word([word('hello')],20,10)['text'],'hello')
    def test_blank_is_not_nearest(self): self.assertIsNone(hit_word([word('hello')],50,10))
    def test_vertical_miss(self): self.assertIsNone(hit_word([word('hello')],20,30))
    def test_low_confidence(self): self.assertIsNone(hit_word([word('hello',score=.3)],20,10))
    def test_negative_coordinates(self): self.assertEqual(hit_word([word('hello',-60,-30)],-40,-20)['text'],'hello')
    def test_neighbour(self): self.assertEqual(hit_word([word('one'),word('two',45)],60,10)['text'],'two')
    def test_rotated_box(self):
        rotated = {'text':'tilt','score':.9,'box':[[20,0],[40,20],[20,40],[0,20]]}
        self.assertIsNone(hit_word([rotated],1,1))
        self.assertEqual(hit_word([rotated],20,20)['text'],'tilt')
    def test_degenerate_box(self): self.assertIsNone(hit_word([{'text':'x','score':1,'box':[[1,1]]*4}],1,1))
    def test_normalize(self): self.assertEqual(normalize(' “Hello,” '),'hello')
    def test_apostrophe(self): self.assertEqual(normalize('don’t'),"don't")


class RealDictionaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls): cls.dictionary=Dictionary(Path(__file__).resolve().parents[1]/'data/ecdict.sqlite3')
    @classmethod
    def tearDownClass(cls): cls.dictionary.db.close()
    def test_hello(self): self.assertTrue(self.dictionary.lookup('Hello')['translation'])
    def test_unknown(self): self.assertIsNone(self.dictionary.lookup('zzzznotaword12345'))
    def test_sql_input(self): self.assertIsNone(self.dictionary.lookup("'; DROP TABLE entries;--"))
    def test_inflection(self): self.assertIsNotNone(self.dictionary.lookup('running'))


if __name__ == '__main__': unittest.main()
