"""Protocol boundaries and launcher/server agreement; no retail game data needed."""
import importlib.util
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('ttlink', ROOT / 'server/ttlink_server.py')
relay = importlib.util.module_from_spec(spec)
spec.loader.exec_module(relay)


class Transport:
    def __init__(self):
        self.sent = []

    def sendto(self, data, addr):
        self.sent.append((data, addr))


def hello(name='Host', room='ABC234', nonce=1):
    name, room = name.encode('utf-8'), room.encode('ascii')
    return (relay.MAGIC + bytes([relay.HELLO]) + struct.pack('>BQ', 1, nonce) +
            bytes([len(name)]) + name + bytes([len(room)]) + room +
            struct.pack('>IHh', 0x7F000001, 11000, 60))


class RelayValidationTests(unittest.TestCase):
    def server(self):
        server = relay.Server()
        server.connection_made(Transport())
        return server

    def test_truncated_packets_do_not_create_clients(self):
        packet = hello()
        # UTC offset is optional; everything before it is required.
        for length in range(len(packet) - 2):
            with self.subTest(length=length):
                server = self.server()
                server.datagram_received(packet[:length], ('127.0.0.1', 20000))
                self.assertFalse(server.clients)
        for kind in (relay.DATA, relay.KEEPALIVE, relay.BYE):
            for length in range(12):
                server = self.server()
                server.datagram_received(relay.MAGIC + bytes([kind]) + bytes(length),
                                         ('127.0.0.1', 20000))
                self.assertFalse(server.clients)

    def test_room_alphabet_and_duplicate_names(self):
        server = self.server()
        server.datagram_received(hello(room='ABL234'), ('127.0.0.1', 20000))
        self.assertFalse(server.clients)
        self.assertEqual(server.transport.sent[-1][0][5], relay.ERR_ROOM)
        server.datagram_received(hello(), ('127.0.0.1', 20000))
        server.datagram_received(hello(name='HOST', nonce=2), ('127.0.0.1', 20001))
        self.assertEqual(len(server.clients), 1)
        self.assertEqual(server.transport.sent[-1][0][5], relay.ERR_NAME)

    def test_authentication_and_cross_room_relay(self):
        server = self.server()
        addresses = [('127.0.0.1', p) for p in (20000, 20001, 20002)]
        for name, room, addr in zip(('Host', 'Guest', 'Other'),
                                    ('ABC234', 'ABC234', 'DEF567'), addresses):
            server.datagram_received(hello(name, room), addr)
        host, guest, other = [server.by_addr[addr] for addr in addresses]
        def send(target, token=host.token, source=addresses[0]):
            packet = relay.MAGIC + bytes([relay.DATA]) + struct.pack(
                '>IQIHH', host.id, token, target.vip, 1000, 1000) + b'payload'
            server.datagram_received(packet, source)
        server.transport.sent.clear()
        send(guest)
        self.assertEqual(server.transport.sent[-1][1], addresses[1])
        self.assertEqual(server.transport.sent[-1][0][4], relay.RELAYED)
        server.transport.sent.clear()
        send(other)
        self.assertFalse(server.transport.sent)
        send(guest, token=host.token ^ 1)
        self.assertFalse(any(data[4] == relay.RELAYED for data, _ in server.transport.sent))
        server.transport.sent.clear()
        send(guest, source=addresses[2])
        self.assertFalse(any(data[4] == relay.RELAYED for data, _ in server.transport.sent))

    def test_unicode_names_fit_wire_without_broken_utf8(self):
        server = self.server()
        server.datagram_received(hello(name='😀' * 8), ('127.0.0.1', 20000))
        name = next(iter(server.clients.values())).name
        self.assertLessEqual(len(name.encode('utf-8')), 15)
        self.assertEqual(name, '😀' * 3)

    def test_hello_rate_tracking_is_bounded(self):
        server = self.server()
        with mock.patch.object(relay, 'MAX_HELLO_IPS', 2):
            for suffix in range(1, 4):
                server.datagram_received(hello(name='Host' + str(suffix)),
                                         ('127.0.0.' + str(suffix), 20000))
        self.assertEqual(len(server.hello_limits), 2)
        self.assertEqual(len(server.clients), 2)


@unittest.skipUnless(os.name == 'nt', 'Windows .NET Framework launcher')
class LauncherValidationTests(unittest.TestCase):
    def test_room_and_port_boundaries_match_protocol(self):
        csc = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        with tempfile.TemporaryDirectory(prefix='Table Tennis launcher check ') as folder:
            folder = Path(folder)
            launcher = folder / 'launcher.exe'
            probe = folder / 'probe.cs'
            probe.write_text('''using System;
using System.Reflection;
class Probe {
 static Type t;
 static void Check(string method,string value,bool expected) {
  bool actual=(bool)t.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{value});
  if(actual!=expected) throw new Exception(method+" rejected/accepted incorrectly: "+value);
 }
 static void Main(string[] args) {
  t=Assembly.LoadFrom(args[0]).GetType("TableTennisLauncher.MainWindow");
  foreach(string s in new[]{"localhost","127.0.0.1:1","host.example:27360","127.0.0.1:65535"}) Check("ValidServer",s,true);
  foreach(string s in new[]{"","host:0","host:65536","host:99999","host:","host:-1","host:1x","host:80\\n"}) Check("ValidServer",s,false);
  foreach(string s in new[]{"","ABC234","GHJ789"}) Check("ValidRoom",s,true);
  foreach(string s in new[]{"1234","AB1234","ABL234","ABI234","ABO234","ABC234\\n"}) Check("ValidRoom",s,false);
 }
}''', encoding='utf-8')
            common = [str(csc), '/nologo', '/optimize', '/lib:' + str(csc.parent / 'WPF'),
                      '/r:PresentationCore.dll,PresentationFramework.dll,WindowsBase.dll,System.dll,System.Xaml.dll']
            subprocess.run(common + ['/target:winexe', '/out:' + str(launcher),
                                      str(ROOT / 'project/launcher/Launcher.cs')], check=True)
            executable = folder / 'probe.exe'
            subprocess.run(common + ['/target:exe', '/out:' + str(executable), str(probe)], check=True)
            subprocess.run([str(executable), str(launcher)], check=True, timeout=10)
