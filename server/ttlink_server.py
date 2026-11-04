#!/usr/bin/env python3
"""Table Tennis online server: a virtual LAN for the game's System Link play.

Players who connect to the same room form one network. The game's System Link broadcasts
(match search and announce) are passed to the other players in the room, so the game's own
QUICK MATCH and CREATE MATCH menus work as on a LAN. Match traffic then goes straight between
the two players after UDP hole punching coordinated here, or through this server when that
fails.

One UDP port carries the game protocol; the same port number on TCP answers GET /status with
JSON (players and rooms online). Python 3.8 or newer, standard library only.

    python3 ttlink_server.py [--port 27360] [--bind 0.0.0.0]

Nearby matchmaking: clients report their round trip to the server and their UTC offset (no
location). In the public room a broadcast (match search or announce) first reaches only players
whose estimated round trip to the sender is short, and the radius widens the longer a player
keeps searching; see `reach()` and server/README.md. Private rooms are not filtered.

Limits against abuse: traffic is only relayed between players in the same room, every client
has a packet and byte budget, broadcasts are capped, clients that stop sending keepalives are
dropped after 30 s, and the number of clients per IP address and per room is limited. Nothing
is stored: the server keeps only player names, room codes and addresses in memory while
players are connected.
"""
import argparse
import asyncio
import ipaddress
import json
import logging
import os
import random
import secrets
import socket
import struct
import time

MAGIC = b"TTV1"
# Message types (see the client in the SDK's xsocket.cpp).
HELLO, WELCOME, MEMBERS, KEEPALIVE, STATUS, DATA, RELAYED, BYE, PUNCH, PUNCH_ACK, DIRECT, ERROR = range(1, 13)

ROOM_ALPHABET = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"  # no 0/O, 1/I/L
ROOM_LENGTH = 6
PUBLIC_ROOM = ""
BROADCAST = 0xFFFFFFFF
VIRTUAL_NET = 0x0A580000  # 10.88.0.0/16: room-local addresses the games use for each other

MAX_PAYLOAD = 1400
CLIENT_TIMEOUT = 30.0
MEMBERS_REFRESH = 10.0
MAX_CLIENTS = 128
MAX_PER_IP = 8
MAX_PUBLIC_ROOM = 32
MAX_PRIVATE_ROOM = 16
# Per client: sustained packets and bytes per second, with a burst allowance of two seconds.
PACKET_RATE = 400
BYTE_RATE = 256 * 1024
BROADCAST_RATE = 20
HELLO_RATE_PER_IP = 5
MAX_HELLO_IPS = 4096
NAME_RECLAIM = 6.0  # seconds without keepalives (sent every 3 s) before a reconnect may take a name

ERR_VERSION, ERR_FULL, ERR_ROOM, ERR_LIMIT, ERR_UNKNOWN, ERR_NAME = 1, 2, 3, 4, 5, 6

# Online protocol versions this server accepts (the version byte in HELLO). The version covers
# everything players exchange, with the server and with each other, on every platform: bump it
# in the client whenever either changes. Each version gets rooms of its own, so players on
# different versions never meet, and an older version keeps working until it is dropped here.
SUPPORTED_VERSIONS = {1}

# Nearby matchmaking (public room only). A player's search radius, as an estimated round trip
# in milliseconds, grows with the age of its current search streak: a streak starts with a
# broadcast after SEARCH_QUIET seconds without one. Two players see each other's broadcasts when
# their estimated round trip is within the larger of their two radii.
SEARCH_QUIET = 60.0
SEARCH_RADIUS = ((6.0, 100), (20.0, 200), (40.0, 400))  # (streak age below, radius ms); then all
NEARBY_RTT = 150  # "nearby" in the STATUS count


