"""Authored AVM1 menu changes adapted from ThisIsAkill/Table-Tennis-Linux (MIT)."""
import struct


def integer(value):
    return b'\x07' + struct.pack('<i', value)


def pool(value):
    return b'\x08' + bytes([value])


def string(value):
    return b'\x00' + value.encode('ascii') + b'\0'


def push(*values):
    data = b''.join(values)
    return b'\x96' + struct.pack('<H', len(data)) + data


def fit(data, size):
    remaining = size - len(data)
    if remaining < 5:
        raise ValueError('Online menu code exceeds verified action slot')
    return data + b'\x99\x02\x00' + struct.pack('<h', remaining - 5) + bytes(remaining - 5)


def enable_online(movie):
    # The game builds arrays by popping arguments, so items are pushed in reverse.
    # Keep the original Exhibition item (its difficulty submenu) byte-for-byte.
    exhibition = bytes(movie[0x17F5D:0x17FB9])
    def item(state, text=''):
        return push(integer(-1), integer(state), string(text), integer(3), pool(162)) + b'\x40'
    code = push(pool(210), integer(-1), integer(91), string('QUIT TO DESKTOP'), integer(3), pool(162)) + b'\x40'
    code += item(85) + item(18) + item(91, 'SYSTEM LINK') + item(91) + item(83)
    code += exhibition + item(5) + push(integer(8), pool(162)) + b'\x40'
    movie[0x17EBB:0x17FEC] = fit(code, 0x17FEC - 0x17EBB)
    # Localisation still supplies the original labels, at the new row indices.
    label = push(pool(0)) + b'\x1c' + push(pool(2)) + b'\x4e' + push(integer(3)) + b'\x4e'
    label += push(integer(0), string('ONLINE')) + b'\x4f'
    movie[0x3AC212:0x3AC23D] = fit(label, 0x3AC23D - 0x3AC212)
    movie[0x3AC24D], movie[0x3AC278] = 5, 6
    # Tell the native frontend which of the three state-91 rows was selected.
    # It handles ONLINE (3), SYSTEM LINK (4), and QUIT TO DESKTOP (7) separately.
    reg = b'\x04\x01'
    request = push(reg, pool(218), integer(1)) + b'\x4f'
    row = push(reg, pool(28), reg, pool(30)) + b'\x4e' + push(reg, pool(31)) + b'\x4e\x4e\x18\x4f'
    test = push(reg, pool(21)) + b'\x4e' + push(integer(91)) + b'\x49\x12'
    code = test + b'\x9d\x02\x00' + struct.pack('<h', len(request) + len(row)) + request + row
    start, end = 0x13F9C, 0x13FE2
    if movie[start:start + 7] != push(reg, pool(14)):
        raise ValueError('Unsupported frontend accept handler')
    movie[start:end] = fit(code, end - start)
