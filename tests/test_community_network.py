"""Opt-in tests of the patched Windows SDK against an isolated loopback server."""
import os
from pathlib import Path
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'project/tools'))
import setup_build


def loopback_port():
    for _ in range(100):
        with socket.socket() as tcp, socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
            udp.bind(('127.0.0.1', 0))
            port = udp.getsockname()[1]
            try:
                tcp.bind(('127.0.0.1', port))
                return port
            except OSError:
                continue
    raise RuntimeError('No free shared TCP/UDP loopback port')


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
                if process.poll() is not None:
                    break
                time.sleep(.1)
        if process.poll() is None:
            process.terminate()
        process.wait(timeout=5)
        log.flush()
        self.fail('Loopback server failed to start: ' + Path(log.name).read_text(errors='replace'))

    def exercise(self, mode):
        # TCP and UDP have separate availability/exclusion rules on Windows.
        port = loopback_port()
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
                    if os.environ.get('TABLETENNIS_TEST_LOG_DIR'):
                        for log in (server_log, a_log, b_log):
                            log.flush()
                        target = Path(os.environ['TABLETENNIS_TEST_LOG_DIR']) / mode
                        target.mkdir(parents=True, exist_ok=True)
                        for name in ('server.log', 'a.log', 'b.log'):
                            shutil.copy2(folder / name, target / name)

    def test_direct_peer_names_and_ping(self):
        self.exercise('direct')

    def test_relay_ping(self):
        self.exercise('relay')

    def test_private_rooms_are_isolated(self):
        self.exercise('isolate')

    def test_clients_rejoin_after_server_restart(self):
        self.exercise('reconnect')

    def test_delayed_transport(self):
        self.exercise('delay')

    def test_room_switch_with_delayed_packets(self):
        self.exercise('switch')

    def test_invalid_server_ports_are_rejected(self):
        for address in ('127.0.0.1:0', '127.0.0.1:65536', '127.0.0.1:27360junk'):
            result = subprocess.run([str(self.exe), address, 'PortTest', 'ABC234', 'invalid'],
                                    capture_output=True, text=True, timeout=8)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_server_keeps_receiving_after_udp_peer_closes(self):
        port = loopback_port()
        def hello(name, nonce):
            name = name.encode('ascii')
            return (b'TTV1\x01' + struct.pack('>BQ', 1, nonce) + bytes([len(name)]) + name +
                    b'\x06ABC234' + struct.pack('>IHh', 0x7F000001, 11000, 60))
        with tempfile.TemporaryDirectory(dir=self.folder) as folder:
            with (Path(folder) / 'server.log').open('w') as log:
                process = self.start_server(port, log)
                try:
                    for i in range(3):
                        # Replies to this closed port generate Windows ICMP errors.
                        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as dead:
                            dead.bind(('127.0.0.1', 0))
                            dead.sendto(hello('Gone' + str(i), i + 1), ('127.0.0.1', port))
                        time.sleep(.15)
                        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as live:
                            live.bind(('127.0.0.1', 0))
                            live.settimeout(2)
                            live.sendto(hello('Live' + str(i), i + 10), ('127.0.0.1', port))
                            packet, _ = live.recvfrom(2048)
                            self.assertEqual(packet[:5], b'TTV1\x02')
                            cid, _, token = struct.unpack_from('>IIQ', packet, 5)
                            live.sendto(b'TTV1\x08' + struct.pack('>IQ', cid, token),
                                        ('127.0.0.1', port))
                        time.sleep(.1)
                finally:
                    process.terminate()
                    process.wait(timeout=5)