class Bucket:
    """Token bucket: rate per second, capacity of `burst` seconds."""

    __slots__ = ("rate", "capacity", "tokens", "stamp")

    def __init__(self, rate, burst=2.0):
        self.rate = rate
        self.capacity = rate * burst
        self.tokens = self.capacity
        self.stamp = time.monotonic()

    def take(self, amount=1.0):
        now = time.monotonic()
        self.tokens = min(self.capacity, self.tokens + (now - self.stamp) * self.rate)
        self.stamp = now
        if self.tokens < amount:
            return False
        self.tokens -= amount
        return True


class Client:
    __slots__ = ("id", "token", "nonce", "addr", "name", "room", "vip", "local_ip", "local_port",
                 "last_seen", "packets", "bytes", "broadcasts", "relayed_bytes", "joined", "rtt",
                 "utc_offset", "streak_start", "last_broadcast", "_reach", "key", "confirmed")

    def __init__(self, cid, token, nonce, addr, name, room, vip, local_ip, local_port):
        self.id = cid
        self.token = token
        self.nonce = nonce
        self.addr = addr
        self.name = name
        self.room = room
        self.vip = vip
        self.local_ip = local_ip
        self.local_port = local_port
        self.last_seen = time.monotonic()
        self.packets = Bucket(PACKET_RATE)
        self.bytes = Bucket(BYTE_RATE)
        self.broadcasts = Bucket(BROADCAST_RATE)
        self.relayed_bytes = 0
        self.joined = time.time()
        self.rtt = None          # client's round trip to this server (ms), from its keepalives
        self.utc_offset = None   # minutes east of UTC, from HELLO
        self.streak_start = 0.0  # current search streak (see SEARCH_RADIUS)
        self.last_broadcast = 0.0
        self._reach = None        # last logged search reach
        self.key = room           # key in Server.rooms (see room_key)
        self.confirmed = False    # Return-path proof before membership/fan-out.


def room_key(version, room):
    """Rooms of different protocol versions are kept apart; version 1 uses the plain code."""
    return room if version == 1 else "v%d/%s" % (version, room)


def estimate_rtt(a, b):
    """Estimated round trip between two players in ms, or None when nothing is known.

    Two rough measures, and the smaller one wins:
    - the sum of both players' round trips to this server: the path through the server is an
      upper bound, close for players on opposite sides of it, too high for two players near
      each other but far from the server;
    - the difference in UTC offset: 30 ms plus 25 ms per hour, which catches the second case.
    """
    estimates = []
    if a.rtt is not None and b.rtt is not None:
        estimates.append(a.rtt + b.rtt)
    if a.utc_offset is not None and b.utc_offset is not None:
        estimates.append(30 + 25 * abs(a.utc_offset - b.utc_offset) / 60.0)
    return min(estimates) if estimates else None


def radius(client, now):
    """Search radius in ms for a player, from the age of its search streak (None = everyone)."""
    if now - client.last_broadcast > SEARCH_QUIET:
        return SEARCH_RADIUS[0][1]
    age = now - client.streak_start
    for limit, value in SEARCH_RADIUS:
        if age < limit:
            return value
    return None


def reaches(sender, target, now):
    """Whether a public-room broadcast from sender is passed to target."""
    estimate = estimate_rtt(sender, target)
    if estimate is None:
        return True
    widest = [radius(sender, now), radius(target, now)]
    if None in widest:
        return True
    return estimate <= max(widest)


def ip_to_int(ip):
    try:
        return int(ipaddress.IPv4Address(ip))
    except ValueError:
        return 0


