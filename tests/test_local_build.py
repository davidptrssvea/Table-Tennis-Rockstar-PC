import hashlib
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'project/tools'))
from disc_image import DiscImage, MAGIC, extract
from patch_native import apply_edits, verify_function_table
from patch_menu import patch
from package_source import audit, verify_zip
import setup_build
import zipfile


def synthetic_iso(directory_sector=40, directory_size=64):
    image = bytearray(44 * 2048)
    descriptor = 32 * 2048
    image[descriptor:descriptor + 20] = MAGIC
    image[descriptor + 2048 - 20:descriptor + 2048] = MAGIC
    struct.pack_into('<II', image, descriptor + 20, directory_sector, directory_size)
    name = b'sample.txt'
    struct.pack_into('<HHIIBB', image, 40 * 2048, 0, 0, 42, 3, 0, len(name))
    image[40 * 2048 + 14:40 * 2048 + 14 + len(name)] = name
    image[42 * 2048:42 * 2048 + 3] = b'abc'
    return image


class WindowsCompilerTests(unittest.TestCase):
    def test_mingw_on_path_falls_back_to_msvc_llvm(self):
        with tempfile.TemporaryDirectory() as directory:
            bad = Path(directory) / 'retcomm-clang.exe'
            good = Path(directory) / 'llvm-clang.exe'
            bad.touch(); good.touch()
            def output(args, **kwargs):
                target = 'x86_64-w64-windows-gnu' if args[0] == str(bad) else 'x86_64-pc-windows-msvc'
                return 'clang version 22.1.8\nTarget: ' + target + '\n'
            with mock.patch.object(setup_build.subprocess, 'check_output', side_effect=output):
                self.assertEqual(setup_build.find_windows_clang([bad, good]), good)

    def test_incompatible_targets_and_old_clang_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            candidate = Path(directory) / 'clang.exe'; candidate.touch()
            for version, target in [(22, 'x86_64-w64-windows-gnu'),
                                    (22, 'i686-pc-windows-msvc'),
                                    (22, 'aarch64-pc-windows-msvc'),
                                    (18, 'x86_64-pc-windows-msvc')]:
                with self.subTest(version=version, target=target):
                    with mock.patch.object(setup_build.subprocess, 'check_output',
                                           return_value=f'clang version {version}.0\nTarget: {target}\n'):
                        with self.assertRaisesRegex(ValueError, 'MSVC Clang'):
                            setup_build.find_windows_clang([candidate])

    def test_broken_candidate_does_not_hide_working_compiler(self):
        with tempfile.TemporaryDirectory() as directory:
            broken = Path(directory) / 'broken.exe'; broken.touch()
            good = Path(directory) / 'working.exe'; good.touch()
            with mock.patch.object(setup_build.subprocess, 'check_output', side_effect=[
                    OSError('cannot run compiler'), 'clang version 19.1.1\nTarget: x86_64-pc-windows-msvc\n']):
                self.assertEqual(setup_build.find_windows_clang([broken, good]), good)

    @staticmethod
    def fake_vs(directory, *versions):
        vs = Path(directory)
        for version in versions:
            (vs / 'VC/Tools/MSVC' / version).mkdir(parents=True)
        return vs

    def test_newest_msvc_toolset_chosen_over_pinned_older_default(self):
        # A side-by-side 14.38 install pins Microsoft.VCToolsVersion.v143.default.txt,
        # which plain vcvars64.bat obeys; the matched engine needs the 14.44+ STL.
        with tempfile.TemporaryDirectory() as directory:
            vs = self.fake_vs(directory, '14.38.33130', '14.44.35207', '14.9.1')
            self.assertEqual(setup_build.select_msvc_toolset(vs), '14.44.35207')

    def test_only_old_msvc_toolsets_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            vs = self.fake_vs(directory, '14.38.33130', '14.40.33807')
            with self.assertRaisesRegex(ValueError, '14.44'):
                setup_build.select_msvc_toolset(vs)


class LocalBuildTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def iso(self, data):
        path = self.root / 'synthetic.iso'
        path.write_bytes(data)
        return path

    def test_reads_case_insensitive_without_modifying_iso(self):
        path = self.iso(synthetic_iso())
        before = path.read_bytes()
        with DiscImage(path) as disc:
            self.assertEqual(disc.file('SAMPLE.TXT', 3), b'abc')
        self.assertEqual(path.read_bytes(), before)

    def test_out_of_bounds_directory_rejected(self):
        with DiscImage(self.iso(synthetic_iso(directory_sector=1000))) as disc:
            with self.assertRaisesRegex(ValueError, 'outside'):
                disc.file('sample.txt', 3)

    def test_cyclic_directory_rejected(self):
        data = synthetic_iso()
        base = 40 * 2048
        struct.pack_into('<HH', data, base, 8, 0)
        struct.pack_into('<HHIIBB', data, base + 32, 8, 0, 42, 3, 0, 1)
        data[base + 46] = ord('b')
        with DiscImage(self.iso(data)) as disc:
            with self.assertRaisesRegex(ValueError, 'cyclic'):
                disc.file('a', 3)

    def test_wrong_game_creates_no_output(self):
        data = synthetic_iso()
        name = b'default.xex'
        data[40 * 2048 + 13] = len(name)
        data[40 * 2048 + 14:40 * 2048 + 14 + len(name)] = name
        with self.assertRaisesRegex(ValueError, 'Unsupported game executable'):
            extract(self.iso(data), self.root / 'output')
        self.assertFalse((self.root / 'output').exists())

    def test_native_edit_checks_input_and_is_idempotent(self):
        digest = lambda text: hashlib.sha256(text.encode()).hexdigest()
        rule = {'name': 'synthetic', 'input_sha256': digest('abc'), 'output_sha256': digest('axc'),
                'edits': [{'offset': 1, 'delete': 1, 'insert': 'x'}]}
        self.assertEqual(apply_edits('abc', rule), 'axc')
        self.assertEqual(apply_edits('axc', rule), 'axc')
        with self.assertRaisesRegex(ValueError, 'differs'):
            apply_edits('abd', rule)

    def test_unsupported_menu_unchanged(self):
        path = self.root / 'menu.rpf'
        path.write_bytes(b'unsupported input')
        with self.assertRaises(ValueError):
            patch(path)
        self.assertEqual(path.read_bytes(), b'unsupported input')

    def test_missing_network_callback_registration_rejected(self):
        table = '{ 0x822F9900, sub_822F9900 },\n'
        # CREATE MATCH and the separate-PC join crash must both fail the build
        # if code generation omits or misregisters their indirect callbacks.
        for address in (0x822F99C0, 0x823A83F8):
            with self.subTest(address=f'0x{address:08X}'):
                required = {0x822F9900, address}
                with self.assertRaisesRegex(ValueError, f'0x{address:08X}'):
                    verify_function_table(table, required)
                wrong = table + f'{{ 0x{address:08X}, sub_822F9900 }},\n'
                with self.assertRaisesRegex(ValueError, f'0x{address:08X}'):
                    verify_function_table(wrong, required)
                verify_function_table(table +
                                      f'{{ 0x{address:08X}, sub_{address:08X} }},\n',
                                      required)

    def test_public_allowlist_excludes_all_local_game_output(self):
        names = audit()
        self.assertFalse(any(n.startswith(('dist/', 'private_work/', 'project/generated/', 'sdk/', '.tools/')) for n in names))
        self.assertFalse(any(Path(n).suffix in ('.exe', '.dll', '.xex', '.rpf', '.bin', '.iso') for n in names))

    def test_installer_payload_rejects_added_game_files(self):
        path = self.root / 'payload.zip'
        with zipfile.ZipFile(path, 'w') as archive:
            archive.writestr('dist/tabletennis.exe', b'not a real executable')
        with self.assertRaisesRegex(ValueError, 'allowlist'):
            verify_zip(path)

    @unittest.skipUnless(sys.platform == 'win32', 'Windows bootstrap entry point')
    def test_iso_validation_does_not_discover_or_install_compilers(self):
        path = self.iso(synthetic_iso())
        with mock.patch.object(sys, 'argv', ['setup_build.py', '--iso', str(path), '--validate-iso']), \
             mock.patch.object(setup_build, 'validate') as validate, \
             mock.patch.object(setup_build, 'build_tools') as tools, \
             mock.patch.object(setup_build, 'find_sdk') as sdk:
            setup_build.main()
        validate.assert_called_once_with(path)
        tools.assert_not_called()
        sdk.assert_not_called()


