"""Actual .NET signature verification and loopback release feeds; no real install."""
import contextlib
import hashlib
import http.server
import json
import os
from pathlib import Path
import subprocess
import tempfile
import threading
import unittest

ROOT = Path(__file__).resolve().parents[1]
REPO = 'davidptrssvea/Table-Tennis-Rockstar-PC'


@unittest.skipUnless(os.name == 'nt', 'Windows .NET Framework updater')
class UpdateClientTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix='Table Tennis signed updates ')
        cls.folder = Path(cls.temp.name)
        cls.csc = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        cls.keytool = cls.folder / 'keytool.exe'
        source = cls.folder / 'keytool.cs'
        source.write_text('''using System;using System.IO;using System.Security.Cryptography;
class Keys {static void Main(string[] a) {
 using(var rsa=new RSACryptoServiceProvider(2048,new CspParameters(24))) {
  rsa.PersistKeyInCsp=false;
  if(a[0]=="create") {File.WriteAllText(a[1],rsa.ToXmlString(false));File.WriteAllText(a[2],rsa.ToXmlString(true));}
  else {rsa.FromXmlString(File.ReadAllText(a[1]));File.WriteAllBytes(a[3],rsa.SignData(File.ReadAllBytes(a[2]),"SHA256"));}
 }
}}''')
        subprocess.run([str(cls.csc), '/nologo', '/out:' + str(cls.keytool), str(source)], check=True)
        cls.public, cls.private = cls.folder / 'public.xml', cls.folder / 'private.xml'
        subprocess.run([str(cls.keytool), 'create', str(cls.public), str(cls.private)], check=True)
        cls.helper, cls.production = cls.folder / 'test-updater.exe', cls.folder / 'production-updater.exe'
        common = [str(cls.csc), '/nologo', '/target:winexe', '/platform:x64', '/optimize',
                  '/lib:' + str(cls.csc.parent / 'WPF'),
                  '/r:PresentationCore.dll,PresentationFramework.dll,WindowsBase.dll,System.dll,System.Xaml.dll,System.Web.Extensions.dll']
        code = [str(ROOT / 'project/updater/UpdateCore.cs'), str(ROOT / 'project/updater/Updater.cs')]
        subprocess.run(common + ['/define:UPDATE_TESTING', '/out:' + str(cls.helper),
                                 '/resource:' + str(cls.public) + ',update-public-key.xml'] + code, check=True)
        subprocess.run(common + ['/out:' + str(cls.production),
                                 '/resource:' + str(ROOT / 'project/updater/release-public-key.xml') + ',update-public-key.xml'] + code, check=True)
        probe = cls.folder / 'download.cs'
        probe.write_text('''using System;using TableTennisUpdates;
class Probe {static int Main(string[] a) {try {
 var r=UpdateCore.Check(a[0],true,a[1]);
 UpdateCore.Download(a[0],r,r.manifest.source,r.sourceUrl,()=>false);return 0;
}catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}}''')
        cls.downloader = cls.folder / 'download.exe'
        subprocess.run(common + ['/target:exe', '/out:' + str(cls.downloader), '/r:' + str(cls.helper), str(probe)], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def setUp(self):
        self.case = tempfile.TemporaryDirectory(dir=self.folder)
        self.addCleanup(self.case.cleanup)
        self.root = Path(self.case.name)
        (self.root / 'dist').mkdir()
        (self.root / 'dist/settings.ini').write_text('iso_path=local-only.iso\n')
        (self.root / 'dist/tabletennis.exe').write_bytes(b'installed version unchanged')
        (self.root / 'dist/install.json').write_text(json.dumps(dict(version='1.0.0', repository=REPO)))
        self.payload = b'source package placeholder'
        self.manifest = dict(schema=1, minimum_updater=1, version='1.2.0', repository=REPO, notes='Test release',
                             source=dict(asset='source.zip', sha256=hashlib.sha256(self.payload).hexdigest(), size=len(self.payload)),
                             sdk=dict(asset='sdk.zip', sha256=hashlib.sha256(b'sdk').hexdigest(), size=3))
        self.routes = {}
        self.requests = []
        owner = self
        class Handler(http.server.BaseHTTPRequestHandler):
            def do_GET(self):
                owner.requests.append(self.path)
                content = owner.routes.get(self.path)
                if content is None:
                    self.send_error(404)
                    return
                self.send_response(200)
                self.send_header('Content-Length', str(len(content)))
                self.end_headers()
                self.wfile.write(content)
            def log_message(self, *args):
                pass
        self.server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.close_server)
        self.url = f'http://127.0.0.1:{self.server.server_port}'
        self.publish()

    def close_server(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=2)

    def publish(self, foreign=False, assets=True):
        prefix = '/' + REPO + '/releases/download/v1.2.0/'
        path = self.root / 'manifest.json'
        path.write_text(json.dumps(self.manifest))
        signature = self.root / 'manifest.sig'
        subprocess.run([str(self.keytool), 'sign', str(self.private), str(path), str(signature)], check=True)
        for name, value in {'tabletennis-update.json': path.read_bytes(),
                            'tabletennis-update.json.sig': signature.read_bytes(),
                            'source.zip': self.payload, 'sdk.zip': b'sdk'}.items():
            self.routes[prefix + name] = value
        release_assets = [dict(name=n, browser_download_url=(self.url + prefix + n)) for n in
                          ('tabletennis-update.json', 'tabletennis-update.json.sig', 'source.zip', 'sdk.zip')]
        if foreign:
            release_assets[2]['browser_download_url'] = 'https://example.com/source.zip'
        self.routes['/latest'] = json.dumps(dict(tag_name='v1.2.0', draft=False, prerelease=False,
                                                 assets=release_assets if assets else [])).encode()

    def check(self, success=True, force=True):
        result = subprocess.run([str(self.helper), '--check-only', '--root', str(self.root),
                                 '--test-api', self.url + '/latest'] + (['--force'] if force else []),
                                capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode == 0, success, result.stderr)
        self.assertEqual((self.root / 'dist/tabletennis.exe').read_bytes(), b'installed version unchanged')
        return dict(line.split('=', 1) for line in (self.root / '.updates/status.txt').read_text().splitlines())

    def test_signed_new_release_detected_without_installing(self):
        status = self.check()
        self.assertEqual(status['state'], 'available')
        self.assertEqual(status['version'], '1.2.0')
        self.assertFalse(any(path.endswith('source.zip') for path in self.requests))

    def test_same_or_older_version_not_offered(self):
        (self.root / 'dist/install.json').write_text(json.dumps(dict(version='1.2.0', repository=REPO)))
        self.assertEqual(self.check()['state'], 'up_to_date')
        (self.root / 'dist/install.json').write_text(json.dumps(dict(version='2.0.0', repository=REPO)))
        self.assertEqual(self.check()['state'], 'up_to_date')

    def test_tampered_manifest_and_signature_refused(self):
        key = '/' + REPO + '/releases/download/v1.2.0/tabletennis-update.json'
        self.routes[key] += b' '
        self.assertEqual(self.check(False)['state'], 'error')
        self.publish()
        key += '.sig'
        self.routes[key] = bytes([self.routes[key][0] ^ 1]) + self.routes[key][1:]
        self.assertEqual(self.check(False)['state'], 'error')

    def test_real_trust_key_rejects_test_key_signature(self):
        result = subprocess.run([str(self.production), '--verify-manifest', str(self.root / 'manifest.json'),
                                 str(self.root / 'manifest.sig')], capture_output=True, text=True, timeout=5)
        self.assertNotEqual(result.returncode, 0)
        self.assertNotIn('--test-api'.encode('utf-16-le'), self.production.read_bytes())

    def test_foreign_download_and_newer_protocol_refused(self):
        self.publish(foreign=True)
        self.assertEqual(self.check(False)['state'], 'error')
        self.manifest['minimum_updater'] = 2
        self.publish()
        self.assertEqual(self.check(False)['state'], 'error')

    def test_unsigned_legacy_release_does_not_offer_install(self):
        self.publish(assets=False)
        self.assertEqual(self.check()['state'], 'up_to_date')

    def test_successful_check_cached_without_more_network_requests(self):
        self.check()
        count = len(self.requests)
        self.assertEqual(self.check(force=False)['state'], 'available')
        self.assertEqual(len(self.requests), count)

    def test_corrupt_package_is_removed_without_activation(self):
        key = '/' + REPO + '/releases/download/v1.2.0/source.zip'
        self.routes[key] = b'x' * len(self.payload)
        result = subprocess.run([str(self.downloader), str(self.root), self.url + '/latest'],
                                capture_output=True, text=True, timeout=15)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.root / '.updates/downloads/v1.2.0/source.zip').exists())
        self.assertFalse(list((self.root / '.updates').rglob('*.download')))
        self.assertEqual((self.root / 'dist/tabletennis.exe').read_bytes(), b'installed version unchanged')

    def test_verified_package_download_does_not_install_it(self):
        result = subprocess.run([str(self.downloader), str(self.root), self.url + '/latest'],
                                capture_output=True, text=True, timeout=15)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual((self.root / '.updates/downloads/v1.2.0/source.zip').read_bytes(), self.payload)
        self.assertEqual((self.root / 'dist/tabletennis.exe').read_bytes(), b'installed version unchanged')
