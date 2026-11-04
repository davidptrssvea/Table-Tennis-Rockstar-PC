"""Verified local update builder and journaled file activation (standard library).

The UI verifies signatures before downloading. This process verifies them again
with the pinned-key updater EXE before extracting or running any release code.
No ISO/game-derived material is downloaded or uploaded.
"""
import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import struct
import subprocess
import sys
import time
import uuid
import zipfile

REPOSITORY = 'davidptrssvea/Table-Tennis-Rockstar-PC'
PROTOCOL = 1
RESERVED = {'CON', 'PRN', 'AUX', 'NUL', *('COM%d' % i for i in range(1, 10)),
            *('LPT%d' % i for i in range(1, 10))}


def digest(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def atomic_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.new')
    with temporary.open('w', encoding='utf-8', newline='\n') as stream:
        json.dump(value, stream, sort_keys=True)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def no_duplicates(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON field: ' + key)
        result[key] = value
    return result


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'), object_pairs_hook=no_duplicates)


def relative_path(name):
    if not isinstance(name, str) or not name or '\\' in name or '\0' in name:
        raise ValueError('Invalid relative path')
    parts = name.split('/')
    if any(not p or p in ('.', '..') or ':' in p or p.rstrip(' .') != p or
           any(ord(c) < 32 for c in p) or p.split('.')[0].upper() in RESERVED for p in parts):
        raise ValueError('Unsafe path: ' + name)
    path = PurePosixPath(name)
    if path.is_absolute():
        raise ValueError('Absolute path is not allowed')
    return Path(*parts)


def linked(path):
    path = Path(path)
    return path.is_symlink() or (hasattr(path, 'is_junction') and path.is_junction())


def under(root, relative):
    root = Path(root).resolve(strict=True)
    path = root / relative_path(relative)
    for parent in [path, *path.parents]:
        if parent == root:
            break
        if linked(parent):
            raise ValueError('Reparse points are not permitted in update paths')
    if not path.resolve().is_relative_to(root):
        raise ValueError('Update path escapes installation')
    return path


def install_root(value):
    path = Path(value).absolute()
    if linked(path):
        raise ValueError('Choose the actual installation folder, not a directory link')
    path = path.resolve(strict=True)
    if path.parent == path or not (path / 'dist/settings.ini').is_file():
        raise ValueError('Select an existing Table Tennis installation')
    under(path, '.updates/transaction.json')
    return path


def extract_checked(archive, destination, maximum):
    destination = Path(destination)
    destination.mkdir(parents=True, exist_ok=False)
    seen, total = set(), 0
    with zipfile.ZipFile(archive) as package:
        if len(package.infolist()) > 20000:
            raise ValueError('Too many archive entries')
        for entry in package.infolist():
            name = entry.filename.rstrip('/') if entry.is_dir() else entry.filename
            relative_path(name)
            key = name.casefold()
            if key in seen:
                raise ValueError('Duplicate/case-colliding archive path')
            seen.add(key)
            kind = stat.S_IFMT(entry.external_attr >> 16)
            if kind not in (0, stat.S_IFREG, stat.S_IFDIR) or entry.flag_bits & 1:
                raise ValueError('Linked, special or encrypted archive entry')
            total += entry.file_size
            if entry.file_size < 0 or total > maximum:
                raise ValueError('Archive expands beyond its allowed size')
            target = under(destination, name)
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with package.open(entry) as source, target.open('xb') as output:
                copied = 0
                for block in iter(lambda: source.read(1024 * 1024), b''):
                    copied += len(block)
                    if copied > entry.file_size:
                        raise ValueError('Archive entry length mismatch')
                    output.write(block)
                if copied != entry.file_size:
                    raise ValueError('Truncated archive entry')


def validate_manifest(manifest):
    if manifest.get('schema') != PROTOCOL or manifest.get('repository') != REPOSITORY:
        raise ValueError('Unsupported update manifest')
    if not re.fullmatch(r'\d+\.\d+\.\d+', str(manifest.get('version', ''))):
        raise ValueError('Invalid stable release version')
    if manifest.get('minimum_updater', 999) > PROTOCOL:
        raise ValueError('This release needs a newer updater')
    for name in ('source', 'sdk'):
        asset = manifest.get(name, {})
        filename = asset.get('asset', '')
        if relative_path(filename).name != filename or not filename.endswith('.zip'):
            raise ValueError('Invalid release asset name')
        if not re.fullmatch('[0-9a-f]{64}', str(asset.get('sha256', ''))):
            raise ValueError('Invalid asset checksum')
        if type(asset.get('size')) is not int or not 1 <= asset['size'] <= 1024 * 1024 * 1024:
            raise ValueError('Invalid asset size')
    return manifest


def verify_package(manifest_path, signature, source, sdk, verifier):
    # The trusted installed EXE contains the public key, never a downloaded key.
    subprocess.run([str(verifier), '--verify-manifest', str(manifest_path), str(signature)], check=True)
    manifest = validate_manifest(read_json(manifest_path))
    for name, path in (('source', source), ('sdk', sdk)):
        path = Path(path)
        if path.stat().st_size != manifest[name]['size'] or digest(path) != manifest[name]['sha256']:
            raise ValueError('The ' + name + ' package failed checksum verification')
    return manifest


@contextmanager
def exclusive(root):
    path = under(root, '.updates/backend.lock')
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('a+b') as lock:
        if not path.stat().st_size:
            lock.write(b'0')
            lock.flush()
        lock.seek(0)
        if os.name == 'nt':
            import msvcrt
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            yield
        finally:
            lock.seek(0)
            if os.name == 'nt':
                msvcrt.locking(lock.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(lock.fileno(), fcntl.LOCK_UN)


def journal_paths(root):
    return under(root, '.updates/transaction.json'), under(root, '.updates/in-progress')


def rollback_journal(root, journal):
    if journal.get('root') != str(root) or journal.get('schema') != 1:
        raise ValueError('Recovery journal belongs to another installation')
    # Validate every path before changing any file.
    operations = []
    seen = set()
    for operation in journal['operations']:
        name, saved = operation['path'], operation['backup']
        if not name.startswith('dist/') or not saved.startswith('.updates/backups/'):
            raise ValueError('Invalid recovery paths')
        if name.casefold() in seen or type(operation.get('had_original')) is not bool or not re.fullmatch('[0-9a-f]{64}', operation.get('new_sha256', '')):
            raise ValueError('Invalid recovery operation')
        seen.add(name.casefold())
        operations.append((operation, under(root, name), under(root, saved)))
    for operation, target, backup in operations:
        if not backup.is_file() and not operation['had_original'] and target.exists() and digest(target) != operation['new_sha256']:
            raise ValueError('A new file was changed outside the update; automatic removal refused')
        if operation['had_original'] and not backup.is_file() and not target.is_file():
            raise ValueError('An original file and its backup are both missing')
    for operation, target, backup in reversed(operations):
        if backup.is_file():
            target.parent.mkdir(parents=True, exist_ok=True)
            os.replace(backup, target)
        elif not operation['had_original'] and target.exists():
            if digest(target) != operation['new_sha256']:
                raise ValueError('A new file was changed outside the update; automatic removal refused')
            target.unlink()
        elif operation['had_original'] and not target.is_file():
            raise ValueError('An original file and its backup are both missing')


def recover(root):
    journal_path, marker = journal_paths(root)
    if journal_path.exists():
        journal = read_json(journal_path)
        if journal['state'] != 'committed':
            rollback_journal(root, journal)
            if journal['state'] == 'rolling_back':
                under(root, '.updates/previous.json').unlink(missing_ok=True)
        else:
            atomic_json(under(root, '.updates/previous.json'), journal)
        journal_path.unlink()
    # With no journal, an interrupted download/build never touched installed files.
    marker.unlink(missing_ok=True)


def activate(root, staged_dist, failure_hook=lambda phase, name: None):
    staged_dist = Path(staged_dist).resolve(strict=True)
    if not staged_dist.is_relative_to(under(root, '.updates/work')):
        raise ValueError('New files must come from the installation staging folder')
    token = uuid.uuid4().hex
    operations = []
    for source in sorted(staged_dist.rglob('*')):
        if linked(source):
            raise ValueError('Linked staged file')
        if not source.is_file():
            continue
        name = source.relative_to(staged_dist).as_posix()
        # Mutable user data stays in place, including ISOs stored inside dist.
        if name == 'settings.ini' or name.startswith(('logs/', 'user_data/', 'cache/')):
            continue
        target = under(root, 'dist/' + name)
        checksum = digest(source)
        if target.is_file() and digest(target) == checksum:
            continue
        backup = '.updates/backups/' + token + '/dist/' + name
        under(root, backup)
        if target.exists() and not target.is_file():
            raise ValueError('A directory conflicts with an updated file')
        operations.append(dict(path='dist/' + name, backup=backup,
                               had_original=target.is_file(), new_sha256=checksum))
    if not operations:
        raise ValueError('No files to activate')
    journal = dict(schema=1, root=str(root), state='applying', operations=operations)
    journal_path, marker = journal_paths(root)
    if not marker.exists():
        atomic_json(marker, dict(state='applying'))
    atomic_json(journal_path, journal)
    try:
        for operation in operations:
            target = under(root, operation['path'])
            backup = under(root, operation['backup'])
            backup.parent.mkdir(parents=True, exist_ok=True)
            if operation['had_original']:
                os.replace(target, backup)
            failure_hook('backed_up', operation['path'])
            source = staged_dist / relative_path(operation['path'][5:])
            target.parent.mkdir(parents=True, exist_ok=True)
            os.replace(source, target)
            failure_hook('installed', operation['path'])
        journal['state'] = 'committed'
        atomic_json(journal_path, journal)
    except Exception:
        rollback_journal(root, journal)
        journal_path.unlink()
        marker.unlink(missing_ok=True)
        raise
    atomic_json(under(root, '.updates/previous.json'), journal)
    journal_path.unlink()
    marker.unlink(missing_ok=True)


def restore_previous(root):
    if journal_paths(root)[0].exists():
        raise ValueError('Recover the interrupted installation first')
    previous = under(root, '.updates/previous.json')
    journal = read_json(previous)
    journal['state'] = 'rolling_back'
    transaction, marker = journal_paths(root)
    atomic_json(marker, dict(state='rolling_back'))
    atomic_json(transaction, journal)
    rollback_journal(root, journal)
    previous.unlink()
    transaction.unlink()
    marker.unlink(missing_ok=True)


def check_cancel(root):
    if under(root, '.updates/cancel.request').exists():
        raise InterruptedError('Update cancelled; the installed version was kept')


def run_build(command, work, root):
    process = subprocess.Popen([str(a) for a in command], cwd=work)
    try:
        while process.poll() is None:
            try:
                check_cancel(root)
            except InterruptedError:
                if os.name == 'nt':
                    subprocess.run(['taskkill.exe', '/PID', str(process.pid), '/T', '/F'],
                                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                else:
                    process.terminate()
                process.wait(timeout=15)
                raise
            time.sleep(.2)
        if process.returncode:
            raise subprocess.CalledProcessError(process.returncode, command)
    finally:
        if process.poll() is None:
            process.terminate()
            process.wait(timeout=15)


def build_release(work, root, iso, sdk, allow_tools):
    old_tools = root / '.tools'
    for name in ('python-3.14.8-embed-amd64.zip', 'python314'):
        source, target = old_tools / name, work / '.tools' / name
        if source.is_file():
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
        elif source.is_dir():
            shutil.copytree(source, target, dirs_exist_ok=True)
    # Execute the signed release's Python builder directly. No PowerShell
    # execution-policy changes are needed for updates.
    script = work / 'project/tools/setup_build.py'
    invoke = ('import runpy,sys;sys.path.insert(0,sys.argv[1]);'
              'sys.argv=sys.argv[2:];runpy.run_path(sys.argv[0],run_name="__main__")')
    command = [sys.executable, '-I', '-B', '-c', invoke, script.parent, script]
    check = subprocess.run([str(a) for a in command + ['--check-tools']], cwd=work)
    if check.returncode:
        if not allow_tools:
            raise ValueError('Required build components are missing. Select the component-installation checkbox and retry')
        verifier = under(root, '.updates/runner/tabletennis_updater.exe')
        print('STAGE:Installing required Microsoft build components', flush=True)
        result = subprocess.run([str(verifier), '--install-tools', '--root', str(root)])
        if result.returncode in (3010, 1641):
            raise ValueError('Restart Windows, then update again. The installed game was kept')
        if result.returncode:
            raise ValueError('Microsoft component installation did not complete; the installed game was kept')
        subprocess.run([str(a) for a in command + ['--check-tools']], cwd=work, check=True)
    run_build(command + ['--iso', iso, '--sdk', sdk], work, root)


def verify_dist(dist, sdk):
    required = ('tabletennis.exe', 'tabletennis_launcher.exe', 'tabletennis_updater.exe',
                'update_backend.py', 'install.json', 'assets/resource/flash/flash.rpf',
                'python/python.exe', 'server/ttlink_server.py', 'community-engine.json')
    for name in required:
        if not (dist / name).is_file():
            raise ValueError('The build did not produce ' + name)
    with (dist / 'tabletennis.exe').open('rb') as stream:
        if stream.read(2) != b'MZ':
            raise ValueError('Game executable is invalid')
        stream.seek(0x3c)
        offset = struct.unpack('<I', stream.read(4))[0]
        stream.seek(offset)
        if stream.read(6) != b'PE\0\0\x64\x86':
            raise ValueError('Game executable is not Windows x64')
    for name in ('rexruntime.dll', 'rexgpu-xenos.dll', 'TracyClient.dll'):
        if digest(dist / name) != digest(sdk / 'bin' / name):
            raise ValueError('Mixed game/engine installation: ' + name)
    if (dist / 'community-engine.json').read_bytes() != (sdk / '.tabletennis-community-sdk').read_bytes():
        raise ValueError('SDK build marker mismatch')


def apply_update(root, manifest, signature, source_zip, sdk_zip, verifier, allow_tools=False,
                 builder=build_release):
    release = verify_package(manifest, signature, source_zip, sdk_zip, verifier)
    recover(root)
    installed = read_json(root / 'dist/install.json')
    version = lambda text: tuple(map(int, text.split('.')))
    if version(release['version']) <= version(installed['version']):
        raise ValueError('Only a newer signed release can be installed')
    root_updates = under(root, '.updates/work')
    root_updates.mkdir(parents=True, exist_ok=True)
    work = root_updates / uuid.uuid4().hex[:12]
    marker = journal_paths(root)[1]
    atomic_json(marker, dict(state='building', version=release['version']))
    try:
        check_cancel(root)
        print('STAGE:Extracting verified update source', flush=True)
        extract_checked(source_zip, work, 64 * 1024 * 1024)
        metadata = read_json(work / 'project/release.json')
        if metadata['version'] != release['version'] or metadata['repository'] != REPOSITORY:
            raise ValueError('Source version does not match the signed manifest')
        sdk = work / '.tools/update-sdk'
        print('STAGE:Preparing the matched game engine', flush=True)
        extract_checked(sdk_zip, sdk, 2 * 1024 * 1024 * 1024)
        sdk_marker = read_json(sdk / '.tabletennis-community-sdk')
        patch_hash = digest(work / 'project/sdk-patches/0001-community-windows.patch')
        if sdk_marker['patch_sha256'] != patch_hash:
            raise ValueError('Release source and engine patch do not match')
        settings = (root / 'dist/settings.ini').read_text(encoding='utf-8-sig')
        iso = next((line.split('=', 1)[1].strip().strip('"') for line in settings.splitlines()
                    if line.lower().startswith('iso_path=')), '')
        iso = Path(iso)
        if not iso.is_absolute() or not iso.is_file():
            raise ValueError('Your ISO was not found. Select it in the launcher before updating')
        check_cancel(root)
        print('STAGE:Building the updated game locally from your ISO', flush=True)
        builder(work, root, iso, sdk, allow_tools)
        check_cancel(root)
        dist = work / 'dist'
        verify_dist(dist, sdk)
        atomic_json(dist / 'install.json', dict(version=release['version'], repository=REPOSITORY,
                    update_protocol=PROTOCOL, source_sha256=release['source']['sha256']))
        # Repeat the graceful close check in case someone opened the old game during the build.
        subprocess.run([str(verifier), '--close-game', '--root', str(root)], check=True)
        check_cancel(root)
        print('STAGE:Installing verified files', flush=True)
        activate(root, dist)
        print('STAGE:Update complete', flush=True)
        # Only this freshly generated workspace is removed, never install data.
        # Refuse cleanup if a build unexpectedly created a reparse point.
        if work.parent.resolve() == root_updates.resolve() and work.resolve().is_relative_to(root_updates.resolve()):
            if not any(linked(p) for p in work.rglob('*')):
                try:
                    shutil.rmtree(work)
                except OSError as error:
                    print('WARNING:Update succeeded; staging cleanup deferred: ' + str(error), flush=True)
        return release['version']
    except Exception:
        # Activation handles its own rollback; a failed build never changes dist.
        if not journal_paths(root)[0].exists():
            marker.unlink(missing_ok=True)
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=('apply', 'recover', 'rollback'))
    parser.add_argument('--root', required=True, type=Path)
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--signature', type=Path)
    parser.add_argument('--source', type=Path)
    parser.add_argument('--sdk', type=Path)
    parser.add_argument('--verifier', type=Path)
    parser.add_argument('--allow-tools', action='store_true')
    args = parser.parse_args()
    root = install_root(args.root)
    with exclusive(root):
        if args.action == 'recover':
            recover(root)
        elif args.action == 'rollback':
            restore_previous(root)
        else:
            if not all((args.manifest, args.signature, args.source, args.sdk, args.verifier)):
                raise ValueError('Verified update package arguments are required')
            apply_update(root, args.manifest, args.signature, args.source, args.sdk,
                         args.verifier, args.allow_tools)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print('UPDATE_ERROR:' + str(error), flush=True)
        sys.exit(1)