class Server(asyncio.DatagramProtocol):
    def __init__(self, allowed_room=None, max_clients=MAX_CLIENTS):
        if allowed_room is not None and (len(allowed_room) != ROOM_LENGTH or any(c not in ROOM_ALPHABET for c in allowed_room)):
            raise ValueError('A host needs one valid private room')
        if not 1 <= max_clients <= MAX_CLIENTS:
            raise ValueError('Invalid client limit')
        self.allowed_room, self.max_clients = allowed_room, max_clients
        self.transport = None
        self.clients = {}       # id -> Client
        self.by_addr = {}       # (ip, port) -> Client
        self.rooms = {}         # code -> {vip: Client}
        self.hello_limits = {}  # ip -> Bucket
        self.relayed_total = 0
        self.started = time.time()
        self.ingress = Bucket(5000, 1)
        self.hellos = Bucket(20, 1)
        self.egress_packets = Bucket(5000, 1)
        self.egress_bytes = Bucket(4 * 1024 * 1024, 1)
        self.control_replies = Bucket(50, 1)
        self.member_limits = {}
        self.dirty_rooms = set()
        self.http_connections = 0
        self.http_requests = Bucket(10, 1)
        self.transport_errors = Bucket(1, 1)

    # --- transport ---------------------------------------------------------------------------
    def connection_made(self, transport):
        self.transport = transport

    def error_received(self, exc):
        if self.transport_errors.take():
            logging.warning("UDP transport reported an error: %s", exc)

    def send(self, addr, kind, body=b""):
        return self.send_raw(MAGIC + bytes([kind]) + body, addr)

    def send_raw(self, message, addr):
        if not self.egress_packets.take() or not self.egress_bytes.take(len(message)):
            return False
        try:
            self.transport.sendto(message, addr)
            return True
        except OSError:
            return False

    def error(self, addr, code, text):
        if not self.control_replies.take():
            return
        data = text.encode()[:200]
        self.send(addr, ERROR, struct.pack(">BB", code, len(data)) + data)

    def datagram_received(self, data, addr):
        if not self.ingress.take() or len(data) < 5 or data[:4] != MAGIC or len(data) > MAX_PAYLOAD + 64:
            return
        try:
            source = ipaddress.IPv4Address(addr[0])
            if not 1 <= addr[1] <= 65535 or source.is_multicast or source.is_unspecified or source.is_reserved:
                return
        except (ValueError, IndexError, TypeError):
            return
        kind = data[4]
        body = data[5:]
        try:
            if kind == HELLO:
                self.on_hello(body, addr)
            elif kind in (KEEPALIVE, DATA, BYE):
                if (kind == KEEPALIVE and len(body) not in (12, 14) or
                    kind == BYE and len(body) != 12 or
                    kind == DATA and not 20 <= len(body) <= 20 + MAX_PAYLOAD):
                    return
                client = self.authenticate(body, addr)
                if client is None:
                    if kind != BYE:
                        self.error(addr, ERR_UNKNOWN, "unknown client")
                    return
                if not client.packets.take() or not client.bytes.take(len(data)):
                    return
                client.last_seen = time.monotonic()
                if kind != BYE and not client.confirmed:
                    client.confirmed = True
                    self.send_members(client.key)
                if kind == KEEPALIVE:
                    if len(body) >= 14:
                        rtt = struct.unpack_from(">H", body, 12)[0]
                        client.rtt = rtt if rtt < 0xFFFF else None
                    self.on_keepalive(client)
                elif kind == DATA:
                    self.on_data(client, body[12:])
                else:
                    self.remove(client, "left")
        except (struct.error, IndexError, UnicodeDecodeError):
            return

    def authenticate(self, body, addr):
        cid, token = struct.unpack_from(">IQ", body, 0)
        client = self.clients.get(cid)
        if client is None or client.token != token or client.addr != addr:
            return None
        return client

    # --- messages ----------------------------------------------------------------------------
    def on_hello(self, body, addr):
        if not self.hellos.take():
            return
        version, nonce = struct.unpack_from(">BQ", body, 0)
        pos = 9
        name_len = body[pos]
        if not 1 <= name_len <= 60:
            return
        name = body[pos + 1:pos + 1 + name_len].decode("utf-8")
        pos += 1 + name_len
        room_len = body[pos]
        if room_len not in (0, ROOM_LENGTH):
            return
        room = body[pos + 1:pos + 1 + room_len].decode("ascii").upper()
        pos += 1 + room_len
        local_ip, local_port = struct.unpack_from(">IH", body, pos)
        pos += 6
        if len(body) not in (pos, pos + 2) or local_port == 0:
            return
        if self.allowed_room is not None and room != self.allowed_room:
            return  # No room enumeration or error amplification for strangers.
        bucket = self.hello_limits.get(addr[0])
        if bucket is None:
            if len(self.hello_limits) >= MAX_HELLO_IPS:
                return
            bucket = self.hello_limits[addr[0]] = Bucket(HELLO_RATE_PER_IP)
        if not bucket.take():
            return
        utc_offset = struct.unpack_from(">h", body, pos)[0] if len(body) >= pos + 2 else None
        if version not in SUPPORTED_VERSIONS:
            self.error(addr, ERR_VERSION, "this server speaks a different version; update the game")
            return
        # The wire format carries at most 15 UTF-8 bytes, not 15 Unicode chars.
        name = "".join(c for c in name if c.isprintable()).encode("utf-8")[:15].decode("utf-8", "ignore") or "Player"
        if room and (len(room) != ROOM_LENGTH or any(c not in ROOM_ALPHABET for c in room)):
            self.error(addr, ERR_ROOM, "invalid room code")
            return
        existing = self.by_addr.get(addr)
        if existing is not None:
            if existing.nonce == nonce:  # a resent HELLO: answer it again
                self.welcome(existing)
                return
            if time.monotonic() - existing.last_seen <= NAME_RECLAIM:
                self.error(addr, ERR_LIMIT, "session is active; retry after disconnect")
                return
            self.remove(existing, "reconnected")
        if len(self.clients) >= self.max_clients:
            self.error(addr, ERR_FULL, "server is full")
            return
        if sum(1 for c in self.clients.values() if c.addr[0] == addr[0]) >= MAX_PER_IP:
            self.error(addr, ERR_LIMIT, "too many players from this address")
            return
        key = room_key(version, room)
        members = self.rooms.setdefault(key, {})
        # The game tells players apart only by name (every copy has the same Xbox ID), so two
        # players with one name must never share a room. A silent entry from the same address
        # is this player reconnecting from a new port, which replaces it.
        clash = next((m for m in members.values() if m.name.casefold() == name.casefold()), None)
        if clash is not None:
            if clash.addr[0] == addr[0] and time.monotonic() - clash.last_seen > NAME_RECLAIM:
                self.remove(clash, "reconnected")
                members = self.rooms.setdefault(key, {})
            else:
                if not members:
                    del self.rooms[key]
                self.error(addr, ERR_NAME, "the name %s is taken here - change it in pc settings" % name)
                return
        limit = MAX_PUBLIC_ROOM if room == PUBLIC_ROOM else MAX_PRIVATE_ROOM
        if len(members) >= limit:
            self.error(addr, ERR_FULL, "room is full")
            return
        free = [n for n in range(1, 255) if (VIRTUAL_NET | n) not in members]
        vip = VIRTUAL_NET | random.choice(free)
        cid = secrets.randbits(32) or 1
        while cid in self.clients:
            cid = secrets.randbits(32) or 1
        client = Client(cid, secrets.randbits(64), nonce, addr, name, room, vip, local_ip, local_port)
        client.key = key
        if utc_offset is not None and -14 * 60 <= utc_offset <= 14 * 60:
            client.utc_offset = utc_offset
        self.clients[cid] = client
        self.by_addr[addr] = client
        members[vip] = client
        logging.info("%s joined %s as %s (%d in room, %d online)", name,
                     "the public room" if room == PUBLIC_ROOM else "a private room",
                     ipaddress.IPv4Address(vip), len(members), len(self.clients))
        self.welcome(client)
        # Do not introduce an address that has not proved it received WELCOME.

    def welcome(self, client):
        room = client.room.encode()
        body = struct.pack(">IIQB", client.id, client.vip, client.token, len(room)) + room
        body += struct.pack(">IH", ip_to_int(client.addr[0]), client.addr[1])
        self.send(client.addr, WELCOME, body)

    def send_members(self, room):
        members = [c for c in self.rooms.get(room, {}).values() if c.confirmed]
        if not members:
            return
        limit = self.member_limits.setdefault(room, Bucket(2, 1))
        if not limit.take():
            self.dirty_rooms.add(room)
            return
        self.dirty_rooms.discard(room)
        for target in members:
            entries = []
            for m in members:
                if m is target:
                    continue
                name = m.name.encode()[:15]
                entries.append(struct.pack(">IIQIHIHB", m.id, m.vip, m.token, ip_to_int(m.addr[0]),
                                           m.addr[1], 0, 0, len(name)) + name)
                # Never make a peer probe an arbitrary advertised LAN/loopback
                # address. The observed, return-path-confirmed endpoint suffices;
                # the relay remains available when direct traffic cannot work.
            # Keep each message well under the payload limit; large rooms get several messages.
            chunk = []
            size = 0
            for entry in entries + [None]:
                if entry is None or size + len(entry) > 1200:
                    body = struct.pack(">HH", len(members) - 1, len(chunk)) + b"".join(chunk)
                    self.send(target.addr, MEMBERS, body)
                    chunk, size = [], 0
                if entry is not None:
                    chunk.append(entry)
                    size += len(entry)

    def on_keepalive(self, client):
        members = self.rooms.get(client.key, {})
        nearby = 0
        if client.room == PUBLIC_ROOM:
            nearby = sum(1 for m in members.values()
                         if m is not client and (estimate_rtt(client, m) or 9999) <= NEARBY_RTT)
        self.send(client.addr, STATUS, struct.pack(">HIHH", len(members), len(self.clients),
                                                    len(self.rooms), nearby))

    def on_data(self, client, body):
        dst_vip, src_port, dst_port = struct.unpack_from(">IHH", body, 0)
        payload = body[8:]
        control = (src_port == dst_port == 0 and dst_vip != BROADCAST and
                   len(payload) in (13, 15) and payload[0] in (1, 2))
        if len(payload) > MAX_PAYLOAD or (not src_port or not dst_port) and not control:
            return
        members = self.rooms.get(client.key, {})
        message = MAGIC + bytes([RELAYED]) + struct.pack(">IHH", client.vip, src_port, dst_port) + payload
        if dst_vip == BROADCAST:
            if not client.broadcasts.take():
                return
            now = time.monotonic()
            if now - client.last_broadcast > SEARCH_QUIET:
                client.streak_start = now
            client.last_broadcast = now
            targets = [m for m in members.values() if m is not client and m.confirmed]
            if client.room == PUBLIC_ROOM:
                everyone = len(targets)
                targets = [m for m in targets if reaches(client, m, now)]
                if now == client.streak_start or len(targets) != getattr(client, "_reach", None):
                    client._reach = len(targets)
                    r = radius(client, now)
                    logging.info("%s searches within %s: reaches %d of %d players", client.name,
                                 "%d ms" % r if r else "any distance", len(targets), everyone)
        else:
            target = members.get(dst_vip)
            targets = [target] if target is not None and target is not client and target.confirmed else []
        for target in targets:
            if self.send_raw(message, target.addr):
                target.relayed_bytes += len(message)
                self.relayed_total += len(message)

    def remove(self, client, why):
        self.clients.pop(client.id, None)
        if self.by_addr.get(client.addr) is client:
            del self.by_addr[client.addr]
        members = self.rooms.get(client.key)
        if members is not None:
            members.pop(client.vip, None)
            if not members:
                del self.rooms[client.key]
                self.member_limits.pop(client.key, None)
                self.dirty_rooms.discard(client.key)
            else:
                self.send_members(client.key)
        logging.info("%s %s (%d online)", client.name, why, len(self.clients))

    # --- housekeeping ------------------------------------------------------------------------
    async def housekeeping(self):
        last_refresh = time.monotonic()
        while True:
            await asyncio.sleep(.5)
            now = time.monotonic()
            for client in [c for c in self.clients.values() if now - c.last_seen > (CLIENT_TIMEOUT if c.confirmed else 6.0)]:
                self.remove(client, "timed out")
            for ip in [ip for ip, b in self.hello_limits.items() if now - b.stamp > 60]:
                del self.hello_limits[ip]
            for room in list(self.dirty_rooms):
                self.send_members(room)
            if now - last_refresh > MEMBERS_REFRESH:
                last_refresh = now
                for room in list(self.rooms):
                    self.send_members(room)

    def public_players(self):
        for key, members in self.rooms.items():
            if key.rpartition("/")[2] == PUBLIC_ROOM:
                yield from members.values()

    def by_offset(self):
        counts = {}
        for m in self.public_players():
            key = "unknown" if m.utc_offset is None else "%+g" % (m.utc_offset / 60.0)
            entry = counts.setdefault(key, {"players": 0, "rtt_ms": []})
            entry["players"] += 1
            if m.rtt is not None:
                entry["rtt_ms"].append(m.rtt)
        return counts

    def status(self):
        public = sum(1 for _ in self.public_players())
        private_rooms = [m for key, m in self.rooms.items() if key.rpartition("/")[2] != PUBLIC_ROOM]
        return {
            "players_online": len(self.clients),
            "public_room_players": public,
            "private_rooms": len(private_rooms),
            "private_room_players": sum(len(m) for m in private_rooms),
            "relayed_bytes": self.relayed_total,
            "uptime_seconds": int(time.time() - self.started),
            # Public room players by UTC offset (hours) and round trip to this server (ms).
            "public_room_by_utc_offset": self.by_offset(),
        }


