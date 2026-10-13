"""Build a self-contained local installer; never execute the installer."""
import argparse
import hashlib
import importlib.metadata
import json
import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]


def digest(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()


def run(args):
    subprocess.run([str(x) for x in args],cwd=ROOT,check=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--build-id',default='0.6.1-001')
    args=parser.parse_args()
    if not all(c.isalnum() or c in '.-' for c in args.build_id): raise ValueError('Invalid build ID')
    output=ROOT/'artifacts/installers'/args.build_id
    output.mkdir(parents=True,exist_ok=False)
    payload=output/'payload';payload.mkdir()
    compiler=ROOT/'installer/tools/nsis-3.12/makensis.exe'
    archive=ROOT/'installer/tools/nsis-3.12-official.zip'
    if digest(archive) != '56581f90db321581c5381193d796fffcf2d24b2f8fed2160a6c6a3baa67f2c4f': raise RuntimeError('NSIS archive mismatch')
    with zipfile.ZipFile(archive) as source:
        for info in source.infolist():
            if info.is_dir(): continue
            target=ROOT/'installer/tools'/info.filename
            if hashlib.sha256(source.read(info)).hexdigest()!=digest(target): raise RuntimeError('Compiler file differs: '+info.filename)
    run(['dotnet','publish','src/WordLens/WordLens.csproj','-c','Release','-r','win-x64','--self-contained','true','-o',payload,'-p:DebugType=None','-p:DebugSymbols=false'])
    run([sys.executable,'-m','PyInstaller','--noconfirm','--distpath',output/'worker-runtime','--workpath',output/'worker-build',ROOT/'installer/WordLens.Worker.spec'])
    frozen=output/'worker-runtime/WordLens.Worker'
    shutil.copytree(frozen,payload/'worker')
    shutil.copytree(ROOT/'worker/models',payload/'worker/models')
    (payload/'data').mkdir()
    for name in ('ecdict.sqlite3','source.json','models.json','LICENSE'):
        shutil.copy2(ROOT/'data'/name,payload/'data'/name)
    shutil.copy2(ROOT/'installer/Maintenance.ps1',payload/'Maintenance.ps1')
    shutil.copy2(ROOT/'docs/installed-guide.md',payload/'使用说明.md')
    shutil.copy2(ROOT/'docs/third-party.md',payload/'THIRD-PARTY-NOTICES.md')
    shutil.copy2(ROOT/'NOTICE.md',payload/'NOTICE.md')
    licenses=payload/'licenses';licenses.mkdir()
    shutil.copy2(ROOT/'installer/tools/nsis-3.12/COPYING',licenses/'NSIS-COPYING.txt')
    shutil.copy2(Path(sys.base_prefix)/'LICENSE.txt',licenses/'Python-LICENSE.txt')
    for name in ('LICENSE.txt','ThirdPartyNotices.txt'):
        shutil.copy2(Path(os.environ['ProgramFiles'])/'dotnet'/name,licenses/('DotNet-'+name))
    metadata=[]
    for dist in importlib.metadata.distributions():
        name=dist.metadata.get('Name','unknown')
        if name.lower() in {'pip','pyinstaller','pyinstaller-hooks-contrib','pefile','altgraph','pywin32-ctypes','setuptools'}: continue
        metadata.append({'name':name,'version':dist.version,'license':dist.metadata.get('License-Expression') or dist.metadata.get('License',''),'homepage':dist.metadata.get('Home-page','')})
        for entry in (dist.files or []):
            if any(token in entry.name.lower() for token in ('license','copying','notice')):
                original=Path(dist.locate_file(entry))
                if original.is_file() and original.stat().st_size<2000000:
                    target=licenses/name/Path(str(entry).replace('\\','/'))
                    # metadata relative paths are not trusted for destination selection.
                    target=licenses/name/(hashlib.sha256(str(entry).encode()).hexdigest()[:8]+'-'+entry.name)
                    target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(original,target)
    (licenses/'python-dependencies.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf-8')
    files=[{'path':p.relative_to(payload).as_posix(),'sha256':digest(p),'bytes':p.stat().st_size} for p in sorted(payload.rglob('*')) if p.is_file()]
    manifest={'product':'WordLens','version':'0.6.1','build_id':args.build_id,'files':files}
    (payload/'payload.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    exe=output/'WordCatch-0.6.1-Setup.exe'
    run([compiler,'/NOCONFIG','/INPUTCHARSET','UTF8','/V3',f'/DPAYLOAD={payload}',f'/DOUTPUT={exe}',ROOT/'installer/WordLens.nsi'])
    (output/'SHA256SUMS.txt').write_text(f'{digest(exe)}  {exe.name}\n',encoding='utf-8')
    report={'version':'0.6.1','build_id':args.build_id,'installer':str(exe.relative_to(ROOT)).replace('\\','/'),'sha256':digest(exe),'bytes':exe.stat().st_size,'payload_files':len(files),'payload_bytes':sum(p['bytes'] for p in files),'signed':False,'installed_on_host':False}
    (output/'build.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report))


if __name__=='__main__': main()
