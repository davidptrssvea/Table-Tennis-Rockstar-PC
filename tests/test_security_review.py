"""Adversarial relay/UPnP regression tests. All traffic stays on loopback."""
import asyncio
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import random
import socketserver
import struct
import subprocess
import tempfile
import threading
import unittest
from unittest import mock
from http.server import BaseHTTPRequestHandler, HTTPServer

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('security_relay', ROOT / 'server/ttlink_server.py')
relay = importlib.util.module_from_spec(spec)
spec.loader.exec_module(relay)


class Transport:
    def __init__(self):
        self.sent = []

    def sendto(self, data, addr):
        self.sent.append((data, addr))


def hello(room='ABC234', nonce=1, name=b'Friend', local=0x7F000001):
    room = room.encode('ascii')
    return b'TTV1\x01' + struct.pack('>BQ', 1, nonce) + bytes([len(name)]) + name + bytes([len(room)]) + room + struct.pack('>IHh', local, 11000, 60)


def confirmed(server, addr, name=b'Friend'):
    server.datagram_received(hello(name=name), addr)
    client = server.by_addr[addr]
    server.datagram_received(b'TTV1\x04' + struct.pack('>IQ', client.id, client.token), addr)
    return client


class RelaySecurityTests(unittest.TestCase):
    def server(self, **options):
        server = relay.Server(**options)
        server.connection_made(Transport())
        return server

    def test_host_rejects_other_rooms_and_limits_clients(self):
        server = self.server(allowed_room='ABC234', max_clients=4)
        for i, room in enumerate(('', 'DEF567', 'GHJ789')):
            server.datagram_received(hello(room), ('127.0.0.1', 10000+i))
        self.assertFalse(server.clients)
        self.assertFalse(server.rooms)
        self.assertFalse(server.hello_limits)
        self.assertFalse(server.transport.sent)
        for i in range(6):
            server.datagram_received(hello(name=('Friend%d' % i).encode()), ('127.0.0.%d' % (i+1), 20000))
        self.assertEqual(len(server.clients), 4)

    def test_no_members_or_relay_to_unconfirmed_address(self):
        server = self.server()
        server.datagram_received(hello(), ('127.0.0.1', 20000))
        pending = server.by_addr[('127.0.0.1', 20000)]
        self.assertFalse(pending.confirmed)
        self.assertEqual([p[0][4] for p in server.transport.sent], [relay.WELCOME])
        active = confirmed(server, ('127.0.0.1', 20001), b'Host')
        server.transport.sent.clear()
        server.datagram_received(b'TTV1\x06' + struct.pack('>IQIHH', active.id, active.token, pending.vip, 1000, 1000) + b'payload', active.addr)
        self.assertFalse(server.transport.sent)
        admitted = pending.last_seen
        with mock.patch.object(relay.time, 'monotonic', return_value=admitted+2):
            server.datagram_received(hello(), pending.addr)
        self.assertEqual(pending.last_seen, admitted, 'Unverified HELLO must not extend a reservation')

    def test_member_list_never_contains_claimed_lan_probe_address(self):
        server = self.server()
        confirmed(server, ('127.0.0.1', 20000), b'Host')
        confirmed(server, ('127.0.0.1', 20001))
        members = [data for data, _ in server.transport.sent if data[4] == relay.MEMBERS]
        self.assertTrue(members)
        entries = 0
        for packet in members:
            _, count = struct.unpack_from('>HH', packet, 5)
            pos = 9
            for _ in range(count):
                fields = struct.unpack_from('>IIQIHIHB', packet, pos)
                self.assertEqual(fields[5:7], (0, 0))
                pos += 29 + fields[-1]
                entries += 1
        self.assertGreater(entries, 0)

    def test_spoofed_hello_cannot_evict_active_confirmed_client(self):
        server = self.server()
        active = confirmed(server, ('127.0.0.1', 20000))
        server.datagram_received(hello(nonce=999), active.addr)
        self.assertIs(server.by_addr[active.addr], active)
        self.assertIn(active.id, server.clients)

    def test_unknown_errors_and_unverified_hellos_are_globally_bounded(self):
        with mock.patch.object(relay.time, 'monotonic', return_value=0):
            server = self.server()
            for i in range(10000):
                server.datagram_received(b'TTV1\x04' + bytes(12), ('10.%d.%d.%d' % (i//65536, i//256 % 256, i%256), 10000))
            self.assertLessEqual(len(server.transport.sent), 50)
            self.assertFalse(server.clients)
            server = self.server()
            for i in range(10000):
                server.datagram_received(hello(), ('10.%d.%d.%d' % (i//65536, i//256 % 256, i%256), 10000))
            self.assertLessEqual(len(server.clients), 20)
            self.assertLessEqual(len(server.hello_limits), 20)
            self.assertFalse(any(data[4] == relay.MEMBERS for data, _ in server.transport.sent))

    def test_invalid_lengths_utf8_and_seeded_fuzz_do_not_escape_callback(self):
        rng = random.Random(20261104)
        server = self.server()
        cases = [hello()+b'trailing', hello(name=b'\xff'), hello(name=b''), hello(local=0xFFFFFFFF)[:-1]]
        for _ in range(5000):
            data = bytearray(hello())
            for _ in range(rng.randrange(1, 5)):
                data[rng.randrange(len(data))] = rng.randrange(256)
            cases.append(bytes(data[:rng.randrange(len(data)+1)]))
        for i, data in enumerate(cases):
            server.ingress.tokens = 5000
            server.hellos.tokens = 20
            server.datagram_received(data, ('127.0.0.1', 20000+i))
        self.assertLessEqual(len(server.clients), relay.MAX_PER_IP)
        self.assertLessEqual(len(server.rooms), len(server.clients))

    def test_egress_budget_and_zero_ports_are_enforced(self):
        server = self.server()
        host = confirmed(server, ('127.0.0.1', 20000), b'Host')
        guest = confirmed(server, ('127.0.0.1', 20001))
        server.transport.sent.clear()
        packet = lambda port: b'TTV1\x06' + struct.pack('>IQIHH', host.id, host.token, guest.vip, port, 1000) + bytes(100)
        server.datagram_received(packet(0), host.addr)
        self.assertFalse(server.transport.sent)
        server.datagram_received(b'TTV1\x06' + struct.pack('>IQIHH', host.id, host.token, guest.vip, 0, 0) + b'\x01' + bytes(12), host.addr)
        self.assertEqual(server.transport.sent[-1][0][4], relay.RELAYED)
        server.transport.sent.clear()
        server.egress_bytes.tokens = 0
        server.egress_bytes.rate = 0
        server.datagram_received(packet(1000), host.addr)
        self.assertFalse(server.transport.sent)


class HttpSecurityTests(unittest.IsolatedAsyncioTestCase):
    async def request(self, text, peer='127.0.0.1', connections=0):
        server = relay.Server()
        server.http_connections = connections
        reader = asyncio.StreamReader()
        reader.feed_data(text)
        reader.feed_eof()
        class Writer:
            def __init__(self): self.data = b''; self.closed = False
            def get_extra_info(self, name): return (peer, 10000)
            def write(self, data): self.data += data
            async def drain(self): pass
            def close(self): self.closed = True
        writer = Writer()
        await relay.http_status(server, reader, writer)
        self.assertTrue(writer.closed)
        self.assertEqual(server.http_connections, connections)
        return writer.data

    async def test_status_is_local_bounded_and_not_browser_cross_origin(self):
        valid = b'GET /status HTTP/1.1\r\nHost: localhost:27360\r\n\r\n'
        reply = await self.request(valid)
        self.assertIn(b'200 OK', reply)
        self.assertNotIn(b'Access-Control-Allow-Origin', reply)
        self.assertFalse(await self.request(valid, peer='192.168.1.2'))
        self.assertFalse(await self.request(valid, connections=8))
        for data in (valid.replace(b'localhost', b'attacker.example'), valid.replace(b'\r\n\r\n', b'\r\nOrigin: https://attacker.example\r\n\r\n'), valid.replace(b'GET', b'POST'), valid.replace(b'/status', b'/status-extra'), b'GET /status HTTP/1.1\r\n'):
            self.assertFalse(await self.request(data))


@unittest.skipUnless(os.name == 'nt', 'Windows .NET launcher code')
class GatewaySecurityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix='Table Tennis UPnP security ')
        cls.folder = Path(cls.temp.name)
        source = cls.folder / 'probe.cs'
        source.write_text('''using System;using System.Net;using TableTennisLauncher;
class Probe {
 static void Check(bool value){if(!value)throw new Exception("Gateway security invariant failed");}
 static void Main(string[] args){
  if(args[0]=="policy") {
   var gateway=IPAddress.Parse("192.168.1.1");
   Check(GatewayLeaseRouter.Pinned(new Uri("http://192.168.1.1:49000/control"),gateway));
   foreach(string url in new[]{"http://8.8.8.8/control","http://router.example/control","file:///C:/file","https://192.168.1.1/control","http://user:pass@192.168.1.1/control","http://192.168.1.1/control#part"}) Check(!GatewayLeaseRouter.Pinned(new Uri(url),gateway));
   foreach(string xml in new[]{"<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><x>&e;</x>",new string('a',262145),new string('<',500)}) {
    bool rejected=false;try{GatewayLeaseRouter.ParseXml(xml);}catch{rejected=true;}Check(rejected);
   }
   string deep="";for(int i=0;i<40;i++)deep+="<x>";for(int i=0;i<40;i++)deep+="</x>";
   bool blocked=false;try{GatewayLeaseRouter.ParseXml(deep);}catch{blocked=true;}Check(blocked);return;
  }
  bool failed=false;IHostRouter router=null;
  try {
   router=new GatewayLeaseRouter(IPAddress.Loopback,new Uri(args[1]),"urn:schemas-upnp-org:service:WANIPConnection:1");
   HostSession.OpenRouter(router,27360,"192.168.1.5","owned");
   HostSession.RenewRouter(router,27360,"192.168.1.5","owned");
  } catch {failed=true;}
  finally {if(router!=null){HostSession.RemoveRouter(router,27360,"192.168.1.5","owned");router.Dispose();}}
  Check(failed==(args[0]!="finite"));
 }
}''')
        csc = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
        cls.exe = cls.folder / 'probe.exe'
        subprocess.run([str(csc), '/nologo', '/out:' + str(cls.exe), str(source), str(ROOT/'project/launcher/HostSession.cs'), str(ROOT/'project/launcher/GatewayLeaseRouter.cs')], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def test_endpoint_and_xml_policy(self):
        subprocess.run([str(self.exe), 'policy'], check=True, timeout=10)

    def test_redirect_and_external_xml_entity_are_never_followed(self):
        hits = []
        class Target(BaseHTTPRequestHandler):
            def log_message(self, *args): pass
            def do_GET(self):
                hits.append(self.path)
                self.send_response(200);self.end_headers();self.wfile.write(b'not accessible')
        target = HTTPServer(('127.0.0.1', 0), Target)
        target_thread = threading.Thread(target=target.serve_forever, daemon=True)
        target_thread.start()
        try:
            for attack in ('redirect', 'entity', 'oversize'):
                class Malicious(BaseHTTPRequestHandler):
                    def log_message(self, *args): pass
                    def do_POST(self):
                        self.rfile.read(int(self.headers['Content-Length']))
                        if attack == 'redirect':
                            self.send_response(302);self.send_header('Location', 'http://127.0.0.1:%d/unwanted' % target.server_port);self.end_headers()
                        else:
                            data = ('<!DOCTYPE x [<!ENTITY e SYSTEM "http://127.0.0.1:%d/unwanted">]><x>&e;</x>' % target.server_port).encode() if attack=='entity' else bytes(262145)
                            self.send_response(200);self.send_header('Content-Length', str(len(data)));self.end_headers()
                            try: self.wfile.write(data)
                            except OSError: pass
                server = HTTPServer(('127.0.0.1', 0), Malicious)
                thread = threading.Thread(target=server.serve_forever, daemon=True)
                thread.start()
                try:
                    subprocess.run([str(self.exe), attack, 'http://127.0.0.1:%d/control' % server.server_port], check=True, timeout=10)
                    self.assertFalse(hits)
                finally:
                    server.shutdown();server.server_close();thread.join()
        finally:
            target.shutdown();target.server_close();target_thread.join()

    def test_finite_lease_foreign_conflict_and_permanent_lease_refusal(self):
        for scenario in ('finite', 'foreign', 'permanent'):
            state = dict(mapping=None, adds=[], deletes=0)
            if scenario == 'foreign':
                state['mapping'] = dict(client='192.168.1.6', description='other', lease=300)
            class Handler(BaseHTTPRequestHandler):
                def log_message(self, *args): pass
                def do_POST(self):
                    import xml.etree.ElementTree as ET
                    payload = self.rfile.read(int(self.headers['Content-Length']))
                    xml = ET.fromstring(payload)
                    action = xml[0][0]
                    name = action.tag.split('}')[-1]
                    values = {x.tag.split('}')[-1]: x.text or '' for x in action}
                    output = {}
                    fault = False
                    if name == 'GetExternalIPAddress': output = dict(NewExternalIPAddress='8.8.8.8')
                    elif name == 'GetSpecificPortMappingEntry':
                        mapping = state['mapping']
                        if mapping is None: fault = True
                        else: output = dict(NewInternalPort='27360', NewInternalClient=mapping['client'], NewPortMappingDescription=mapping['description'], NewLeaseDuration=str(mapping['lease']), NewEnabled='1')
                    elif name == 'AddPortMapping':
                        state['adds'].append(values)
                        state['mapping'] = dict(client=values['NewInternalClient'], description=values['NewPortMappingDescription'], lease=0 if scenario=='permanent' else 300)
                    elif name == 'DeletePortMapping': state['deletes'] += 1; state['mapping'] = None
                    if fault: body = '<s:Fault><detail><UPnPError><errorCode>714</errorCode></UPnPError></detail></s:Fault>'
                    else: body = '<u:%sResponse xmlns:u="urn:schemas-upnp-org:service:WANIPConnection:1">%s</u:%sResponse>' % (name, ''.join('<%s>%s</%s>' % (k,v,k) for k,v in output.items()), name)
                    data = ('<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body>'+body+'</s:Body></s:Envelope>').encode()
                    self.send_response(500 if fault else 200)
                    self.send_header('Content-Length', str(len(data)))
                    self.end_headers();self.wfile.write(data)
            server = HTTPServer(('127.0.0.1', 0), Handler)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            try:
                subprocess.run([str(self.exe), scenario, 'http://127.0.0.1:%d/control' % server.server_port], check=True, timeout=15)
                if scenario == 'foreign':
                    self.assertFalse(state['adds']);self.assertEqual(state['deletes'], 0)
                else:
                    self.assertTrue(state['adds']);self.assertTrue(all(x['NewLeaseDuration']=='300' for x in state['adds']))
                    self.assertEqual(state['deletes'], 1);self.assertIsNone(state['mapping'])
            finally:
                server.shutdown();server.server_close();thread.join()
