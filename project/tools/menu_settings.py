"""OPTIONS edits adapted from ThisIsAkill/Table-Tennis-Linux (MIT)."""
import struct

# Small AVM1 assembler for the PC SETTINGS entry. Constant pool indices refer
# to the pools already present in the movie's action blocks.
def _int(value):
    return b'\x07' + struct.pack('<i', value)


def _pool(index):
    return (b'\x08' + bytes([index])) if index < 256 else (b'\x09' + struct.pack('<H', index))


def _reg(index):
    return b'\x04' + bytes([index])


def _str(text):
    return b'\x00' + text.encode() + b'\0'


def _push(*items):
    body = b''.join(items)
    return b'\x96' + struct.pack('<H', len(body)) + body


def _branch(opcode, offset):
    return bytes([opcode]) + b'\x02\x00' + struct.pack('<h', offset)


GET_VARIABLE, GET_MEMBER, SET_MEMBER, NEW_OBJECT, STRICT_EQUALS = b'\x1c', b'\x4e', b'\x4f', b'\x40', b'\x66'
GREATER, NOT, EQUALS2, POP = b'\x67', b'\x12', b'\x49', b'\x17'


def _store_register(index):
    return b'\x87\x01\x00' + bytes([index])
JUMP, IF = 0x99, 0x9D