async def http_status(server, reader, writer):
    """Bounded loopback-only diagnostic service, never a public status endpoint."""
    accepted = False
    try:
        peer = writer.get_extra_info('peername')
        if not peer or not ipaddress.ip_address(peer[0]).is_loopback or server.http_connections >= 8 or not server.http_requests.take():
            return
        server.http_connections += 1
        accepted = True
        request = await asyncio.wait_for(reader.read(2048), timeout=5.0)
        if b'\r\n\r\n' not in request:
            return
        line = request.split(b"\r\n", 1)[0].decode("latin-1", "replace")
        parts = line.split(" ")
        if len(parts) != 3 or parts[0] != 'GET' or parts[1] not in ('/', '/status') or parts[2] not in ('HTTP/1.0', 'HTTP/1.1'):
            return
        headers = request.split(b'\r\n')[1:]
        hosts = [h.split(b':', 1)[1].strip().lower() for h in headers if h.lower().startswith(b'host:')]
        if any(h.lower().startswith(b'origin:') for h in headers) or len(hosts) > 1:
            return
        if hosts and hosts[0].split(b':')[0] not in (b'localhost', b'127.0.0.1'):
            return
        if parts[2] == 'HTTP/1.1' and not hosts:
            return
        if parts[1] == '/status':
            body = json.dumps(server.status(), indent=1).encode()
            kind = "application/json"
        else:
            s = server.status()
            body = ("Table Tennis online server\n%d players online, %d in the public room, "
                    "%d private rooms\n" % (s["players_online"], s["public_room_players"],
                                            s["private_rooms"])).encode()
            kind = "text/plain; charset=utf-8"
        writer.write(b"HTTP/1.0 200 OK\r\nContent-Type: " + kind.encode() +
                     b"\r\nX-Content-Type-Options: nosniff\r\nContent-Length: " +
                     str(len(body)).encode() + b"\r\nConnection: close\r\n\r\n" + body)
        await asyncio.wait_for(writer.drain(), timeout=2.0)
    except (asyncio.TimeoutError, ConnectionError, IndexError):
        pass
    finally:
        if accepted:
            server.http_connections -= 1
        writer.close()


