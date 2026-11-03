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
MAX_CLIENTS = 2000
MAX_PER_IP = 8
MAX_PUBLIC_ROOM = 250
MAX_PRIVATE_ROOM = 16
# Per client: sustained packets and bytes per second, with a burst allowance of two seconds.
PACKET_RATE = 400
BYTE_RATE = 256 * 1024
BROADCAST_RATE = 20
HELLO_RATE_PER_IP = 5
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
                 "utc_offset", "streak_start", "last_broadcast", "_reach", "key")

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
    def __init__(self):
        self.transport = None
        self.clients = {}       # id -> Client
        self.by_addr = {}       # (ip, port) -> Client
        self.rooms = {}         # code -> {vip: Client}
        self.hello_limits = {}  # ip -> Bucket
        self.relayed_total = 0
        self.started = time.time()

    # --- transport ---------------------------------------------------------------------------
    def connection_made(self, transport):
        self.transport = transport

    def send(self, addr, kind, body=b""):
        self.transport.sendto(MAGIC + bytes([kind]) + body, addr)

    def error(self, addr, code, text):
        data = text.encode()[:200]
        self.send(addr, ERROR, struct.pack(">BB", code, len(data)) + data)

    def datagram_received(self, data, addr):
        if len(data) < 5 or data[:4] != MAGIC or len(data) > MAX_PAYLOAD + 64:
            return
        kind = data[4]
        body = data[5:]
        try:
            if kind == HELLO:
                self.on_hello(body, addr)
            elif kind in (KEEPALIVE, DATA, BYE):
                client = self.authenticate(body, addr)
                if client is None:
                    if kind != BYE:
                        self.error(addr, ERR_UNKNOWN, "unknown client")
                    return
                client.last_seen = time.monotonic()
                if not client.packets.take() or not client.bytes.take(len(data)):
                    return
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
        bucket = self.hello_limits.get(addr[0])
        if bucket is None:
            bucket = self.hello_limits[addr[0]] = Bucket(HELLO_RATE_PER_IP)
        if not bucket.take():
            return
        version, nonce = struct.unpack_from(">BQ", body, 0)
        pos = 9
        name_len = body[pos]
        name = body[pos + 1:pos + 1 + name_len].decode("utf-8", "replace")
        pos += 1 + name_len
        room_len = body[pos]
        room = body[pos + 1:pos + 1 + room_len].decode("ascii", "replace").upper()
        pos += 1 + room_len
        local_ip, local_port = struct.unpack_from(">IH", body, pos)
        pos += 6
        utc_offset = struct.unpack_from(">h", body, pos)[0] if len(body) >= pos + 2 else None
        if version not in SUPPORTED_VERSIONS:
            self.error(addr, ERR_VERSION, "this server speaks a different version; update the game")
            return
        name = "".join(c for c in name if c.isprintable())[:15] or "Player"
        if room and (len(room) != ROOM_LENGTH or any(c not in ROOM_ALPHABET for c in room)):
            self.error(addr, ERR_ROOM, "invalid room code")
            return
        existing = self.by_addr.get(addr)
        if existing is not None:
            if existing.nonce == nonce:  # a resent HELLO: answer it again
                existing.last_seen = time.monotonic()
                self.welcome(existing)
                return
            self.remove(existing, "reconnected")
        if len(self.clients) >= MAX_CLIENTS:
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
        cid = random.getrandbits(32) or 1
        while cid in self.clients:
            cid = random.getrandbits(32) or 1
        client = Client(cid, random.getrandbits(64), nonce, addr, name, room, vip, local_ip, local_port)
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
        self.send_members(key)

    def welcome(self, client):
        room = client.room.encode()
        body = struct.pack(">IIQB", client.id, client.vip, client.token, len(room)) + room
        body += struct.pack(">IH", ip_to_int(client.addr[0]), client.addr[1])
        self.send(client.addr, WELCOME, body)

    def send_members(self, room):
        members = list(self.rooms.get(room, {}).values())
        for target in members:
            entries = []
            for m in members:
                if m is target:
                    continue
                name = m.name.encode()[:15]
                entries.append(struct.pack(">IIQIHIHB", m.id, m.vip, m.token, ip_to_int(m.addr[0]),
                                           m.addr[1], m.local_ip, m.local_port, len(name)) + name)
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
        if len(payload) > MAX_PAYLOAD:
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
            targets = [m for m in members.values() if m is not client]
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
            targets = [target] if target is not None and target is not client else []
        for target in targets:
            self.transport.sendto(message, target.addr)
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
            else:
                self.send_members(client.key)
        logging.info("%s %s (%d online)", client.name, why, len(self.clients))

    # --- housekeeping ------------------------------------------------------------------------
    async def housekeeping(self):
        last_refresh = time.monotonic()
        while True:
            await asyncio.sleep(2.0)
            now = time.monotonic()
            for client in [c for c in self.clients.values() if now - c.last_seen > CLIENT_TIMEOUT]:
                self.remove(client, "timed out")
            for ip in [ip for ip, b in self.hello_limits.items() if now - b.stamp > 60]:
                del self.hello_limits[ip]
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
    """Minimal HTTP: GET /status returns JSON; anything else a short text page."""
    try:
        request = await asyncio.wait_for(reader.read(2048), timeout=5.0)
        line = request.split(b"\r\n", 1)[0].decode("latin-1", "replace")
        path = line.split(" ")[1] if line.count(" ") >= 2 else "/"
        if path.startswith("/status"):
            body = json.dumps(server.status(), indent=1).encode()
            kind = "application/json"
        else:
            s = server.status()
            body = ("Table Tennis online server\n%d players online, %d in the public room, "
                    "%d private rooms\n" % (s["players_online"], s["public_room_players"],
                                            s["private_rooms"])).encode()
            kind = "text/plain; charset=utf-8"
        writer.write(b"HTTP/1.0 200 OK\r\nContent-Type: " + kind.encode() +
                     b"\r\nAccess-Control-Allow-Origin: *\r\nContent-Length: " +
                     str(len(body)).encode() + b"\r\nConnection: close\r\n\r\n" + body)
        await writer.drain()
    except (asyncio.TimeoutError, ConnectionError, IndexError):
        pass
    finally:
        writer.close()


async def main():
    parser = argparse.ArgumentParser(description="Table Tennis online server")
    parser.add_argument("--port", type=int, default=int(os.environ.get("TTLINK_PORT", "27360")),
                        help="UDP port for the game, and TCP port for /status (default 27360)")
    parser.add_argument("--bind", default=os.environ.get("TTLINK_BIND", "0.0.0.0"))
    parser.add_argument("--quiet", action="store_true", help="log only warnings")
    args = parser.parse_args()
    logging.basicConfig(level=logging.WARNING if args.quiet else logging.INFO,
                        format="%(asctime)s %(message)s")

    loop = asyncio.get_running_loop()
    server = Server()
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 1 << 20)
    sock.bind((args.bind, args.port))
    await loop.create_datagram_endpoint(lambda: server, sock=sock)
    http = await asyncio.start_server(lambda r, w: http_status(server, r, w), args.bind, args.port)
    logging.info("listening on %s:%d (UDP game traffic, TCP /status)", args.bind, args.port)
    async with http:
        await server.housekeeping()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