@unittest.skipUnless(sys.platform == 'win32' and os.environ.get('TABLETENNIS_TEST_DIST')
                     and os.environ.get('TABLETENNIS_TEST_ISO'),
                     'Opt-in runtime check needs a private local build and retail ISO')
class RuntimeIsoTests(unittest.TestCase):
    def test_iso_boot_from_arguments_and_saved_settings(self):
        """Catch the SDK directory-only guard, including direct EXE launch.

        Copy only local runtime files into an isolated folder. Never change the
        installed settings, saves, SDK or ISO; no game files belong in Git.
        """
        dist = Path(os.environ['TABLETENNIS_TEST_DIST']).resolve(strict=True)
        iso = Path(os.environ['TABLETENNIS_TEST_ISO']).resolve(strict=True)
        community = (dist / 'community-engine.json').is_file()
        sources = ('argument', 'settings', 'network-argument') if community else ('argument', 'settings')
        for source in sources:
            with self.subTest(source=source), tempfile.TemporaryDirectory(prefix='Table Tennis ISO test ') as folder:
                target = Path(folder)
                for pattern in ('tabletennis.exe', '*.dll'):
                    for path in dist.glob(pattern):
                        shutil.copy2(path, target / path.name)
                shutil.copytree(dist / 'assets', target / 'assets')
                settings = f'iso_path={iso}\n' if source == 'settings' else ''
                if community:
                    settings += ('system_link=' + ('false' if source == 'network-argument' else 'true') +
                                 '\nonline_server=127.0.0.1:27360\nplayer_name=SettingsUser\nroom_code=ABC234\n')
                (target / 'settings.ini').write_text(settings + 'framerate=60\nallow_tearing=true\n', encoding='utf-8')
                args = [str(target / 'tabletennis.exe'), '--fullscreen=false']
                if source != 'settings':
                    args.append('--game_data_root=' + str(iso))
                    # Explicit selection must win over settings.ini's 60 cap.
                    args.extend(['--framerate=144', '--allow_tearing=false'])
                    if community:
                        args.extend(['--system_link=' + ('true' if source == 'network-argument' else 'false'),
                                     '--online_server=127.0.0.1:27361', '--user_name=CommandLineUser'])
                process = subprocess.Popen(args, cwd=target.parent,
                                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                try:
                    deadline = time.monotonic() + 20
                    log = ''
                    while time.monotonic() < deadline:
                        log = '\n'.join(p.read_text(encoding='utf-8', errors='replace')
                                        for p in (target / 'logs').glob('*.log'))
                        if 'Runtime and VFS fully initialized' in log or '[error]' in log or process.poll() is not None:
                            break
                        time.sleep(0.2)
                    self.assertIn('Mounted ISO via DiscImageDevice', log, log[-6000:])
                    self.assertIn('Runtime and VFS fully initialized', log, log[-6000:])
                    self.assertNotIn('--game_data_root does not exist', log)
                    self.assertIn('TabletennisOfflineMenu', log, log[-6000:])
                    if source != 'settings':
                        self.assertIn('framerate 144 requested; pacing at 60', log)
                        self.assertIn('loaded GPU timing flags: vsync=false, tearing=false', log)
                    else:
                        self.assertIn('loaded GPU timing flags: vsync=false, tearing=true', log)
                    if community:
                        enabled = 'false' if source == 'argument' else 'true'
                        endpoint = '127.0.0.1:27360' if source == 'settings' else '127.0.0.1:27361'
                        player = 'SettingsUser' if source == 'settings' else 'CommandLineUser'
                        self.assertIn(f'Network setup: enabled={enabled}, system_link={enabled}, '
                                      f'server={endpoint}, player={player}', log, log[-6000:])
                    self.assertIsNone(process.poll(), 'Game exited during ISO startup')
                finally:
                    if process.poll() is None:
                        process.terminate()
                    process.wait(timeout=10)


@unittest.skipUnless(sys.platform == 'win32' and os.environ.get('TABLETENNIS_TEST_NATIVE'),
                     'Opt-in native pacing test needs the compiler environment')
class NativePacingTests(unittest.TestCase):
    def test_frame_schedule(self):
        root = Path(__file__).resolve().parents[1]
        _, _, clang, _, env = setup_build.build_tools()
        with tempfile.TemporaryDirectory(prefix='Table Tennis pacing ') as directory:
            executable = Path(directory) / 'pacing.exe'
            subprocess.run([str(clang), '-std=c++23', '-UNDEBUG',
                            '-I' + str(root / 'project/src'), str(root / 'tests/frame_pacing_test.cpp'),
                            '-o', str(executable)], check=True, env=env)
            subprocess.run([str(executable)], check=True, timeout=10)


@unittest.skipUnless(sys.platform == 'win32' and os.environ.get('TABLETENNIS_TEST_NATIVE'),
                     'Opt-in native screen-layout test needs the compiler environment')
class NativeScreenTests(unittest.TestCase):
    def test_ui_proportions_and_scissor_bounds(self):
        root = Path(__file__).resolve().parents[1]
        _, _, clang, _, env = setup_build.build_tools()
        with tempfile.TemporaryDirectory(prefix='Table Tennis aspect ') as directory:
            executable = Path(directory) / 'layout.exe'
            subprocess.run([str(clang), '-std=c++23', '-UNDEBUG',
                            '-I' + str(root / 'project/src'), str(root / 'tests/pc_features_test.cpp'),
                            '-o', str(executable)], check=True, env=env)
            subprocess.run([str(executable)], check=True, timeout=10)


class MatchedEngineDownloadTests(unittest.TestCase):
    def test_default_engine_uses_pinned_release_and_flat_archive(self):
        import io, json
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'project').mkdir()
            payload = io.BytesIO()
            with zipfile.ZipFile(payload, 'w') as package:
                package.writestr('bin/rexglue.exe', b'fixture')
                package.writestr('lib/cmake/rexglue/', b'')
            data = payload.getvalue()
            checksum = hashlib.sha256(data).hexdigest()
            (root / 'project/engine.json').write_text(json.dumps(dict(url='https://example.invalid/pinned.zip', sha256=checksum, size=len(data))))
            with mock.patch.object(setup_build, 'ROOT', root), \
                 mock.patch.object(setup_build.urllib.request, 'urlopen', return_value=io.BytesIO(data)) as fetch, \
                 mock.patch.object(setup_build.subprocess, 'check_output', return_value='0.10.0'):
                sdk = setup_build.find_sdk(None, False)
                self.assertEqual(sdk, root / '.tools' / ('engine-' + checksum[:12]))
                self.assertEqual(fetch.call_args.args[0].full_url, 'https://example.invalid/pinned.zip')
                self.assertEqual(setup_build.find_sdk(None, True), sdk)
                self.assertEqual(fetch.call_count, 1)

    def test_corrupt_engine_download_never_runs_or_extracts(self):
        import io, json
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'project').mkdir()
            (root / 'project/engine.json').write_text(json.dumps(dict(url='https://example.invalid/pinned.zip', sha256='0'*64, size=4)))
            with mock.patch.object(setup_build, 'ROOT', root), \
                 mock.patch.object(setup_build.urllib.request, 'urlopen', return_value=io.BytesIO(b'wrong')), \
                 mock.patch.object(setup_build.subprocess, 'check_output') as execute:
                with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
                    setup_build.find_sdk(None, False)
                execute.assert_not_called()
                self.assertFalse(any(root.rglob('rexglue.exe')))


if __name__ == '__main__':
    unittest.main()
