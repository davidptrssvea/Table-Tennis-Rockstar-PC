"""Updater extraction, crash recovery and data preservation without retail data."""
import importlib.util
import json
from pathlib import Path
import stat
import tempfile
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('update_backend', ROOT / 'project/updater/update_backend.py')
backend = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backend)


class PowerLoss(BaseException):
    pass


class UpdateBackendTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='Table Tennis updater ')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        (self.root / 'dist').mkdir()
        (self.root / 'dist/settings.ini').write_bytes(b'iso_path=untouched.iso\r\nfuture_key=keep\r\n')
        (self.root / 'dist/tabletennis.exe').write_bytes(b'old game')
        (self.root / 'dist/rexruntime.dll').write_bytes(b'old engine')
        (self.root / 'dist/retail.iso').write_bytes(b'user disc')
        (self.root / 'user_data').mkdir()
        (self.root / 'user_data/save').write_bytes(b'progress')

    def staged(self):
        dist = self.root / '.updates/work/test/dist'
        dist.mkdir(parents=True)
        for name, data in {'tabletennis.exe': b'new game', 'rexruntime.dll': b'new engine',
                           'new-support.txt': b'new support', 'settings.ini': b'wrong defaults'}.items():
            (dist / name).write_bytes(data)
        return dist

    def old_intact(self):
        self.assertEqual((self.root / 'dist/tabletennis.exe').read_bytes(), b'old game')
        self.assertEqual((self.root / 'dist/rexruntime.dll').read_bytes(), b'old engine')
        self.assertEqual((self.root / 'dist/settings.ini').read_bytes(), b'iso_path=untouched.iso\r\nfuture_key=keep\r\n')
        self.assertEqual((self.root / 'dist/retail.iso').read_bytes(), b'user disc')
        self.assertEqual((self.root / 'user_data/save').read_bytes(), b'progress')

    def test_windows_unsafe_paths_rejected(self):
        for name in ('../outside', '/absolute', 'C:/outside', 'dist/x:stream', 'con.txt',
                     'AUX/data', 'dist/thing.', 'dist/thing ', 'a\\b', 'a//b', 'a/./b', 'a\0b'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                backend.relative_path(name)
        self.assertEqual(backend.relative_path('project/src/main.cpp'), Path('project/src/main.cpp'))

    def test_archive_rejects_traversal_case_collisions_and_symlinks(self):
        for names, linked in ((['../escape'], False), (['file', 'FILE'], False), (['link'], True)):
            with self.subTest(names=names):
                archive = self.root / 'bad.zip'
                with zipfile.ZipFile(archive, 'w') as package:
                    for name in names:
                        entry = zipfile.ZipInfo(name)
                        if linked:
                            entry.external_attr = (stat.S_IFLNK | 0o777) << 16
                        package.writestr(entry, b'bad')
                target = self.root / ('extract' + str(len(list(self.root.glob('extract*')))))
                with self.assertRaises(ValueError):
                    backend.extract_checked(archive, target, 1024)
        self.assertFalse((self.root.parent / 'escape').exists())

    def test_archive_expansion_limit_and_valid_nested_files(self):
        archive = self.root / 'archive.zip'
        with zipfile.ZipFile(archive, 'w') as package:
            package.writestr('dir/data', b'hello')
        with self.assertRaises(ValueError):
            backend.extract_checked(archive, self.root / 'small', 4)
        backend.extract_checked(archive, self.root / 'valid', 5)
        self.assertEqual((self.root / 'valid/dir/data').read_bytes(), b'hello')

    def test_activation_keeps_iso_settings_saves_and_allows_rollback(self):
        backend.activate(self.root, self.staged())
        self.assertEqual((self.root / 'dist/tabletennis.exe').read_bytes(), b'new game')
        self.assertEqual((self.root / 'dist/settings.ini').read_bytes(), b'iso_path=untouched.iso\r\nfuture_key=keep\r\n')
        backend.restore_previous(self.root)
        self.old_intact()
        self.assertFalse((self.root / 'dist/new-support.txt').exists())

    def test_exception_during_file_swap_restores_entire_pair(self):
        def fail(phase, name):
            if phase == 'installed' and name.endswith('rexruntime.dll'):
                raise OSError('simulated disk failure')
        with self.assertRaises(OSError):
            backend.activate(self.root, self.staged(), fail)
        self.old_intact()
        self.assertFalse(backend.journal_paths(self.root)[0].exists())

    def test_power_loss_at_each_mutation_recovers_idempotently(self):
        for target_phase in ('backed_up', 'installed'):
            with self.subTest(phase=target_phase):
                staged = self.staged()
                def fail(phase, name):
                    if phase == target_phase and name.endswith('rexruntime.dll'):
                        raise PowerLoss()
                with self.assertRaises(PowerLoss):
                    backend.activate(self.root, staged, fail)
                self.assertTrue(backend.journal_paths(self.root)[0].exists())
                backend.recover(self.root)
                backend.recover(self.root)
                self.old_intact()
                # A fresh owned fixture for the second case, not recursive deletion.
                for file in staged.iterdir():
                    file.unlink()
                staged.rmdir()

    def test_interrupted_build_marker_does_not_touch_installation(self):
        backend.atomic_json(backend.journal_paths(self.root)[1], {'state': 'building'})
        backend.recover(self.root)
        self.old_intact()
        self.assertFalse(backend.journal_paths(self.root)[1].exists())

    def test_committed_transaction_recovery_keeps_new_pair(self):
        original = backend.atomic_json
        def fail_previous(path, value):
            if Path(path).name == 'previous.json':
                raise PowerLoss()
            original(path, value)
        with mock.patch.object(backend, 'atomic_json', side_effect=fail_previous):
            with self.assertRaises(PowerLoss):
                backend.activate(self.root, self.staged())
        backend.recover(self.root)
        self.assertEqual((self.root / 'dist/tabletennis.exe').read_bytes(), b'new game')
        self.assertEqual((self.root / 'dist/rexruntime.dll').read_bytes(), b'new engine')
        backend.restore_previous(self.root)
        self.old_intact()

    def test_unchanged_files_are_not_moved(self):
        staged = self.staged()
        (staged / 'rexruntime.dll').write_bytes(b'old engine')
        backend.activate(self.root, staged)
        journal = backend.read_json(self.root / '.updates/previous.json')
        self.assertNotIn('dist/rexruntime.dll', [op['path'] for op in journal['operations']])

    def test_journal_cannot_escape_or_target_another_installation(self):
        for foreign in (True, False):
            journal = dict(schema=1, root='elsewhere' if foreign else str(self.root), state='applying',
                           operations=[dict(path='dist/../../outside', backup='.updates/backups/x',
                                            had_original=True, new_sha256='0'*64)])
            with self.assertRaises(ValueError):
                backend.rollback_journal(self.root, journal)
            self.old_intact()

    def test_stage_outside_owned_workspace_is_rejected(self):
        staged = self.root / 'unowned'
        staged.mkdir()
        (staged / 'tabletennis.exe').write_bytes(b'wrong')
        with self.assertRaises(ValueError):
            backend.activate(self.root, staged)
        self.old_intact()

    def test_modified_new_file_is_not_deleted_by_recovery(self):
        backend.activate(self.root, self.staged())
        (self.root / 'dist/new-support.txt').write_bytes(b'user changed this')
        with self.assertRaisesRegex(ValueError, 'changed outside'):
            backend.restore_previous(self.root)
        self.assertEqual((self.root / 'dist/new-support.txt').read_bytes(), b'user changed this')

    def test_lock_refuses_a_second_writer(self):
        with backend.exclusive(self.root):
            with self.assertRaises(OSError):
                with backend.exclusive(self.root):
                    self.fail('Concurrent writer acquired the same lock')

    def test_duplicate_json_and_newer_protocol_rejected(self):
        path = self.root / 'duplicate.json'
        path.write_text('{"version":"1.2.0","version":"9.0.0"}')
        with self.assertRaises(ValueError):
            backend.read_json(path)
        with self.assertRaises(ValueError):
            backend.validate_manifest({'schema': 2, 'repository': backend.REPOSITORY})

    def test_backend_rejects_downgrade_without_building(self):
        backend.atomic_json(self.root / 'dist/install.json', dict(version='1.2.0'))
        for version in ('1.2.0', '1.1.9'):
            with mock.patch.object(backend, 'verify_package', return_value=dict(version=version)), \
                 mock.patch.object(backend, 'build_release') as build:
                with self.assertRaisesRegex(ValueError, 'newer signed'):
                    backend.apply_update(self.root, None, None, None, None, None, builder=build)
                build.assert_not_called()
        self.old_intact()

    def test_power_loss_during_rollback_is_recoverable(self):
        backend.activate(self.root, self.staged())
        replace = backend.os.replace
        def interrupted(source, target):
            replace(source, target)
            if '.updates/backups/' in Path(source).as_posix():
                raise PowerLoss()
        with mock.patch.object(backend.os, 'replace', side_effect=interrupted):
            with self.assertRaises(PowerLoss):
                backend.restore_previous(self.root)
        backend.recover(self.root)
        backend.recover(self.root)
        self.old_intact()
        self.assertFalse((self.root / '.updates/in-progress').exists())