async def main():
    parser = argparse.ArgumentParser(description="Table Tennis online server")
    parser.add_argument("--port", type=int, default=int(os.environ.get("TTLINK_PORT", "27360")),
                        help="UDP port for the game, and TCP port for /status (default 27360)")
    parser.add_argument("--bind", default=os.environ.get("TTLINK_BIND", "0.0.0.0"))
    parser.add_argument("--quiet", action="store_true", help="log only warnings")
    parser.add_argument('--room', help='Accept only this private invitation room')
    parser.add_argument('--max-clients', type=int, default=MAX_CLIENTS)
    parser.add_argument('--no-status', action='store_true', help='Disable the loopback TCP diagnostic listener')
    parser.add_argument('--parent-pid', type=int, help='Windows supervisor PID; exit if it exits')
    parser.add_argument('--parent-ticks', type=int, help='Exact .NET UTC creation ticks of the supervisor')
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error('Port must be 1-65535')
    if not 1 <= args.max_clients <= MAX_CLIENTS:
        parser.error('Client limit must be 1-%d' % MAX_CLIENTS)
    parent = None
    if args.parent_pid is not None or args.parent_ticks is not None:
        if os.name != 'nt' or not args.parent_pid or not args.parent_ticks:
            parser.error('Both Windows parent identity arguments are required')
        parent = ParentWatch(args.parent_pid, args.parent_ticks)
    logging.basicConfig(level=logging.WARNING if args.quiet else logging.INFO,
                        format="%(asctime)s %(message)s")

    loop = asyncio.get_running_loop()
    server = Server(args.room, args.max_clients)
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1 << 20)
    sock.bind((args.bind, args.port))
    await loop.create_datagram_endpoint(lambda: server, sock=sock)
    http = None
    tasks = []
    try:
        if not args.no_status:
            http = await asyncio.start_server(lambda r, w: http_status(server, r, w), '127.0.0.1', args.port, limit=2048, backlog=8)
        logging.info("listening on %s:%d (UDP game traffic; diagnostics local only)", args.bind, args.port)
        tasks.append(asyncio.create_task(server.housekeeping()))
        if parent is not None:
            tasks.append(asyncio.create_task(parent.wait()))
        done, _ = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
        for task in done:
            task.result()
    finally:
        for task in tasks:
            task.cancel()
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)
        if http is not None:
            http.close()
            await http.wait_closed()
        server.transport.close()
        if parent is not None:
            parent.close()


