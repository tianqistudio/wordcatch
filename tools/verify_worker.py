"""Integration tests use generated text images, never personal screen contents."""
import base64
import io
import json
import subprocess
import sys
import time
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]


def main():
    args = [sys.executable, '-u', str(ROOT/'worker/server.py')]
    if '--prepare' in sys.argv: args.append('--prepare')
    process = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, encoding='utf-8', cwd=ROOT)
    results = []
    try:
        ready = json.loads(process.stdout.readline())
        assert ready.get('ready'), ready
        if '--prepare' in sys.argv:
            print('OCR and dictionary ready')
            return
        sequence = 0
        def request(payload):
            nonlocal sequence
            sequence += 1
            process.stdin.write(json.dumps({'id':sequence,**payload})+'\n'); process.stdin.flush()
            result = json.loads(process.stdout.readline())
            assert result['id'] == sequence, result
            assert 'error' not in result, result
            return result
        for size, foreground, background in [(18,'black','white'),(26,'white','#202020'),(36,'black','white')]:
            font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',size)
            image=Image.new('RGB',(720,180),background)
            draw=ImageDraw.Draw(image)
            draw.text((30,50),'Hello world',font=font,fill=foreground)
            bounds=draw.textbbox((30,50),'Hello',font=font)
            x=(bounds[0]+bounds[2])/2; y=(bounds[1]+bounds[3])/2
            stream=io.BytesIO();image.save(stream,format='PNG')
            payload={'action':'ocr','image':base64.b64encode(stream.getvalue()).decode(),'x':x,'y':y}
            reply=request(payload)
            assert reply['text'].lower()=='hello', reply
            assert reply['entry']['translation'], reply
            results.append({'font_size':size,'background':background,'text':reply['text'],'elapsed_ms':reply['elapsed_ms']})
            blank=request({**payload,'x':600,'y':130})
            assert blank['text']=='' and blank['entry'] is None, blank
        reply=request({'action':'lookup','text':'running'})
        assert reply['entry'], reply
        # Protocol stays alive after malformed/unsupported requests.
        process.stdin.write('{"id":999,"action":"invalid"}\n');process.stdin.flush()
        assert 'error' in json.loads(process.stdout.readline())
        assert request({'action':'lookup','text':'hello'})['entry']
        report={'passed':True,'cases':results,'blank_miss_cases':3,'dictionary_inflection':True,'error_recovery':True}
        output=ROOT/'artifacts/qa/v0.1.0/worker-tests.json'
        output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
        print(json.dumps(report,ensure_ascii=False))
    finally:
        process.stdin.close()
        try: process.wait(timeout=10)
        except subprocess.TimeoutExpired: process.kill();process.wait()


if __name__=='__main__': main()
