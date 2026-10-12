"""Single-owner JSON-lines OCR process. stdout is protocol only."""
import base64
import contextlib
import io
import json
import sys
import time
from pathlib import Path
from core import Dictionary, hit_word

ROOT = Path(sys.executable).resolve().parents[1] if getattr(sys, 'frozen', False) else Path(__file__).resolve().parents[1]
sys.stdout.reconfigure(encoding='utf-8', line_buffering=True)
sys.stdin.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')


def emit(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)


def main():
    try:
        with contextlib.redirect_stdout(sys.stderr):
            import numpy as np
            from PIL import Image, ImageDraw, ImageFont
            from rapidocr import RapidOCR
            models = ROOT/'worker/models'
            names = ['PP-OCRv6_det_small.onnx', 'ch_ppocr_mobile_v2.0_cls_mobile.onnx', 'PP-OCRv6_rec_small.onnx']
            if '--prepare' not in sys.argv and any(not (models/name).is_file() for name in names):
                raise RuntimeError('本地模型缺失，请运行 Setup-WordLens.ps1 准备模型。')
            engine = RapidOCR(params={'Global.model_root_dir': str(models), 'Global.log_level': 'error'})
            warmup = Image.new('RGB', (320, 96), 'white')
            ImageDraw.Draw(warmup).text((20, 20), 'Hello world', font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf', 28), fill='black')
            engine(np.asarray(warmup)[:, :, ::-1].copy(), return_word_box=True)
            dictionary = Dictionary(ROOT/'data/ecdict.sqlite3')
        emit({'ready': True})
    except Exception as exc:
        emit({'ready': False, 'error': str(exc)})
        return 1
    for line in sys.stdin:
        request = {}
        try:
            request = json.loads(line)
            start = time.perf_counter()
            if request['action'] == 'lookup':
                emit({'id': request['id'], 'text': request['text'], 'entry': dictionary.lookup(request['text'])})
                continue
            if request['action'] != 'ocr':
                raise ValueError('Unsupported action')
            image = Image.open(io.BytesIO(base64.b64decode(request['image'], validate=True))).convert('RGB')
            if image.width > 2400 or image.height > 1600:
                raise ValueError('Capture too large')
            # RapidOCR ndarray convention is BGR, unlike Pillow RGB.
            pixels = np.asarray(image)[:, :, ::-1].copy()
            with contextlib.redirect_stdout(sys.stderr):
                result = engine(pixels, return_word_box=True)
            words = []
            for row in (result.word_results or []):
                for text, score, box in row:
                    if box is not None:
                        words.append({'text': text, 'score': float(score), 'box': np.asarray(box).tolist()})
            selected = hit_word(words, request['x'], request['y'])
            text = selected['text'] if selected else ''
            emit({'id': request['id'], 'text': text, 'entry': dictionary.lookup(text) if text else None,
                  'selected': selected, 'words': words, 'elapsed_ms': round((time.perf_counter()-start)*1000)})
        except Exception as exc:
            emit({'id': request.get('id'), 'error': str(exc)})
    dictionary.db.close()
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
