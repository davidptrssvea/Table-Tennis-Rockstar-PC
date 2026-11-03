"""Opt-in tests of the patched Windows SDK against an isolated loopback server."""
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'project/tools'))
import setup_build


@unittest.skipUnless(sys.platform == 'win32' and os.environ.get('TABLETENNIS_TEST_COMMUNITY_SDK'),
                     'Opt-in matched Windows SDK required')
class CommunityNetworkTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix='Table Tennis network ')
        cls.folder = Path(cls.temp.name)
        sdk = Path(os.environ['TABLETENNIS_TEST_COMMUNITY_SDK']).resolve(strict=True)
        if not (sdk / '.tabletennis-community-sdk').is_file():
            raise ValueError('Matched community SDK marker missing')
        (cls.folder / 'CMakeLists.txt').write_text('''cmake_minimum_required(VERSION 3.25)
project(network_probe LANGUAGES CXX)
set(CMAKE_CXX_STANDARD 23)
find_package(rexglue 0.10.0 REQUIRED CONFIG)
add_executable(network_probe probe.cpp)
target_link_libraries(network_probe PRIVATE rex::runtime ws2_32)
''')
        shutil.copy2(ROOT / 'tests/community_network_probe.cpp', cls.folder / 'probe.cpp')
        cmake, ninja, clang, _, env = setup_build.build_tools()
        subprocess.run([str(cmake), '-S', str(cls.folder), '-B', str(cls.folder / 'build'),
                        '-G', 'Ninja', f'-DCMAKE_MAKE_PROGRAM={ninja}',
                        f'-DCMAKE_CXX_COMPILER={clang}', f'-DCMAKE_PREFIX_PATH={sdk}',
                        '-DCMAKE_BUILD_TYPE=Release'], env=env, check=True,
                       stdout=subprocess.DEVNULL)
        subprocess.run([str(cmake), '--build', str(cls.folder / 'build')], env=env,
                       check=True, stdout=subprocess.DEVNULL)
        cls.exe = cls.folder / 'build/network_probe.exe'
        for name in ('rexruntime.dll', 'TracyClient.dll'):
            shutil.copy2(sdk / 'bin' / name, cls.exe.parent / name)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def start_server(self, port, log):
        process = subprocess.Popen([sys.executable, str(ROOT / 'server/ttlink_server.py'),
                                    '--bind', '127.0.0.1', '--port', str(port)],
                                   stdout=log, stderr=subprocess.STDOUT)
        for _ in range(50):
            try:
                with urllib.request.urlopen(f'http://127.0.0.1:{port}/status', timeout=.2):
                    return process
            except OSError:
                time.sleep(.1)
        process.terminate()
        process.wait(timeout=5)
        self.fail('Loopback server failed to start')

    def exercise(self, mode):
        with socket.socket() as listener:
            listener.bind(('127.0.0.1', 0))
            port = listener.getsockname()[1]
        with tempfile.TemporaryDirectory(dir=self.folder) as directory:
            folder = Path(directory)
            children = []
            with (folder / 'server.log').open('w') as server_log, \
                 (folder / 'a.log').open('w') as a_log, (folder / 'b.log').open('w') as b_log:
                server = self.start_server(port, server_log)
                children.append(server)
                try:
                    for name, room, log in [('WindowsA', 'ABC234', a_log),
                                            ('WindowsB', 'DEF567' if mode == 'isolate' else 'ABC234', b_log)]:
                        children.append(subprocess.Popen([str(self.exe), f'127.0.0.1:{port}',
                                                          name, room, mode], stdout=log,
                                                         stderr=subprocess.STDOUT))
                    if mode == 'reconnect':
                        for _ in range(100):
                            if all('PAIRED' in (folder / n).read_text() for n in ('a.log', 'b.log')):
                                break
                            time.sleep(.1)
                        else:
                            self.fail('Clients did not initially pair')
                        server.terminate()
                        server.wait(timeout=5)
                        time.sleep(1)
                        server = self.start_server(port, server_log)
                        children.append(server)
                    for client, name in zip(children[1:3], ('a.log', 'b.log')):
                        result = client.wait(timeout=35)
                        self.assertEqual(result, 0, (folder / name).read_text())
                    if mode == 'reconnect':
                        log = (folder / 'server.log').read_text()
                        for name in ('WindowsA', 'WindowsB'):
                            self.assertGreaterEqual(log.count(name + ' joined'), 2, log)
                finally:
                    for child in children:
                        if child.poll() is None:
                            child.terminate()
                        child.wait(timeout=5)

    def test_direct_peer_names_and_ping(self):
        self.exercise('direct')

    def test_relay_ping(self):
        self.exercise('relay')

    def test_private_rooms_are_isolated(self):
        self.exercise('isolate')

    def test_clients_rejoin_after_server_restart(self):
        self.exercise('reconnect')
