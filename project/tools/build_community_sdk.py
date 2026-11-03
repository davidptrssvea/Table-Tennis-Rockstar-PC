"""Build the optional matched Windows SDK; never touch a user's installed SDK."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

from setup_build import ROOT, build_tools, run

COMMIT = 'f5337cdc947ff6d4c4196737e2c807a48f2a1fc2'
PATCH = ROOT / 'project/sdk-patches/0001-community-windows.patch'
SUBMODULES = ('cli11 libmspack FFmpeg tomlplusplus simde xxHash spdlog fmt utfcpp '
              'imgui sdl3 o1heap tracy inja').split()


def materialize_windows_links(folder):
    # Git without Windows symlink privileges checks links out as small text files.
    output = subprocess.check_output(['git', 'ls-files', '-s'], cwd=folder, text=True)
    for line in output.splitlines():
        if not line.startswith('120000 '):
            continue
        path = folder / line.split('\t', 1)[1]
        if path.is_symlink():
            continue
        data = path.read_bytes()
        if len(data) > 256:
            continue
        text = data.decode('utf-8')
        if '\n' in text.strip():
            continue  # already materialized source
        target = (path.parent / text.strip()).resolve()
        if not target.is_relative_to(folder.resolve()) or not target.is_file():
            raise ValueError(f'Unsupported dependency link: {path}')
        shutil.copyfile(target, path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=ROOT / '.tools/community-sdk-source')
    parser.add_argument('--output', type=Path, default=ROOT / '.tools/community-sdk')
    parser.add_argument('--build', type=Path, help='Separate CMake build directory (optional)')
    parser.add_argument('--jobs', type=int, default=4)
    args = parser.parse_args()
    if not 1 <= args.jobs <= 16:
        raise ValueError('--jobs must be between 1 and 16')
    source, output = args.source.resolve(), args.output.resolve()
    cmake, ninja, clang, _, env = build_tools()
    if not source.exists():
        source.parent.mkdir(parents=True, exist_ok=True)
        run(['git', '-c', 'core.autocrlf=false', 'clone', '--depth', '1', '--branch',
             'v0.10.0', 'https://github.com/rexglue/rexglue-sdk.git', source])
    revision = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=source, text=True).strip()
    if revision != COMMIT:
        raise ValueError('Unsupported SDK source revision; no patch applied')
    common = ['git', 'apply', '--ignore-space-change']
    applied = subprocess.run(common + ['--reverse', '--check', str(PATCH)], cwd=source,
                             capture_output=True).returncode == 0
    if not applied:
        run(common + ['--check', PATCH], cwd=source)
        run(common + [PATCH], cwd=source)
    run(['git', 'submodule', 'update', '--init', '--depth', '1', '--jobs', str(args.jobs)] +
        ['thirdparty/' + name for name in SUBMODULES], cwd=source)
    materialize_windows_links(source / 'thirdparty/libmspack')
    build = args.build.resolve() if args.build else source / 'out/community-windows'
    run([cmake, '-S', source, '-B', build, '-G', 'Ninja',
         f'-DCMAKE_MAKE_PROGRAM={ninja}', f'-DCMAKE_C_COMPILER={clang.with_name("clang.exe")}',
         f'-DCMAKE_CXX_COMPILER={clang}', '-DCMAKE_BUILD_TYPE=Release',
         '-DCMAKE_C_FLAGS=-march=x86-64-v2', '-DCMAKE_CXX_FLAGS=-march=x86-64-v2',
         '-DREXGLUE_BUILD_TESTS=OFF', '-DREXGLUE_USE_D3D12=ON', '-DREXGLUE_USE_VULKAN=OFF',
         '-DREXGLUE_ENABLE_FIDELITYFX=OFF', '-DCMAKE_EXPORT_NO_PACKAGE_REGISTRY=ON',
         '-DCMAKE_EXPORT_PACKAGE_REGISTRY=OFF', f'-DCMAKE_INSTALL_PREFIX={output}'], env=env)
    run([cmake, '--build', build, '--parallel', args.jobs], env=env)
    run([cmake, '--install', build], env=env)
    # Written only after all binaries, libraries and headers were installed together.
    (output / '.tabletennis-community-sdk').write_text(json.dumps({
        'sdk_commit': COMMIT, 'patch_sha256': hashlib.sha256(PATCH.read_bytes()).hexdigest()
    }) + '\n', encoding='utf-8')
    print(f'Complete matched SDK: {output}')
    print('Use setup_build.py --sdk with this path to build the experimental network version.')


if __name__ == '__main__':
    main()
