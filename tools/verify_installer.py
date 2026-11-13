"""Verify NSIS container and every embedded payload byte without installing or extracting to disk."""
import argparse
import re
import hashlib
import json
import subprocess
import sys
from pathlib import Path
from seven_zip import resolve_seven_zip

ROOT=Path(__file__).resolve().parents[1]


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('build_id')
    parser.add_argument('--seven-zip')
    args=parser.parse_args()
    build=args.build_id
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9.-]*',build): raise ValueError('Invalid build ID')
    seven,toolchain=resolve_seven_zip(args.seven_zip)
    output=ROOT/'artifacts/installers'/build
    exe=ROOT/json.loads((output/'build.json').read_text(encoding='utf-8'))['installer']
    tested=subprocess.run([str(seven),'t',str(exe),'-bd'],capture_output=True,check=True)
    listing=subprocess.run([str(seven),'l','-slt','-sccUTF-8',str(exe)],capture_output=True,check=True).stdout.decode('utf-8').replace('\r\n','\n')
    entries=[]
    for block in listing.split('----------',1)[1].strip().split('\n\n'):
        fields=dict(line.split(' = ',1) for line in block.splitlines() if ' = ' in line)
        path=fields.get('Path','')
        prefix='$PLUGINSDIR\\payload\\'
        if path.startswith(prefix) and fields.get('Size'): entries.append((path[len(prefix):].replace('\\','/'),int(fields['Size'])))
    expected={p.relative_to(output/'payload').as_posix():p for p in (output/'payload').rglob('*') if p.is_file()}
    if set(p for p,_ in entries)!=set(expected): raise RuntimeError('Embedded file list mismatch')
    process=subprocess.Popen([str(seven),'x','-so','-bd',str(exe),'$PLUGINSDIR\\payload\\*','-x!$PLUGINSDIR\\payload\\Uninstall.exe'],stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    checked=[]
    try:
        for name,size in entries:
            sha=hashlib.sha256(); remaining=size
            while remaining:
                block=process.stdout.read(min(1024*1024,remaining))
                if not block: raise RuntimeError('Unexpected end of container: '+name)
                sha.update(block); remaining-=len(block)
            with expected[name].open('rb') as stream: reference=hashlib.file_digest(stream,'sha256').hexdigest()
            if sha.hexdigest()!=reference: raise RuntimeError('Embedded hash mismatch: '+name)
            checked.append(name)
        if process.stdout.read(1): raise RuntimeError('Unexpected additional embedded bytes')
        _,err=process.communicate(timeout=30)
        if process.returncode: raise RuntimeError(err.decode(errors='replace'))
    finally:
        if process.poll() is None: process.kill();process.wait()
    report={'toolchain':toolchain,'passed':True,'container_crc_test':True,'embedded_files_hash_verified':len(checked),'embedded_payload_bytes':sum(s for _,s in entries),'installed':False,'method':'7-Zip test and streamed extraction to SHA-256; no files extracted to disk'}
    (output/'container-verification.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report))


if __name__=='__main__': main()

