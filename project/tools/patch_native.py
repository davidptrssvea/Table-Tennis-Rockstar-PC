"""Apply project-authored edits to code generated locally from a verified XEX.

The edit manifest contains hashes, positions and replacement code, not original
function bodies. Exact input hashes make SDK/codegen mismatches fail closed.
"""
import hashlib
import json
from pathlib import Path
import re

PATTERN = re.compile(r'DEFINE_REX_FUNC\((\w+)\) \{.*?(?=\nDEFINE_REX_FUNC|\Z)', re.S)


def digest(text):
    return hashlib.sha256(text.encode()).hexdigest()


def verify_function_table(text, required):
    registered = {(int(address, 16), name) for address, name in
                  re.findall(r'\{\s*0x([0-9A-Fa-f]+)\s*,\s*(\w+)\s*\}', text)}
    missing = sorted(address for address in required
                     if (address, f'sub_{address:08X}') not in registered)
    if missing:
        addresses = ', '.join(f'0x{address:08X}' for address in missing)
        raise ValueError('Missing registered override functions: ' + addresses +
                         '. Regenerate with current overrides.toml and --ignore-stamp.')


def apply_edits(text, rule):
    current = digest(text)
    if current == rule['output_sha256']:
        return text
    if current != rule['input_sha256']:
        raise ValueError(f"Generated code differs from supported SDK output: {rule['name']}")
    for edit in reversed(rule['edits']):
        start = edit['offset']
        text = text[:start] + edit['insert'] + text[start + edit['delete']:]
    if digest(text) != rule['output_sha256']:
        raise ValueError(f"Patch verification failed: {rule['name']}")
    return text


def patch(folder):
    folder = Path(folder)
    overrides = Path(__file__).resolve().parents[1] / 'overrides.toml'
    required = {int(address, 16) for address in
                re.findall(r'^0x([0-9A-Fa-f]+)\s*=\s*\{', overrides.read_text(), re.M)}
    if not required:
        raise ValueError('No expected override functions found')
    # Photo mode overrides existing discovered functions; verify their registrations too.
    required |= {0x82153598, 0x821709F8, 0x82187BE8, 0x821B5A68, 0x823F9318, 0x82160698}
    # Native resolution overrides (native_res.cpp).
    required |= {0x82228F30, 0x8214F248}
    verify_function_table((folder / 'tabletennis_init.cpp').read_text(encoding='utf-8'), required)
    print(f'Verified {len(required)} required override registrations.', flush=True)
    rules = {r['name']: r for r in json.loads(Path(__file__).with_name('native_edits.json').read_text())}
    pending, seen = {}, set()
    for path in folder.glob('*.cpp'):
        text = path.read_text(encoding='utf-8')
        def replace(match):
            name = match.group(1)
            if name not in rules:
                return match.group(0)
            if name in seen:
                raise ValueError(f'Duplicate generated function: {name}')
            seen.add(name)
            return apply_edits(match.group(0), rules[name])
        updated = PATTERN.sub(replace, text)
        for namespace, include in (('tabletennis::intro::', 'intro_skip.h'),
                                   ('tabletennis::pacing::', 'frame_pacer.h'),
                                   ('tabletennis::pc::', 'pc_features.h'),
                                   ('tabletennis::lan::', 'system_link.h'),
                                   ('tabletennis::native::', 'native_res.h')):
            if namespace in updated and f'#include "{include}"' not in updated:
                first_line, rest = updated.split('\n', 1)
                updated = first_line + f'\n#include "{include}"\n' + rest
        if updated != text:
            pending[path] = updated
    header = folder / 'tabletennis_pch.h'
    text = header.read_text(encoding='utf-8')
    updated = apply_edits(text, rules[header.name])
    seen.add(header.name)
    if updated != text:
        pending[header] = updated
    if seen != rules.keys():
        raise ValueError(f'Missing generated functions: {rules.keys() - seen}')
    for path, text in pending.items():
        path.write_text(text, encoding='utf-8', newline='\n')
    print(f'Verified all {len(rules)} native edits; updated {len(pending)} local files.', flush=True)