class ParentWatch:
    """Hold an OS process handle: PID reuse cannot keep an orphan relay alive."""
    def __init__(self, pid, ticks):
        import ctypes
        from ctypes import wintypes
        self.kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        self.kernel.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
        self.kernel.OpenProcess.restype = wintypes.HANDLE
        self.kernel.GetProcessTimes.argtypes = (wintypes.HANDLE, *([ctypes.POINTER(wintypes.FILETIME)] * 4))
        self.kernel.WaitForSingleObject.argtypes = (wintypes.HANDLE, wintypes.DWORD)
        self.kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
        self.handle = self.kernel.OpenProcess(0x100000 | 0x1000, False, pid)
        if not self.handle:
            raise OSError('The host supervisor is no longer available')
        created, exited, kernel, user = (wintypes.FILETIME() for _ in range(4))
        if not self.kernel.GetProcessTimes(self.handle, ctypes.byref(created), ctypes.byref(exited), ctypes.byref(kernel), ctypes.byref(user)) or (
                (created.dwHighDateTime << 32) | created.dwLowDateTime) + 504911232000000000 != ticks:
            self.close()
            raise OSError('Host supervisor identity changed')

    async def wait(self):
        while self.kernel.WaitForSingleObject(self.handle, 0) == 258:  # WAIT_TIMEOUT
            await asyncio.sleep(.5)

    def close(self):
        if self.handle:
            self.kernel.CloseHandle(self.handle)
            self.handle = None


if __name__ == "__main__":
    try:
        if os.name == "nt" and hasattr(asyncio, "Runner"):
            # On Windows the Proactor UDP reader in affected Python versions
            # stops rearming after ICMP port-unreachable (a peer just left).
            # Selector keeps listening after that recoverable datagram error.
            with asyncio.Runner(loop_factory=asyncio.SelectorEventLoop) as runner:
                runner.run(main())
        else:
            if os.name == "nt":  # Python 3.8-3.10 compatibility
                asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
            asyncio.run(main())
    except KeyboardInterrupt:
        pass
