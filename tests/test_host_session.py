"""Player-host lifecycle and fake-router ownership tests; never maps a real router."""
import hashlib
import os
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import time
import unittest
import uuid
import zipfile

ROOT = Path(__file__).resolve().parents[1]


@unittest.skipUnless(os.name == 'nt', 'Windows launcher')
class HostSessionTests(unittest.TestCase):
    def test_router_ownership_public_address_and_recovery_rules(self):
        with tempfile.TemporaryDirectory(prefix='Table Tennis router tests ') as directory:
            folder = Path(directory)
            probe = folder / 'probe.cs'
            probe.write_text('''using System;using TableTennisLauncher;
class Fake:IHostRouter {
 public RouterMapping Map;public int Adds,Removes;public string External="8.8.8.8";
 public RouterMapping Find(int port){return Map;}
 public RouterMapping Add(int port,string client,string description){Adds++;return Map=new RouterMapping{Port=port,InternalPort=port,Client=client,Description=description,External=External,LeaseSeconds=300};}
 public void Remove(int port){Removes++;Map=null;} public void Dispose(){}
}
class Probe {
 static void Assert(bool ok){if(!ok)throw new Exception("Host/router invariant failed");}
 static void Main(){
  foreach(string ip in new[]{"127.0.0.1","10.1.1.1","192.168.1.5","172.16.0.1","100.64.0.1","169.254.1.2","0.0.0.0","224.0.0.1","255.255.255.255","::1","203.0.113.5"}) Assert(!HostSession.PublicIPv4(ip));
  Assert(HostSession.PublicIPv4("8.8.8.8"));
  var fake=new Fake();
  Assert(HostSession.OpenRouter(fake,27360,"192.168.1.5","mine")=="8.8.8.8:27360");Assert(fake.Adds==1);
  HostSession.OpenRouter(fake,27360,"192.168.1.5","mine");Assert(fake.Adds==1);
  HostSession.RemoveRouter(fake,27360,"192.168.1.6","mine");Assert(fake.Removes==0);
  HostSession.RemoveRouter(fake,27360,"192.168.1.5","other");Assert(fake.Removes==0);
  HostSession.RemoveRouter(fake,27360,"192.168.1.5","mine");Assert(fake.Removes==1);
  fake.Map=new RouterMapping{Port=27360,InternalPort=27360,Client="192.168.1.5",Description="other",External="8.8.8.8"};
  bool blocked=false;try{HostSession.OpenRouter(fake,27360,"192.168.1.5","mine");}catch{blocked=true;}
  Assert(blocked && fake.Adds==1);HostSession.RemoveRouter(fake,27360,"192.168.1.5","mine");Assert(fake.Removes==1);
  fake.Map=null;fake.External="100.64.1.2";blocked=false;
  try{HostSession.OpenRouter(fake,27360,"192.168.1.5","mine");}catch{blocked=true;}
  Assert(blocked);HostSession.RemoveRouter(fake,27360,"192.168.1.5","mine");Assert(fake.Removes==2);
  string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"TT-host-address-"+Guid.NewGuid().ToString("N"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root,"dist"));
  System.IO.File.WriteAllText(System.IO.Path.Combine(root,"dist","settings.ini"),"fixture");
  try {
   string token=Guid.NewGuid().ToString("N"),folder=HostSession.Folder(root),session=HostSession.SessionFolder(root,token);
   System.IO.Directory.CreateDirectory(session);
   System.IO.File.WriteAllText(System.IO.Path.Combine(folder,"active.txt"),token);
   System.IO.File.WriteAllText(System.IO.Path.Combine(session,"status.txt"),"state=ready\\naddress=8.8.8.8:27360\\nlocal=192.168.1.5:27360\\n");
   Assert(HostSession.InvitationAddress(root,"192.168.1.5:27360")=="8.8.8.8:27360");
   Assert(HostSession.InvitationAddress(root,"different.example:27360")=="different.example:27360");
   HostSession.Stop(root);Assert(System.IO.File.Exists(System.IO.Path.Combine(session,"stop")));
  } finally {System.IO.Directory.Delete(root,true);}
 }
}''')
            csc = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            exe = folder / 'probe.exe'
            subprocess.run([str(csc), '/nologo', '/out:' + str(exe), str(probe),
                            str(ROOT / 'project/launcher/HostSession.cs'), str(ROOT / 'project/launcher/GatewayLeaseRouter.cs')], check=True)
            subprocess.run([str(exe)], check=True, timeout=10)

    def test_real_loopback_host_duplicate_stop_and_game_exit(self):
        archive = ROOT / '.tools/python-3.14.8-embed-amd64.zip'
        if not archive.is_file():
            self.skipTest('Needs the cached official embedded Python archive')
        self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(),
                         'a93abe456ab01bd96d7a085b3cdb6566b3063f4241360d114142fbdb07f0a310')
        with socket.socket() as tcp, socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
            try:
                tcp.bind(('127.0.0.1', 27360))
                udp.bind(('127.0.0.1', 27360))
            except OSError:
                self.skipTest('Default relay port already in use; leaving it untouched')
        with tempfile.TemporaryDirectory(prefix='Table Tennis private host ') as directory:
            root = Path(directory)
            dist = root / 'dist'
            (dist / 'python').mkdir(parents=True)
            (dist / 'server').mkdir()
            with zipfile.ZipFile(archive) as package:
                package.extractall(dist / 'python')
            (dist / 'settings.ini').write_text('iso_path=unused.iso\n')
            shutil.copy2(ROOT / 'server/ttlink_server.py', dist / 'server/ttlink_server.py')
            csc = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
            exe = dist / 'tabletennis_launcher.exe'
            subprocess.run([str(csc), '/nologo', '/target:winexe', '/out:' + str(exe),
                            '/lib:' + str(csc.parent / 'WPF'),
                            '/r:PresentationCore.dll,PresentationFramework.dll,WindowsBase.dll,System.dll,System.Xaml.dll',
                            str(ROOT / 'project/launcher/Launcher.cs'), str(ROOT / 'project/launcher/HostSession.cs'), str(ROOT / 'project/launcher/GatewayLeaseRouter.cs')], check=True)
            game_source = root / 'game.cs'
            game_source.write_text('class Game {static void Main(){System.Threading.Thread.Sleep(1800);}}')
            subprocess.run([str(csc), '/nologo', '/out:' + str(dist / 'tabletennis.exe'), str(game_source)], check=True)
            for finish in ('stop', 'game', 'crash'):
                token = uuid.uuid4().hex
                session = root / '.updates/hosting' / token
                process = subprocess.Popen([str(exe), '--host-session', str(root), '127.0.0.1', 'lan', '', token, 'ABC234'])
                try:
                    deadline = time.monotonic() + 10
                    status = ''
                    while time.monotonic() < deadline and process.poll() is None:
                        file = session / 'status.txt'
                        try:
                            status = file.read_text() if file.exists() else ''
                        except PermissionError:
                            time.sleep(.05)
                            continue
                        if 'state=ready' in status or 'state=error' in status:
                            break
                        time.sleep(.1)
                    self.assertIn('state=ready', status, status)
                    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
                        udp.settimeout(2)
                        udp.sendto(b'TTV1\x04' + b'\0'*12, ('127.0.0.1', 27360))
                        self.assertTrue(udp.recv(2048).startswith(b'TTV1\x0c\x05'))
                    duplicate = subprocess.run([str(exe), '--host-session', str(root), '127.0.0.1', 'lan', '', uuid.uuid4().hex, 'ABC234'], timeout=5)
                    self.assertNotEqual(duplicate.returncode, 0)
                    self.assertEqual((root / '.updates/hosting/active.txt').read_text(), token)
                    if finish == 'crash':
                        import ctypes
                        from ctypes import wintypes
                        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
                        kernel.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
                        kernel.OpenProcess.restype = wintypes.HANDLE
                        kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
                        kernel.TerminateProcess.argtypes = (wintypes.HANDLE, wintypes.UINT)
                        kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
                        pid = int((root / '.updates/server.pid').read_text().splitlines()[0])
                        # Handle obtained from this test's owned relay before crashing its parent.
                        handle = kernel.OpenProcess(0x100000 | 1, False, pid)
                        self.assertTrue(handle)
                        try:
                            process.kill();process.wait(timeout=5)
                            exited = kernel.WaitForSingleObject(handle, 5000)
                            if exited != 0:
                                kernel.TerminateProcess(handle, 1)
                            self.assertEqual(exited, 0, 'Relay survived its supervisor crash')
                        finally:
                            kernel.CloseHandle(handle)
                        continue
                    if finish == 'stop':
                        (session / 'stop').write_text('stop')
                    else:
                        # Get the real .NET process creation ticks through an owned helper.
                        watch = root / 'watch.cs'
                        watch.write_text('using System;using System.Diagnostics;using System.IO;class W{static void Main(string[] a){var p=Process.GetProcessById(int.Parse(a[0]));File.WriteAllText(a[1],p.Id+"\\n"+p.StartTime.ToUniversalTime().Ticks+"\\n");}}')
                        subprocess.run([str(csc), '/nologo', '/out:' + str(root / 'watch.exe'), str(watch)], check=True)
                        game = subprocess.Popen([str(dist / 'tabletennis.exe')])
                        subprocess.run([str(root / 'watch.exe'), str(game.pid), str(session / 'game.txt')], check=True)
                        game.wait(timeout=5)
                    self.assertEqual(process.wait(timeout=8), 0)
                    self.assertIn('state=stopped', (session / 'status.txt').read_text())
                finally:
                    if process.poll() is None:
                        session.mkdir(parents=True, exist_ok=True)
                        (session / 'stop').write_text('stop')
                        try:
                            process.wait(timeout=5)
                        except subprocess.TimeoutExpired:
                            subprocess.run(['taskkill.exe', '/PID', str(process.pid), '/T', '/F'], capture_output=True)
                            process.wait(timeout=5)
