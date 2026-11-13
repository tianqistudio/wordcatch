"""Resolve an explicitly configured/PATH tool and validate it before execution."""
import hashlib
import json
import os
import shutil
from pathlib import Path


def resolve_seven_zip(explicit=None):
    candidate = explicit or os.environ.get('WORDCATCH_7ZIP') or shutil.which('7z.exe') or shutil.which('7z')
    if not candidate:
        raise RuntimeError('7-Zip is required: use --seven-zip, WORDCATCH_7ZIP, or PATH; see docs/build-toolchain.md')
    path = Path(candidate).expanduser().resolve(strict=True)
    policy = json.loads(Path(__file__).with_name('seven-zip-toolchain.json').read_text(encoding='utf-8'))
    observed = {}
    for name, expected in policy['files'].items():
        target = path if name == '7z.exe' else path.with_name(name)
        with target.open('rb') as stream:
            observed[name] = hashlib.file_digest(stream, 'sha256').hexdigest()
        if observed[name] != expected:
            raise RuntimeError('Unapproved 7-Zip tool bytes: ' + name + '; see docs/build-toolchain.md')
    return path, {'version': policy['version'], 'path': str(path), 'files': observed,
                  'source_archive_sha256': policy['source_archive_sha256']}
