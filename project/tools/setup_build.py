"""Build the game locally from the user's own verified retail ISO (Windows x64)."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import urllib.request
import zipfile

from disc_image import extract, validate
from build_updater import install_support
from patch_menu import patch as patch_menu
from patch_native import patch as patch_native

ROOT = Path(__file__).resolve().parents[2]
SDK_URL = 'https://github.com/rexglue/rexglue-sdk/releases/download/v0.10.0/rexglue-sdk-0.10.0-win-amd64.zip'
SDK_SHA256 = 'ad4485da853fbe932e9f5dbb643f9b5fe85dc73775ebbad4df6879fe6df0c11b'


def run(args, **kwargs):
    print('Running:', Path(str(args[0])).name, *[str(a) for a in args[1:]], flush=True)
    subprocess.run([str(a) for a in args], check=True, **kwargs)


def find_sdk(path, offline):
    if path:
        sdk = Path(path).resolve()
    else:
        engine_file = ROOT / 'project/engine.json'
        engine = json.loads(engine_file.read_text()) if engine_file.exists() else None
        url, checksum = (engine['url'], engine['sha256']) if engine else (SDK_URL, SDK_SHA256)
        sdk = ROOT / '.tools' / ('engine-' + checksum[:12] if engine else 'sdk')
        if not (sdk / 'bin/rexglue.exe').is_file():
            matches = list(sdk.glob('**/bin/rexglue.exe')) if sdk.exists() else []
            if len(matches) == 1:
                sdk = matches[0].parents[1]
            else:
                if offline:
                    raise ValueError('No cached engine. Supply --sdk PATH or permit the verified engine download.')
                archive = ROOT / '.tools' / ('engine-' + checksum[:12] + '.zip' if engine else 'rexglue-sdk-0.10.0.zip')
                archive.parent.mkdir(parents=True, exist_ok=True)
                if not archive.exists() or hashlib.sha256(archive.read_bytes()).hexdigest() != checksum:
                    temp = archive.with_suffix('.download')
                    print('Downloading the verified matched Windows engine.' if engine else 'Downloading the official ReXGlue SDK v0.10.0 (about 45 MB).', flush=True)
                    request = urllib.request.Request(url, headers={'User-Agent': 'TableTennis-LocalBuilder/1.5.0'})
                    with urllib.request.urlopen(request, timeout=120) as response, temp.open('wb') as output:
                        shutil.copyfileobj(response, output)
                    if hashlib.sha256(temp.read_bytes()).hexdigest() != checksum:
                        raise ValueError('SDK download checksum mismatch; file was not used')
                    temp.replace(archive)
                with zipfile.ZipFile(archive) as package:
                    if sum(i.file_size for i in package.infolist()) > 2 * 1024 ** 3:
                        raise ValueError('Unexpected SDK archive size')
                    for name in package.namelist():
                        target = (sdk / name).resolve()
                        if not target.is_relative_to(sdk.resolve()):
                            raise ValueError('Unsafe SDK archive entry')
                    package.extractall(sdk)
                matches = list(sdk.glob('**/bin/rexglue.exe'))
                if len(matches) != 1:
                    raise ValueError('SDK layout not recognized')
                sdk = matches[0].parents[1]
    if not (sdk / 'bin/rexglue.exe').is_file() or not (sdk / 'lib/cmake/rexglue').is_dir():
        raise ValueError('SDK path must contain bin/rexglue.exe and lib/cmake/rexglue')
    version = subprocess.check_output([str(sdk / 'bin/rexglue.exe'), '--version'], text=True)
    if version.strip() != '0.10.0':
        raise ValueError('This project requires ReXGlue SDK v0.10.0')
    return sdk


def find_windows_clang(candidates):
    """The matched engine requires the x64 MSVC ABI, not MinGW/libstdc++."""
    import re
    for candidate in candidates:
        if not candidate or not Path(candidate).is_file():
            continue
        try:
            version = subprocess.check_output([str(candidate), '--version'], text=True,
                                              stderr=subprocess.STDOUT, timeout=15)
        except (OSError, subprocess.SubprocessError):
            continue
        major = re.search(r'clang version (\d+)', version)
        target = re.search(r'^Target:\s*(\S+)', version, re.MULTILINE)
        if (major and int(major[1]) >= 19 and target
                and re.fullmatch(r'x86_64-(?:pc-)?windows-msvc(?:[\d.]*)', target[1])):
            return Path(candidate)
        print(f'Skipping incompatible Clang: {candidate} '
              f'(target {target[1] if target else "unknown"}; '
              'requires Clang 19+ targeting x86_64-pc-windows-msvc).', flush=True)
    raise ValueError('No compatible Windows x64 MSVC Clang 19+ found. '
                     'Install official Windows LLVM or Visual Studio Clang. '
                     'MinGW/GNU-targeting Clang is incompatible with the matched engine. See README.md.')


MIN_MSVC_TOOLSET = (14, 44)


def select_msvc_toolset(vs):
    """Return the newest installed MSVC toolset the matched engine can link against.

    Plain vcvars64.bat obeys Microsoft.VCToolsVersion.v143.default.txt, which an older
    side-by-side toolset (for example 14.38) can pin. The engine's prebuilt libraries
    need the 14.44+ STL, so the toolset is chosen explicitly instead.
    """
    versions = []
    for path in (Path(vs) / 'VC/Tools/MSVC').glob('*'):
        try:
            version = tuple(int(part) for part in path.name.split('.'))
        except ValueError:
            continue
        if path.is_dir() and version[:2] >= MIN_MSVC_TOOLSET:
            versions.append((version, path.name))
    if not versions:
        raise ValueError('MSVC toolset 14.44 or newer not found. Update Build Tools 2022 to 17.14 '
                         'in Visual Studio Installer. See README.md.')
    return max(versions)[1]


def build_tools():
    candidates = []
    vswhere = Path(os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')) / 'Microsoft Visual Studio/Installer/vswhere.exe'
    if vswhere.exists():
        result = subprocess.check_output([str(vswhere), '-latest', '-version', '[17.0,18.0)', '-products', '*', '-requires',
                                          'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath'], text=True).strip()
        if result:
            candidates.append(Path(result))
    for base in (os.environ.get('ProgramFiles(x86)', ''), os.environ.get('ProgramFiles', '')):
        candidates += [Path(base) / 'Microsoft Visual Studio/2022' / edition
                       for edition in ('BuildTools', 'Community', 'Professional', 'Enterprise')]
    vs = next((p for p in candidates if (p / 'VC/Auxiliary/Build/vcvars64.bat').exists()), None)
    clang_candidates = [shutil.which('clang++'),
                        str(Path(os.environ.get('ProgramFiles', r'C:\Program Files')) / 'LLVM/bin/clang++.exe')]
    if vs:
        clang_candidates += [str(vs / 'VC/Tools/Llvm/x64/bin/clang++.exe'),
                             str(vs / 'VC/Tools/Llvm/bin/clang++.exe')]
    if not vs:
        raise ValueError('Install Visual Studio 2022 C++ Build Tools (including Windows SDK, CMake and Ninja). See README.md.')
    clang = find_windows_clang(clang_candidates)
    cmake = shutil.which('cmake') or str(vs / 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe')
    ninja = shutil.which('ninja') or str(vs / 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe')
    csc = Path(os.environ.get('WINDIR', r'C:\Windows')) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    if not all(Path(p).is_file() for p in (cmake, ninja, csc)):
        raise ValueError('CMake, Ninja or .NET Framework C# compiler missing. See README.md.')
    vcvars = vs / 'VC/Auxiliary/Build/vcvars64.bat'
    toolset = select_msvc_toolset(vs)
    # cmd.exe needs its own outer quote pair around a quoted batch pathname.
    result = subprocess.run(f'cmd.exe /d /s /c ""{vcvars}" -vcvars_ver={toolset} >nul && set"',
                            capture_output=True, text=True, check=True)
    env = dict(os.environ)
    env.update(line.split('=', 1) for line in result.stdout.splitlines() if '=' in line and not line.startswith('='))
    if env.get('VCToolsVersion', '').rstrip('\\') != toolset:
        raise ValueError(f'Visual Studio did not activate MSVC toolset {toolset}. '
                         'Repair Build Tools 2022 in Visual Studio Installer. See README.md.')
    print(f'Using MSVC toolset {toolset}.', flush=True)
    return cmake, ninja, Path(clang), csc, env


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--iso', type=Path)
    parser.add_argument('--sdk', type=Path, help='Use an installed ReXGlue SDK v0.10.0; no SDK download')
    parser.add_argument('--no-download', action='store_true')
    parser.add_argument('--prepare-only', action='store_true', help='Extract, patch and generate without compiling')
    parser.add_argument('--jobs', type=int, default=min(8, os.cpu_count() or 2))
    parser.add_argument('--check-tools', action='store_true', help='Check the compiler environment without building')
    parser.add_argument('--validate-iso', action='store_true', help='Check the supported retail ISO without building')
    args = parser.parse_args()
    if sys.version_info < (3, 10) or os.name != 'nt' or platform.machine().lower() not in ('amd64', 'x86_64'):
        raise ValueError('Use 64-bit Python 3.10+ on Windows x64')
    if not 1 <= args.jobs <= 32:
        raise ValueError('--jobs must be between 1 and 32')
    if args.check_tools:
        build_tools()
        print('Build tools are ready.', flush=True)
        return
    if not args.iso:
        raise ValueError('Select your retail ISO')
    iso = args.iso.resolve(strict=True)
    if iso.suffix.lower() != '.iso':
        raise ValueError('Select a retail Xbox 360 ISO file')
    if args.validate_iso:
        validate(iso)
        return
    # Check compilers first, before downloading or generating game material.
    tools = build_tools() if not args.prepare_only else None
    print('STAGE:Preparing game build support', flush=True)
    sdk = find_sdk(args.sdk, args.no_download)
    print('STAGE:Preparing your local game files', flush=True)
    extract(iso, ROOT)
    patch_menu(ROOT / 'dist/assets/resource/flash/flash.rpf', online=(sdk / '.tabletennis-community-sdk').is_file())
    print('STAGE:Generating your local game', flush=True)
    # Existing generated files may contain native edits from an older release.
    # Recreate the supported input before applying the current hash-checked rules.
    run([sdk / 'bin/rexglue.exe', 'codegen', ROOT / 'project/tabletennis_manifest.toml', '--ignore-stamp'], cwd=ROOT)
    patch_native(ROOT / 'project/generated/default')
    if args.prepare_only:
        print('Preparation complete. Original-derived files remain local and ignored by Git.', flush=True)
        return
    cmake, ninja, clang, csc, env = tools
    run([cmake, '-S', ROOT / 'project', '-B', ROOT / 'build', '-G', 'Ninja',
         f'-DCMAKE_MAKE_PROGRAM={ninja}', f'-DCMAKE_C_COMPILER={clang.with_name("clang.exe")}',
         f'-DCMAKE_CXX_COMPILER={clang}', '-DCMAKE_BUILD_TYPE=Release', f'-DCMAKE_PREFIX_PATH={sdk}'], env=env)
    print('STAGE:Building your game - please keep this window open', flush=True)
    run([cmake, '--build', ROOT / 'build', '--parallel', args.jobs], env=env)
    print('STAGE:Finishing installation', flush=True)
    dist = ROOT / 'dist'
    shutil.copy2(ROOT / 'build/tabletennis.exe', dist / 'tabletennis.exe')
    shutil.copy2(ROOT / 'project/assets/tabletennis.ico', dist / 'tabletennis.ico')
    run([csc, '/nologo', '/target:winexe', '/optimize', f'/out:{dist / "tabletennis_launcher.exe"}',
         f'/win32icon:{dist / "tabletennis.ico"}',
         f'/lib:{csc.parent / "WPF"}', '/r:PresentationCore.dll,PresentationFramework.dll,WindowsBase.dll,System.dll,System.Xaml.dll',
         ROOT / 'project/launcher/Launcher.cs', ROOT / 'project/launcher/HostSession.cs', ROOT / 'project/launcher/GatewayLeaseRouter.cs'])
    for name in ('rexruntime.dll', 'rexgpu-xenos.dll', 'TracyClient.dll'):
        shutil.copy2(sdk / 'bin' / name, dist / name)
    shutil.copytree(ROOT / 'licenses', dist / 'licenses', dirs_exist_ok=True)
    if (sdk / 'licenses').exists():
        shutil.copytree(sdk / 'licenses', dist / 'licenses/SDK', dirs_exist_ok=True)
    engine_marker = dist / 'community-engine.json'
    if (sdk / '.tabletennis-community-sdk').is_file():
        shutil.copy2(sdk / '.tabletennis-community-sdk', engine_marker)
    elif engine_marker.exists():
        engine_marker.unlink()
    # Preserve existing graphics settings; write the current user's ISO only on this PC.
    settings = dist / 'settings.ini'
    if not settings.exists():
        settings.write_text(f'iso_path={iso}\nresolution=1080p\nframerate=60\nfullscreen=false\nshow_fps=false\naa=none\npost=fsr\n', encoding='utf-8')
    else:
        lines = [line for line in settings.read_text(encoding='utf-8-sig').splitlines()
                 if not line.lower().startswith('iso_path=')]
        settings.write_text(f'iso_path={iso}\n' + '\n'.join(lines) + '\n', encoding='utf-8')
    print('STAGE:Installing update support', flush=True)
    install_support(ROOT, dist, csc, args.no_download)
    print(f'Build complete. Start {dist / "tabletennis_launcher.exe"}. Keep this local build private.', flush=True)


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print(f'ERROR: {error}\nSetup stopped. No security settings were changed. See README.md for prerequisites.', file=sys.stderr)
        sys.exit(1)
