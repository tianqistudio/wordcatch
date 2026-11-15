"""Build a self-contained local installer; never execute the installer."""
import argparse
import contextlib
import ctypes
import hashlib
import importlib.metadata
import json
import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path
from seven_zip import resolve_seven_zip

ROOT=Path(__file__).resolve().parents[1]


def digest(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()


def run(args):
    subprocess.run([str(x) for x in args],cwd=ROOT,check=True)


@contextlib.contextmanager
def nsis_path_alias(directory):
    """Keep NSIS input paths short without copying or moving the payload."""
    if os.name != 'nt':
        yield directory
        return
    occupied = ctypes.windll.kernel32.GetLogicalDrives()
    drive = next((chr(letter) + ':' for letter in range(ord('Z'), ord('M') - 1, -1)
                  if not occupied & (1 << (letter - ord('A')))), None)
    if drive is None:
        raise RuntimeError('No unused drive letter is available for NSIS staging')
    subprocess.run(['subst', drive, str(directory)], check=True, capture_output=True)
    try:
        yield Path(drive + '/')
    finally:
        subprocess.run(['subst', drive, '/D'], check=True, capture_output=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--build-id',default='0.7.2-001')
    parser.add_argument('--seven-zip')
    args=parser.parse_args()
    seven,_=resolve_seven_zip(args.seven_zip)
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
    shutil.copy2(ROOT/'README.en.md',payload/'README.en.md')
    shutil.copy2(ROOT/'README.md',payload/'README.md')
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
    # Scan the complete payload before sealing it into the installer. The
    # container verifier below binds every embedded file to these same bytes.
    run([sys.executable, ROOT/'tools/verify_release_privacy.py', payload,
         ROOT/'vendor/nuget', '--allow-codeview-fingerprint', 'fed8267010385db424a04f39dfb7dcf22c34a000dcfde21d05e19f2c6fa17598', '--owned-prefix', 'WordLens', '--owned-prefix', 'TianqiStudio',
         '--forbid-prefix', str(ROOT), '--report', output/'privacy-verification.json'])
    files=[{'path':p.relative_to(payload).as_posix(),'sha256':digest(p),'bytes':p.stat().st_size} for p in sorted(payload.rglob('*')) if p.is_file()]
    manifest={'product':'WordLens','version':'0.7.2','build_id':args.build_id,'files':files}
    (payload/'payload.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    exe=output/'WordCatch-0.7.2-Setup.exe'
    with nsis_path_alias(output) as alias:
        run([compiler,'/NOCONFIG','/INPUTCHARSET','UTF8','/V3',f'/DPAYLOAD={alias/"payload"}',f'/DOUTPUT={alias/exe.name}',ROOT/'installer/WordLens.nsi'])
    (output/'SHA256SUMS.txt').write_text(f'{digest(exe)}  {exe.name}\n',encoding='utf-8')
    report={'version':'0.7.2','build_id':args.build_id,'installer':str(exe.relative_to(ROOT)).replace('\\','/'),'sha256':digest(exe),'bytes':exe.stat().st_size,'payload_files':len(files),'payload_bytes':sum(p['bytes'] for p in files),'signed':False,'installed_on_host':False}
    (output/'build.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    # Verify the retained installer before retiring only this build's duplicate
    # frozen worker. The payload and diagnostic worker-build tree remain intact.
    run([sys.executable, ROOT/'tools/verify_installer.py', args.build_id, '--seven-zip', seven])
    print(json.dumps(report))


if __name__=='__main__': main()
