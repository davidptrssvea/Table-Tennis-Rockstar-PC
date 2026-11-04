"""Prepare source-only + game-free engine assets and their signed release manifest.

Does not publish a release. The private signing key is DPAPI-protected outside
the repository; sign_update.ps1 uses it without adding it to any package.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile

from package_source import ROOT, audit


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def prepare(output, sdk, notes):
    metadata = json.loads((ROOT / 'project/release.json').read_text(encoding='utf-8-sig'))
    version = metadata['version']
    output.mkdir(parents=True, exist_ok=True)
    source = output / f'Table-Tennis-Source-v{version}.zip'
    marker = json.loads((sdk / '.tabletennis-community-sdk').read_text())
    if marker['patch_sha256'] != sha(ROOT / 'project/sdk-patches/0001-community-windows.patch'):
        raise ValueError('Build the matched community SDK before preparing update assets')
    engine = output / f'Table-Tennis-Engine-Windows-v{version}.zip'
    count = 0
    with zipfile.ZipFile(engine, 'w', zipfile.ZIP_DEFLATED, compresslevel=3) as package:
        for path in sorted(sdk.rglob('*')):
            if path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction()):
                raise ValueError('Linked SDK entry: ' + str(path))
            if not path.is_file() or path.suffix.lower() == '.pdb':
                continue
            name = path.relative_to(sdk).as_posix()
            if name != '.tabletennis-community-sdk' and name.split('/')[0] not in {'bin','cmake','include','lib','licenses','share'}:
                raise ValueError('Unexpected SDK file: ' + name)
            if path.suffix.lower() in {'.iso','.xex','.rpf','.xeck'} or path.name.lower().startswith('tabletennis'):
                raise ValueError('Game material is not permitted in engine assets')
            if path.suffix.lower() == '.exe' and path.name != 'rexglue.exe':
                raise ValueError('Unexpected SDK executable: ' + name)
            package.write(path, name)
            count += 1
    engine_info = dict(url=f'https://github.com/{metadata["repository"]}/releases/download/v{version}/{engine.name}',
                       sha256=sha(engine), size=engine.stat().st_size)
    (ROOT / 'project/engine.json').write_text(json.dumps(engine_info, indent=2) + '\n', encoding='utf-8')
    with zipfile.ZipFile(source, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as package:
        for name in audit():
            package.write(ROOT / name, name)
    release = dict(schema=1, minimum_updater=1, repository=metadata['repository'], version=version,
                   notes=notes, source=dict(asset=source.name, sha256=sha(source), size=source.stat().st_size),
                   sdk=dict(asset=engine.name, sha256=sha(engine), size=engine.stat().st_size))
    manifest = output / 'tabletennis-update.json'
    manifest.write_text(json.dumps(release, sort_keys=True, indent=2) + '\n', encoding='utf-8')
    subprocess.run(['powershell.exe','-NoProfile','-File',str(ROOT/'project/tools/sign_update.ps1'),
                    '-Manifest',str(manifest)], check=True)
    for path in (source,engine):
        with zipfile.ZipFile(path) as package:
            if package.testzip():
                raise ValueError('Archive verification failed')
    (output/'SHA256SUMS.txt').write_text(''.join(sha(p)+'  '+p.name+'\n' for p in
        (source,engine,manifest,manifest.with_suffix('.json.sig'))),encoding='ascii')
    print(f'Prepared {len(audit())} public source files and {count} game-free SDK files. Nothing was published.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--sdk', required=True, type=Path)
    parser.add_argument('--notes', required=True, type=Path)
    args = parser.parse_args()
    prepare(args.output.resolve(),args.sdk.resolve(strict=True),args.notes.read_text(encoding='utf-8'))
