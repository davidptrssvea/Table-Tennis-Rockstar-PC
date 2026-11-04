"""Audit and package the explicit public source allowlist; never package dist."""
import argparse
import hashlib
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
ROOT_FILES = {'README.md', 'LICENSE', 'CHANGELOG.md', '.gitignore', 'setup.cmd', 'setup.ps1'}
FIXED_FILES = {
    'project/assets/tabletennis.ico', 'licenses/Icon/CREDITS.txt',
    'project/CMakeLists.txt', 'project/tabletennis_manifest.toml', 'project/overrides.toml',
    'project/cmake/rexglue.cmake', 'project/launcher/Launcher.cs',
    'project/src/main.cpp', 'project/src/tabletennis_app.h', 'project/src/offline_menu_device.h',
    'project/src/intro_skip.cpp', 'project/src/intro_skip.h',
    'project/src/frame_pacer.cpp', 'project/src/frame_pacer.h',
    'project/src/frame_schedule.h', 'tests/frame_pacing_test.cpp',
    'project/src/pc_features.cpp', 'project/src/pc_features.h',
    'project/src/pc_settings.cpp', 'project/src/pc_settings.h', 'project/src/settings_store.h',
    'project/tools/menu_settings.py',
    'project/src/achievement_ui.cpp', 'project/src/achievement_ui.h',
    'project/src/button_prompts.cpp', 'project/src/button_prompts.h', 'project/src/button_prompt_art.inc',
    'project/src/system_link.cpp', 'project/src/system_link.h',
    'licenses/Table-Tennis-Linux/LICENSE.txt', 'licenses/Table-Tennis-Linux/NOTICE.txt',
    'project/tools/menu_online.py', 'project/tools/build_community_sdk.py',
    'project/sdk-patches/0001-community-windows.patch',
    'server/ttlink_server.py', 'server/README.md',
    'tests/test_community_network.py', 'tests/community_network_probe.cpp',
    'tests/test_online_validation.py',
    'project/src/screen_layout.h', 'tests/pc_features_test.cpp',
    'project/src/stats_window.cpp', 'project/src/stats_window.h',
    'project/sdk-overrides/rex/platform/fpscr.h', 'project/sdk-overrides/rex/ppc/context.h',
    'project/sdk-overrides/rex_app.cpp',
    'project/tools/disc_image.py', 'project/tools/patch_menu.py', 'project/tools/patch_native.py',
    'project/tools/native_edits.json', 'project/tools/setup_build.py', 'project/tools/package_source.py',
    'tests/test_local_build.py', 'licenses/ReXGlue/LICENSE.txt', 'licenses/SDL3/LICENSE.txt',
    'reports/LOCAL_BUILD.md',
    'reports/ONLINE_REVIEW.md',
    'project/installer/Installer.cs', 'project/tools/bootstrap.ps1', 'project/tools/build_installer.ps1',
}


def audit(root=ROOT, tracked=False):
    root = Path(root)
    allowed = ROOT_FILES | FIXED_FILES
    missing = sorted(name for name in allowed if not (root / name).is_file())
    if missing:
        raise ValueError(f'Missing public source files: {missing}')
    if tracked:
        names = set(filter(None, subprocess.check_output(['git', 'ls-files', '-z'], cwd=root).decode().split('\0')))
        if names != allowed:
            raise ValueError(f'Git index differs from source allowlist: extra={names - allowed}, missing={allowed - names}')
    for name in allowed:
        data = (root / name).read_bytes()
        if data.startswith((b'MZ', b'RPF0', b'XEX2')) or (b'DEFINE_REX_FUNC' + b'(sub_') in data:
            raise ValueError(f'Original-derived binary or generated function in public file: {name}')
    return sorted(allowed)


def package(output):
    files = audit()
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    path = output / 'Table-Tennis-Local-Build-v1.1.2.zip'
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in files:
            archive.write(ROOT / name, name)
    with zipfile.ZipFile(path) as archive:
        if archive.testzip() or set(archive.namelist()) != set(files):
            raise ValueError('Source package verification failed')
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    (output / 'SHA256SUMS.txt').write_text(f'{digest}  {path.name}\n', encoding='ascii')
    print(f'Source-only package verified: {len(files)} files, {path.stat().st_size:,} bytes; SHA-256 {digest}')


def verify_zip(path):
    files = audit()
    with zipfile.ZipFile(path) as archive:
        if len(archive.namelist()) != len(files) or set(archive.namelist()) != set(files) or archive.testzip():
            raise ValueError('Installer payload differs from the public source allowlist')
        for name in files:
            if archive.read(name) != (ROOT / name).read_bytes():
                raise ValueError(f'Installer payload does not match reviewed source: {name}')
    print(f'PASS: installer payload contains exactly {len(files)} reviewed public source files.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path)
    parser.add_argument('--audit-index', action='store_true')
    parser.add_argument('--verify-zip', type=Path)
    args = parser.parse_args()
    if args.audit_index:
        print(f'PASS: {len(audit(tracked=True))} tracked files match the public source allowlist.')
    if args.output:
        package(args.output)
    if args.verify_zip:
        verify_zip(args.verify_zip)
