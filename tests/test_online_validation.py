"""Protocol boundaries and launcher/server agreement; no retail game data needed."""
import importlib.util
import os
import socket
import shutil
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
        # VIPs are room-local, so unrelated rooms may legitimately reuse one.
        # Choose distinct fixture VIPs when asserting an out-of-room destination.
        with mock.patch.object(relay.random, 'choice', side_effect=[1, 2, 3]):
            for name, room, addr in zip(('Host', 'Guest', 'Other'),
                                        ('ABC234', 'ABC234', 'DEF567'), addresses):
                server.datagram_received(hello(name, room), addr)
                client = server.by_addr[addr]
                server.datagram_received(relay.MAGIC + bytes([relay.KEEPALIVE]) + struct.pack('>IQ', client.id, client.token), addr)
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
 static void Controls(System.Windows.DependencyObject node,System.Collections.Generic.List<System.Windows.DependencyObject> result) {
  result.Add(node);
  foreach(object child in System.Windows.LogicalTreeHelper.GetChildren(node)) if(child is System.Windows.DependencyObject) Controls((System.Windows.DependencyObject)child,result);
 }
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
   bool selected=(bool)t.GetMethod("SelectedRelayResponds",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{"localhost:"+args[1]});
   if(selected!=actual) throw new Exception("Hostname/UDP readiness mismatch");
   return;
  }
  var create=t.GetMethod("MakeInvitation",BindingFlags.NonPublic|BindingFlags.Static);
  var read=t.GetMethod("ReadInvitation",BindingFlags.NonPublic|BindingFlags.Static);
  var random=t.GetMethod("NewPrivateRoom",BindingFlags.NonPublic|BindingFlags.Static);
  for(int i=0;i<2048;i++) {
   string roomCode=(string)random.Invoke(null,null);
   if(roomCode.Length!=6) throw new Exception("Private room needs six characters");
   Check("ValidRoom",roomCode,true);
  }
  string invitation=(string)create.Invoke(null,new object[]{"relay.example:34567","ABC234","0123456789AB"});
  string[] decoded=(string[])read.Invoke(null,new object[]{invitation});
  if(decoded.Length!=3 || decoded[0]!="relay.example:34567" || decoded[1]!="ABC234" || decoded[2]!="0123456789AB")
   throw new Exception("Invitation lost server/room/build");
  foreach(string bad in new[]{"",invitation+"\\n","TT2.AAAA","TT1.A","TT1."+new string('A',513)}) {
   bool rejected=false;try {read.Invoke(null,new object[]{bad});} catch(TargetInvocationException) {rejected=true;}
   if(!rejected) throw new Exception("Malformed invitation accepted");
  }
  foreach(string payload in new[]{"host:0|ABC234|0123456789AB","host|1234|0123456789AB","host|ABC234|wrong","host|ABC234|0123456789AB|extra"}) {
   string bad="TT1."+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+','-').Replace('/','_');
   bool rejected=false;try {read.Invoke(null,new object[]{bad});} catch(TargetInvocationException) {rejected=true;}
   if(!rejected) throw new Exception("Invalid invitation fields accepted");
  }
  string settings=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]),"settings.ini");
  System.IO.File.WriteAllText(settings,"achievement_sound=false\\nfuture_key=preserve\\niso_path=C:\\\\Test.iso\\n");
  System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]),"community-engine.json"),"{}");
  var window=Activator.CreateInstance(t);
  var controls=new System.Collections.Generic.List<System.Windows.DependencyObject>();Controls((System.Windows.DependencyObject)window,controls);
  System.Windows.Controls.ComboBox hostMode=null;System.Windows.Controls.CheckBox consent=null;
  foreach(var control in controls) {
   var combo=control as System.Windows.Controls.ComboBox;
   if(combo!=null) foreach(object item in combo.Items) if(item.ToString().Contains("UPnP (EXPERIMENTAL")) hostMode=combo;
   var checkbox=control as System.Windows.Controls.CheckBox;
   if(checkbox!=null && checkbox.Content is System.Windows.Controls.TextBlock && ((System.Windows.Controls.TextBlock)checkbox.Content).Text.StartsWith("EXPERIMENTAL: allow")) consent=checkbox;
  }
  if(hostMode==null || consent==null || hostMode.SelectedIndex!=0 || consent.IsChecked==true) throw new Exception("UPnP must be explicitly experimental and off by default");
  consent.IsChecked=true;hostMode.SelectedIndex=1;
  if(consent.IsChecked==true) throw new Exception("Changing modes retained router permission");
  hostMode.SelectedIndex=0;
  string folder=System.IO.Path.GetDirectoryName(args[0]);
  foreach(string name in new[]{"tabletennis.exe","rexruntime.dll","rexgpu-xenos.dll"})
   System.IO.File.WriteAllText(System.IO.Path.Combine(folder,name),"owned test fixture "+name);
  string build=(string)t.GetMethod("OnlineBuildId",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(window,null);
  var apply=t.GetMethod("ApplyInvitation",BindingFlags.NonPublic|BindingFlags.Instance);
  var serverBox=(System.Windows.Controls.TextBox)t.GetField("txtServer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(window);
  var playerBox=(System.Windows.Controls.TextBox)t.GetField("txtPlayer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(window);
  string player=playerBox.Text,oldServer=serverBox.Text;
  bool mismatch=false;try {apply.Invoke(window,new object[]{invitation});} catch(TargetInvocationException) {mismatch=true;}
  if(!mismatch || serverBox.Text!=oldServer) throw new Exception("Mismatched invite changed settings");
  string matching=(string)create.Invoke(null,new object[]{"relay.example:34567","ABC234",build});
  apply.Invoke(window,new object[]{matching});
  if(serverBox.Text!="relay.example:34567" || playerBox.Text!=player) throw new Exception("Invitation changed player name or lost server");
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
                                      str(ROOT / 'project/launcher/Launcher.cs'), str(ROOT / 'project/launcher/HostSession.cs'), str(ROOT / 'project/launcher/GatewayLeaseRouter.cs')], check=True)
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


@unittest.skipUnless(sys.platform == 'win32' and os.environ.get('TABLETENNIS_TEST_COMMUNITY_SDK'),
                     'Opt-in matched Windows SDK required')
class ButtonPromptTests(unittest.TestCase):
    def test_auto_detection_with_virtual_playstation_and_xbox(self):
        sys.path.insert(0, str(ROOT / 'project/tools'))
        import setup_build
        sdk = Path(os.environ['TABLETENNIS_TEST_COMMUNITY_SDK']).resolve(strict=True)
        self.assertTrue((sdk / '.tabletennis-community-sdk').is_file())
        with tempfile.TemporaryDirectory(prefix='Table Tennis prompt check ') as directory:
            folder = Path(directory)
            source = (ROOT / 'tests/button_prompts_probe.cpp').as_posix()
            (folder / 'CMakeLists.txt').write_text(
                'cmake_minimum_required(VERSION 3.25)\nproject(prompt_probe LANGUAGES CXX)\n'
                'set(CMAKE_CXX_STANDARD 23)\nfind_package(rexglue 0.10.0 REQUIRED CONFIG)\n'
                f'add_executable(prompt_probe "{source}")\n'
                'target_link_libraries(prompt_probe PRIVATE rex::runtime)\n')
            cmake, ninja, clang, _, env = setup_build.build_tools()
            subprocess.run([str(cmake), '-S', str(folder), '-B', str(folder / 'build'),
                            '-G', 'Ninja', f'-DCMAKE_MAKE_PROGRAM={ninja}',
                            f'-DCMAKE_CXX_COMPILER={clang}', f'-DCMAKE_PREFIX_PATH={sdk}',
                            '-DCMAKE_BUILD_TYPE=Release'], env=env, check=True,
                           stdout=subprocess.DEVNULL)
            subprocess.run([str(cmake), '--build', str(folder / 'build')], env=env,
                           check=True, stdout=subprocess.DEVNULL)
            for name in ('rexruntime.dll', 'TracyClient.dll'):
                shutil.copy2(sdk / 'bin' / name, folder / 'build' / name)
            subprocess.run([str(folder / 'build/prompt_probe.exe')], check=True,
                           timeout=10, stdout=subprocess.DEVNULL)
