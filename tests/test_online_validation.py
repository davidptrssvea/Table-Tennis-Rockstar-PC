"""Protocol boundaries and launcher/server agreement; no retail game data needed."""
import importlib.util
import os
import socket
import sys
import time
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
using System.Net;
using System.Net.NetworkInformation;
class Probe {
 static Type t;
 static void Check(string method,string value,bool expected) {
  bool actual=(bool)t.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{value});
  if(actual!=expected) throw new Exception(method+" rejected/accepted incorrectly: "+value);
 }
 [STAThread]
 static void Main(string[] args) {
  t=Assembly.LoadFrom(args[0]).GetType("TableTennisLauncher.MainWindow");
  if(args.Length>1) {
   bool actual=(bool)t.GetMethod("RelayResponds",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{new IPEndPoint(IPAddress.Loopback,int.Parse(args[1]))});
   if(actual!=bool.Parse(args[2])) throw new Exception("Relay readiness mismatch");
   return;
  }
  string settings=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]),"settings.ini");
  System.IO.File.WriteAllText(settings,"achievement_sound=false\\nfuture_key=preserve\\niso_path=C:\\\\Test.iso\\n");
  var window=Activator.CreateInstance(t);
  t.GetMethod("SaveSettings",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(window,null);
  string saved=System.IO.File.ReadAllText(settings);
  if(!saved.Contains("achievement_sound=false") || !saved.Contains("future_key=preserve"))
   throw new Exception("Launcher lost in-game/unknown settings");
  ((System.Windows.Window)window).Close();
  var local=t.GetMethod("LocalServerEndpoint",BindingFlags.NonPublic|BindingFlags.Static);
  foreach(string value in new[]{"localhost:34567","127.0.0.1:34567",Environment.MachineName+":34567"}) {
   var endpoint=(IPEndPoint)local.Invoke(null,new object[]{value});
   if(endpoint==null || endpoint.Port!=34567) throw new Exception("Local server not recognized: "+value);
  }
  if(((IPEndPoint)local.Invoke(null,new object[]{"localhost"})).Port!=27360) throw new Exception("Wrong default port");
  foreach(string value in new[]{"203.0.113.77:27360","remote.example:27360","localhost:0"})
   if(local.Invoke(null,new object[]{value})!=null) throw new Exception("Remote/invalid server treated as local");
  foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces()) {
   if(adapter.OperationalStatus!=OperationalStatus.Up) continue;
   foreach(var entry in adapter.GetIPProperties().UnicastAddresses)
    if(entry.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork &&
       local.Invoke(null,new object[]{entry.Address+":34567"})==null) throw new Exception("Own adapter not recognized");
  }
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
            # A closed UDP port and an unrelated listener must not count as a relay.
            with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
                udp.bind(('127.0.0.1', 0))
                port = udp.getsockname()[1]
                subprocess.run([str(executable), str(launcher), str(port), 'false'],
                               check=True, timeout=5)
            subprocess.run([str(executable), str(launcher), str(port), 'false'],
                           check=True, timeout=5)
            # Verify the production Windows server's actual game protocol responds.
            with socket.socket() as tcp, socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
                udp.bind(('127.0.0.1', 0))
                port = udp.getsockname()[1]
                tcp.bind(('127.0.0.1', port))
            with (folder / 'server.log').open('w') as log:
                process = subprocess.Popen([sys.executable, str(ROOT / 'server/ttlink_server.py'),
                                            '--bind', '127.0.0.1', '--port', str(port)],
                                           stdout=log, stderr=subprocess.STDOUT)
                try:
                    for _ in range(50):
                        try:
                            with socket.create_connection(('127.0.0.1', port), timeout=.1):
                                break
                        except OSError:
                            if process.poll() is not None:
                                self.fail('Relay exited before readiness')
                            time.sleep(.1)
                    else:
                        self.fail('Relay did not start')
                    subprocess.run([str(executable), str(launcher), str(port), 'true'],
                                   check=True, timeout=5)
                    # Readiness probes never join a room or create player records.
                    import json
                    import urllib.request
                    with urllib.request.urlopen(f'http://127.0.0.1:{port}/status', timeout=2) as reply:
                        self.assertEqual(json.load(reply)['players_online'], 0)
                finally:
                    process.terminate()
                    process.wait(timeout=5)