def _fit(code, size):
    """Pad a rewritten region to its original size with a Jump over zeros. A gap too small for
    the Jump (2 or 4 bytes) is filled with PushDuplicate + Pop pairs, which leave the stack as
    it was."""
    gap = size - len(code)
    if gap in (2, 4):
        return code + b'\x4c\x17' * (gap // 2)
    if gap < 0 or 0 < gap < 5:
        raise ValueError('PC settings patch does not fit')
    return code + (_branch(JUMP, gap - 5) + bytes(gap - 5) if gap else b'')


def _options_list(size):
    """_global.LIST_OPTIONS with ["PC SETTINGS", FE_GAME_OPTIONS, -1] added as the third item,
    after AUDIO OPTIONS.

    Each item is [text, target frame state, -1]; Array pops its arguments, so
    the last item is pushed first. The frame state constants (FE_GAME_OPTIONS
    21, FE_CONTROLLER 79, FE_AUDIO_OPTIONS 24) are inlined to make room. The
    PC SETTINGS state is never used: the accept handler raises a flag for it.
    """
    # In menu order; None marks PC SETTINGS. The texts are the movie's own t_* constants.
    items = [(21, 286), (24, 285), None, (79, 284), (21, 283), (21, 282), (21, 281), (21, 280)]
    code = _push(_pool(279))
    for item in reversed(items):
        if item is None:
            code += _push(_int(-1), _int(21), _str('PC SETTINGS'), _int(3), _pool(162)) + NEW_OBJECT
            continue
        state, text = item
        code += (_push(_int(-1), _int(state), _pool(1)) + GET_VARIABLE + _push(_pool(text)) + GET_MEMBER +
                 _push(_int(3), _pool(162)) + NEW_OBJECT)
    code += _push(_int(8), _pool(162)) + NEW_OBJECT
    return _fit(code, size)


def _options_accept(start, end, done):
    """OPTIONS accept handler: item 2 (PC SETTINGS) raises _global.GLOBAL_PC_SETTINGS,
    which the executable polls to open its page; items 4-7 raise the save/load flags.
    Every other item falls through to the game's own handling (its target frame state)."""
    flags = [_pool(214), _pool(215), _pool(216), _pool(217), _str('GLOBAL_PC_SETTINGS')]
    bodies = [_push(_reg(1), flag, _int(1)) + SET_MEMBER for flag in flags]
    sound = (_push(_reg(1), _pool(81), _reg(1), _pool(213)) + GET_MEMBER + SET_MEMBER +
             _push(_reg(1), _pool(83), _int(1)) + SET_MEMBER)
    # The first test compares the item index StoreRegister left on the stack.
    tests = [_push(_int(2)) + STRICT_EQUALS] + [_push(_reg(0), _int(i)) + STRICT_EQUALS for i in (4, 5, 6, 7)]
    # Test i jumps to body test_body[i]: index 2 -> the PC flag (body 4), 4-7 -> bodies 0-3.
    test_body = [4, 0, 1, 2, 3]
    position = start + sum(len(test) + 5 for test in tests) + 5
    targets = []
    for body in bodies[:4]:
        targets.append(position)
        position += len(body) + 5
    targets.append(position)
    sound_at = position + len(bodies[4])
    code = b''
    for test, body in zip(tests, test_body):
        target = targets[body]
        code += test
        code += _branch(IF, target - (start + len(code) + 5))
    code += _branch(JUMP, end - (start + len(code) + 5))
    for body in bodies[:4]:
        code += body
        code += _branch(JUMP, sound_at - (start + len(code) + 5))
    code += bodies[4] + sound
    code += _branch(JUMP, done - (start + len(code) + 5))
    return _fit(code, end - start)


def _add_pc_settings(movie):
    movie[0x1878F:0x188BC] = _options_list(0x188BC - 0x1878F)
    movie[0x13C0D:0x13D00] = _options_accept(0x13C0D, 0x13D00, 0x13E3C)
    # The localisation script copies the translated labels into LIST_OPTIONS by row
    # (LIST_OPTIONS[i][0] = t_...); move CONTROLS through LOAD SHARED SETTINGS down one row
    # so PC SETTINGS keeps its own label in row 2.
    for address, row in ((0x3AC880, 2), (0x3AC8AB, 3), (0x3AC8D6, 4), (0x3AC901, 5), (0x3AC92C, 6)):
        if movie[address:address + 4] != b'\x96\x05\x00\x07' or movie[address + 4] not in (row, row + 1):
            raise ValueError('Unexpected OPTIONS label code')
        movie[address + 4] = row + 1


def _pause_options_list(size):
    """_global.LIST_iPAUSE_OPTIONS with ["PC SETTINGS", iPAUSE_OPTIONS, -1] as the third item,
    after AUDIO OPTIONS.

    Items are [text, target state, -1] (pause states: CONTROLLER 3, AUDIO 2, GAME 7, HELPTIPS 22,
    OPTIONS 1), pushed last item first. Targeting OPTIONS itself makes the accept handler treat
    the item as "same state", where it raises the PC flag. GAME OPTIONS and HELP TIPS read their
    labels with GetVariable (resolved through _global, as the movie's own functions do) to make
    room; the label script rewrites every row's text by index anyway.
    """
    items = [(3, 22, False), (2, 20, False), None, (7, 18, True), (22, 16, True)]
    code = b''
    head = [_pool(14)]
    for item in reversed(items):
        if item is None:
            code += _push(*head, _int(-1), _int(1), _str('PC SETTINGS'), _int(3), _pool(4)) + NEW_OBJECT
        else:
            state, text, short = item
            if short:
                code += _push(*head, _int(-1), _int(state), _pool(text)) + GET_VARIABLE
            else:
                code += (_push(*head, _int(-1), _int(state), _pool(0)) + GET_VARIABLE + _push(_pool(text)) +
                         GET_MEMBER)
            code += _push(_int(3), _pool(4)) + NEW_OBJECT
        head = []
    code += _push(_int(5), _pool(4)) + NEW_OBJECT
    return _fit(code, size)


def _pause_accept(start, end, done):
    """Pause menu A handler for the highlighted item (r0 = item, r1 = _global):
    if item[2] > -2: if item[1] != STATE_CURRENT, go to item[1] (the game's behaviour);
    otherwise raise GLOBAL_PC_SETTINGS in the OPTIONS list, or play ERROR anywhere else."""
    item = (_push(_pool(52)) + GET_VARIABLE + _push(_reg(1), _pool(20)) + GET_MEMBER + _push(_reg(1), _pool(53)) +
            GET_MEMBER + GET_MEMBER + GET_MEMBER + _store_register(0) + POP)
    disabled = _push(_reg(0), _int(2)) + GET_MEMBER + _push(_int(-2)) + GREATER + NOT
    same_state = _push(_reg(1), _pool(2)) + GET_MEMBER + _push(_reg(0), _int(1)) + GET_MEMBER + EQUALS2
    go = (_push(_reg(1), _pool(10), _reg(0), _int(1)) + GET_MEMBER + SET_MEMBER +
          _push(_reg(1), _pool(23), _reg(1), _pool(24)) + GET_MEMBER + SET_MEMBER)
    not_options = _push(_reg(1), _pool(2)) + GET_MEMBER + _push(_reg(1), _pool(3)) + GET_MEMBER + EQUALS2 + NOT
    flag = _push(_reg(1), _str('GLOBAL_PC_SETTINGS'), _int(1)) + SET_MEMBER
    camera = _push(_reg(1), _pool(71), _reg(1), _pool(72)) + GET_MEMBER + SET_MEMBER
    error = _push(_reg(1), _pool(71), _reg(1), _pool(74)) + GET_MEMBER + SET_MEMBER
    sound = _push(_reg(1), _pool(73), _int(1)) + SET_MEMBER
    # Branches are (opcode, label); strings starting with ':' define labels.
    parts = [item, disabled, (IF, 'error'), same_state, (IF, 'same'), go, (JUMP, 'camera'),
             ':same', not_options, (IF, 'error'), flag,
             ':camera', camera, (JUMP, 'sound'), ':error', error, ':sound', sound, (JUMP, 'done')]
    labels = {'done': done}
    position = start
    for part in parts:
        if isinstance(part, str):
            labels[part[1:]] = position
        else:
            position += 5 if isinstance(part, tuple) else len(part)
    code = b''
    for part in parts:
        if isinstance(part, tuple):
            code += _branch(part[0], labels[part[1]] - (start + len(code) + 5))
        elif not isinstance(part, str):
            code += part
    return _fit(code, end - start)


def _add_pause_pc_settings(movie):
    movie[0x7A83:0x7B30] = _pause_options_list(0x7B30 - 0x7A83)
    movie[0x4C96:0x4D87] = _pause_accept(0x4C96, 0x4D87, 0x531B)
    # The label script copies the translated labels into LIST_iPAUSE_OPTIONS by row; move
    # GAME OPTIONS and HELP TIPS down one row so PC SETTINGS keeps its own label in row 2.
    for address, row in ((0x9096, 2), (0x90C1, 3)):
        if movie[address:address + 4] != b'\x96\x05\x00\x07' or movie[address + 4] != row:
            raise ValueError('Unexpected pause OPTIONS label code')
        movie[address + 4] = row + 1


