"""Locally create the offline/startup menu override; no original archive bundled."""
import hashlib
from pathlib import Path
import struct
import zlib

ORIGINAL_ARCHIVE = '2dd3b2e2141caf554a63b0791271ab256b9597238f3685ca9569776fb80e28bc'
ORIGINAL_MOVIE = '75c3a8250db52a44461b5fea383c148ce938229231257f5cd7f8a985848dfa17'
PATCHED_MOVIE = 'a37a6269f5b4c80d452a0bbdd91e7d63ec888ce6169dfde223a18a81649f33e5'


def patch(path):
    path = Path(path)
    archive = bytearray(path.read_bytes())
    if archive[:4] != b'RPF0':
        raise ValueError('Not an RPF0 menu archive')
    toc, count = struct.unpack_from('<II', archive, 4)
    names = toc + count * 16
    if not 0 < count < 100000 or toc < 12 or names >= len(archive):
        raise ValueError('Invalid archive table')
    entries = []
    for i in range(count):
        entry = toc + i * 16
        name, offset, packed, raw_size = struct.unpack_from('<4I', archive, entry)
        if name & 0x80000000:
            continue
        end = archive.index(0, names + name)
        if offset + packed > len(archive):
            raise ValueError('Archive entry outside file')
        entries.append((archive[names + name:end], entry, offset, packed, raw_size))
    matches = [e for e in entries if e[0] == b'pongui_001.xeck']
    if len(matches) != 1:
        raise ValueError('Expected exactly one frontend movie')
    _, entry, offset, packed, raw_size = matches[0]
    if raw_size > 16 * 1024 * 1024:
        raise ValueError('Unexpected frontend size')
    decoder = zlib.decompressobj(-15)
    movie = bytearray(decoder.decompress(archive[offset:offset + packed], raw_size + 1))
    if len(movie) != raw_size or not decoder.eof:
        raise ValueError('Invalid frontend compression')
    current = hashlib.sha256(movie).hexdigest()
    if current == PATCHED_MOVIE:
        print('Menu patches already verified.', flush=True)
        return
    if hashlib.sha256(archive).hexdigest() != ORIGINAL_ARCHIVE or current != ORIGINAL_MOVIE:
        raise ValueError('Unsupported menu archive; no changes made')
    # AVM1 builds the list in reverse stack order. Move Options/Extras into
    # the former online slot and append an authored Quit label/state instead.
    extras = bytes(movie[0x17EBB:0x17EE5])
    options = bytes(movie[0x17EE5:0x17F0D])
    # The first Push also contains LIST_MAIN; preserve that receiver key.
    extras = extras[:1] + struct.pack('<H', struct.unpack_from('<H', extras, 1)[0] - 2) + extras[5:]
    payload = (b'\x08\xd2\x07' + struct.pack('<i', -1) + b'\x07' + struct.pack('<i', 91)
               + b'\x00Quit to desktop\x00\x07' + struct.pack('<i', 3) + b'\x08\xa2')
    quit_item = b'\x96' + struct.pack('<H', len(payload)) + payload + b'\x40\x4d\x4d'
    if len(quit_item) != 42 or len(extras) != 40:
        raise ValueError('Invalid authored menu action layout')
    movie[0x17EBB:0x17EE5] = quit_item
    movie[0x17EE5:0x17F0D] = extras
    movie[0x17F0D:0x17F35] = options
    start, end = 0x3AC212, 0x3AC23D
    movie[start:end] = b'\x99\x02\x00' + struct.pack('<h', end - start - 5) + bytes(end - start - 5)
    for address, value in ((0x3AC24D, 3), (0x3AC278, 4)):
        movie[address] = value
    # Zero only the fixed startup frame thresholds; retain initialization gates.
    for address in (0x3AABB7, 0x3AB044, 0x3AAC74):
        movie[address + 4:address + 8] = bytes(4)
    if hashlib.sha256(movie).hexdigest() != PATCHED_MOVIE:
        raise ValueError('Patched frontend verification failed')
    compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
    replacement = compressor.compress(movie) + compressor.flush()
    limit = min((e[2] for e in entries if e[2] > offset), default=len(archive))
    if offset + len(replacement) > limit:
        raise ValueError('Patched movie exceeds archive slot')
    archive[offset:limit] = replacement + bytes(limit - offset - len(replacement))
    struct.pack_into('<I', archive, entry + 8, len(replacement))
    temp = path.with_suffix('.tmp')
    temp.write_bytes(archive)
    temp.replace(path)
    print('Created and verified offline menu and intro timing patches locally.', flush=True)
