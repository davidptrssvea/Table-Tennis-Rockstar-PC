"""Compile/install game-free update support; the signing key stays outside the repo."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import urllib.request
import zipfile

PYTHON_URL = 'https://www.python.org/ftp/python/3.14.8/python-3.14.8-embed-amd64.zip'
PYTHON_SHA = 'a93abe456ab01bd96d7a085b3cdb6566b3063f4241360d114142fbdb07f0a310'


def install_support(root, dist, csc, no_download=False):
    root, dist, csc = Path(root), Path(dist), Path(csc)
    metadata = json.loads((root / 'project/release.json').read_text(encoding='utf-8-sig'))
    if metadata.get('repository') != 'davidptrssvea/Table-Tennis-Rockstar-PC' or not re.fullmatch(r'\d+\.\d+\.\d+', metadata.get('version', '')):
        raise ValueError('Invalid release metadata')
    source = root / 'project/updater'
    subprocess.run([str(csc), '/nologo', '/target:winexe', '/platform:x64', '/optimize', '/codepage:65001',
                    '/out:' + str(dist / 'tabletennis_updater.exe'),
                    '/win32icon:' + str(root / 'project/assets/tabletennis.ico'),
                    '/resource:' + str(source / 'release-public-key.xml') + ',update-public-key.xml',
                    '/lib:' + str(csc.parent / 'WPF'),
                    '/r:PresentationCore.dll,PresentationFramework.dll,WindowsBase.dll,System.dll,System.Xaml.dll,System.Web.Extensions.dll',
                    str(source / 'UpdateCore.cs'), str(source / 'Updater.cs')], check=True)
    shutil.copy2(source / 'update_backend.py', dist / 'update_backend.py')
    shutil.copytree(root / 'server', dist / 'server', dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
    cache = root / '.tools/python-3.14.8-embed-amd64.zip'
    if not cache.exists() or hashlib.sha256(cache.read_bytes()).hexdigest() != PYTHON_SHA:
        if no_download:
            raise ValueError('The official embedded Python archive is not cached; update support needs it')
        cache.parent.mkdir(parents=True, exist_ok=True)
        temporary = cache.with_suffix('.download')
        urllib.request.urlretrieve(PYTHON_URL, temporary)
        if hashlib.sha256(temporary.read_bytes()).hexdigest() != PYTHON_SHA:
            raise ValueError('Embedded Python download checksum failed')
        temporary.replace(cache)
    runtime = dist / 'python'
    runtime.mkdir(exist_ok=True)
    with zipfile.ZipFile(cache) as package:
        package.extractall(runtime)  # Pinned official archive, not a release-supplied ZIP.
    (runtime / 'python314._pth').write_text('python314.zip\n.\n', encoding='ascii')
    (dist / 'install.json').write_text(json.dumps(metadata, sort_keys=True) + '\n', encoding='utf-8')
    (dist / 'install-version.txt').write_text(metadata['version'] + '\n', encoding='ascii')


if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--dist', type=Path)
    parser.add_argument('--no-download', action='store_true')
    args = parser.parse_args()
    csc = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    install_support(args.root, args.dist or args.root / 'dist', csc, args.no_download)
