"""Locally create the offline/startup menu override; no original archive bundled."""
import hashlib
from pathlib import Path
import struct
import zlib

ORIGINAL_ARCHIVE = '2dd3b2e2141caf554a63b0791271ab256b9597238f3685ca9569776fb80e28bc'
ORIGINAL_MOVIE = '75c3a8250db52a44461b5fea383c148ce938229231257f5cd7f8a985848dfa17'
PATCHED_MOVIE = 'cd6ad6d72bd9e4642cb529478ae7476e0e3215aba8a443250409e837a776953a'


ONLINE_MOVIE = 'b8600c82885cf512ae81ca9e8db7538138ea71f34d5a6575542254cb910311a2'


def _patch_base(path, online=False):
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
    expected = ONLINE_MOVIE if online else PATCHED_MOVIE
    if current == expected:
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
               + b'\x00QUIT TO DESKTOP\x00\x07' + struct.pack('<i', 3) + b'\x08\xa2')
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
    if online:
        from menu_online import enable_online
        enable_online(movie)
    if hashlib.sha256(movie).hexdigest() != expected:
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


ORIGINAL_PAUSE_MOVIE = '7b6f9e70c58adcea5974681aad4dd2fa7915dd6f54015efdaaf888818ba0861d'
SETTINGS_PAUSE_MOVIE = 'ca15a4b5931015c5547ee019ee59febb82bf62b7a547d7c0353f104d3db7d3d4'
SETTINGS_OFFLINE_MOVIE = '4595b6d6664e92a53586525e246c610314c85acf54c4fb686cacce2436151fc6'
SETTINGS_ONLINE_MOVIE = '4b761e4703365822f8735111852925147f5ea4c810627dfb8e64a4bf9796e42e'


def _movies(archive):
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
        entries.append((archive[names+name:end], entry, offset, packed, raw_size))
    result = {}
    for name in (b'pongui_001.xeck', b'pongpause_001.xeck'):
        matches = [e for e in entries if e[0] == name]
        if len(matches) != 1:
            raise ValueError('Expected one frontend/pause menu')
        _, entry, offset, packed, size = matches[0]
        if size > 16 * 1024 * 1024:
            raise ValueError('Unexpected menu size')
        decoder = zlib.decompressobj(-15)
        movie = bytearray(decoder.decompress(archive[offset:offset+packed], size+1))
        if len(movie) != size or not decoder.eof:
            raise ValueError('Invalid menu compression')
        limit = min((e[2] for e in entries if e[2] > offset), default=len(archive))
        result[name] = (movie, entry, offset, limit)
    return result


def patch(path, online=False):
    from menu_settings import _add_pc_settings, _add_pause_pc_settings
    path = Path(path)
    archive = bytearray(path.read_bytes())
    movies = _movies(archive)
    frontend = movies[b'pongui_001.xeck'][0]
    pause = movies[b'pongpause_001.xeck'][0]
    expected = SETTINGS_ONLINE_MOVIE if online else SETTINGS_OFFLINE_MOVIE
    front_hash, pause_hash = (hashlib.sha256(m).hexdigest() for m in (frontend,pause))
    if front_hash == expected and pause_hash == SETTINGS_PAUSE_MOVIE:
        print('Frontend and pause PC SETTINGS patches already verified.', flush=True)
        return
    base_expected = ONLINE_MOVIE if online else PATCHED_MOVIE
    if front_hash not in (ORIGINAL_MOVIE, base_expected) or pause_hash != ORIGINAL_PAUSE_MOVIE:
        raise ValueError('Unsupported frontend/pause archive; no changes made')
    temporary = path.with_suffix('.pc-working')
    try:
        temporary.write_bytes(archive)
        _patch_base(temporary, online)
        archive = bytearray(temporary.read_bytes())
        movies = _movies(archive)
        for name, apply, digest in ((b'pongui_001.xeck',_add_pc_settings,expected),
                                   (b'pongpause_001.xeck',_add_pause_pc_settings,SETTINGS_PAUSE_MOVIE)):
            movie, entry, offset, limit = movies[name]
            apply(movie)
            if hashlib.sha256(movie).hexdigest() != digest:
                raise ValueError('PC SETTINGS movie verification failed')
            compressor = zlib.compressobj(9,zlib.DEFLATED,-15)
            packed = compressor.compress(movie)+compressor.flush()
            if offset+len(packed)>limit:
                raise ValueError('PC SETTINGS movie exceeds archive slot')
            archive[offset:limit] = packed+bytes(limit-offset-len(packed))
            struct.pack_into('<I',archive,entry+8,len(packed))
        temporary.write_bytes(archive)
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)
    print('Created verified PC SETTINGS entries in frontend and pause OPTIONS.',flush=True)
