#!/usr/bin/env python3
"""🔒 Diva v2.0 password lock: the action detection, the controls and the camera / player lock cannot change
without Eva's password.

`Tools/lock/lock.json` lists every locked file with its SHA-256 and is signed (Ed25519) with a key derived from the
password (scrypt). The repository holds only the public key, never the password, so nobody can sign a change without
it. The signature and the files are checked by the Python tests (test_password_lock.py), the git pre-commit hook
(.githooks/pre-commit), GitHub Actions (.github/workflows/lock.yml) and Unity (DivaLockCheck.cs: an error on load,
and release builds refuse to start).

    python3 Tools/lock/diva_lock.py verify            # are the locked files exactly as signed? (default)
    python3 Tools/lock/diva_lock.py status            # list the locked files and which ones changed
    python3 Tools/lock/diva_lock.py sign              # Eva, after an approved change: password, re-sign
    python3 Tools/lock/diva_lock.py sign --add PATH   # lock one more file (--remove PATH to release one)
    python3 Tools/lock/diva_lock.py change-password   # old password, then the new one
    python3 Tools/lock/diva_lock.py init              # first time only (refuses once a key is pinned)
    (add --show to see the password while typing; clear the window afterwards with Cmd+K)

Only the standard library is used (Ed25519 from RFC 8032), so the check runs anywhere Python 3 runs.
Line endings are normalised (CRLF -> LF) before hashing, so a Windows checkout verifies too.
"""
import argparse
import getpass
import hashlib
import json
import os
import re
import secrets
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'Tools' / 'lock' / 'lock.json'
TOOL = Path(__file__).resolve()
# The public key of Eva's password. `init` writes it here once; changing it needs the old password
# (change-password). The CI workflow pins the same key.
PINNED_PUBLIC_KEY = 'd80806f1a3020e287c432db6d4a754797cf0c41d23b004b888a482749de49cec'

# The files locked in v2.0: action detection, how actions drive the elephant, the camera and the player lock,
# the tests and validators that guard them, and this lock itself.
DEFAULT_FILES = [
    # Action detection (MediaPipe bridge, gesture rules, settings)
    'TrackingOverrides/bridge.py',
    'TrackingOverrides/diva_gestures.py',
    'TrackingOverrides/diva_gestures.json',
    # How the actions drive the elephant
    'Assets/DigiPhant/Runtime/DivaControlState.cs',
    'Assets/DigiPhant/Runtime/DivaDemo.cs',
    'Assets/DigiPhant/Runtime/DigiPhantController.cs',
    'Assets/DigiPhant/Runtime/DigiPhantLocomotion.cs',
    # Camera and the player lock (camera choice, tracking start / restart, camera check that saves the roles)
    'Assets/DigiPhant/Runtime/DigiPhantCameraPreview.cs',
    'Assets/DigiPhant/Runtime/DigiPhantMacCameras.cs',
    'Assets/DivaShow/Runtime/DivaShowCameraCheck.cs',
    # Tests and validators that guard the above
    'TrackingOverrides/test_gesture_lock.py',
    'TrackingOverrides/test_player_parts.py',
    'TrackingOverrides/test_hands_up.py',
    'TrackingOverrides/test_stop_on_time.py',
    'TrackingOverrides/test_easier_walk.py',
    'TrackingOverrides/test_aim_no_start.py',
    'TrackingOverrides/test_relock.py',
    'TrackingOverrides/test_role_spots.py',
    'TrackingOverrides/test_spot_anchor.py',
    'TrackingOverrides/test_diva_gestures.py',
    'Assets/DigiPhant/Editor/DivaSetup.cs',
    'Assets/DigiPhant/Editor/DivaStationaryTurnValidation.cs',
    # The lock itself
    'Tools/lock/diva_lock.py',
    'TrackingOverrides/test_password_lock.py',
    'Assets/DivaShow/Editor/DivaLockCheck.cs',
    '.githooks/pre-commit',
    '.github/workflows/lock.yml',
    '.github/CODEOWNERS',
]

# ---- Ed25519 (RFC 8032, section 6) --------------------------------------------------------------------------------
_p = 2 ** 255 - 19
_q = 2 ** 252 + 27742317777372353535851937790883648493
_d = -121665 * pow(121666, _p - 2, _p) % _p
_sqrt_m1 = pow(2, (_p - 1) // 4, _p)


def _sha512(data):
    return hashlib.sha512(data).digest()


def _add(P, Q):
    A, B = (P[1] - P[0]) * (Q[1] - Q[0]) % _p, (P[1] + P[0]) * (Q[1] + Q[0]) % _p
    C, D = 2 * P[3] * Q[3] * _d % _p, 2 * P[2] * Q[2] % _p
    E, F, G, H = B - A, D - C, D + C, B + A
    return (E * F, G * H, F * G, E * H)


def _mul(s, P):
    Q = (0, 1, 1, 0)
    while s > 0:
        if s & 1:
            Q = _add(Q, P)
        P = _add(P, P)
        s >>= 1
    return Q


def _equal(P, Q):
    return (P[0] * Q[2] - Q[0] * P[2]) % _p == 0 and (P[1] * Q[2] - Q[1] * P[2]) % _p == 0


def _recover_x(y, sign):
    if y >= _p:
        return None
    x2 = (y * y - 1) * pow(_d * y * y + 1, _p - 2, _p)
    if x2 == 0:
        return None if sign else 0
    x = pow(x2, (_p + 3) // 8, _p)
    if (x * x - x2) % _p != 0:
        x = x * _sqrt_m1 % _p
    if (x * x - x2) % _p != 0:
        return None
    if (x & 1) != sign:
        x = _p - x
    return x


_gy = 4 * pow(5, _p - 2, _p) % _p
_gx = _recover_x(_gy, 0)
_G = (_gx, _gy, 1, _gx * _gy % _p)


def _compress(P):
    zinv = pow(P[2], _p - 2, _p)
    x, y = P[0] * zinv % _p, P[1] * zinv % _p
    return int.to_bytes(y | ((x & 1) << 255), 32, 'little')


def _decompress(s):
    if len(s) != 32:
        return None
    y = int.from_bytes(s, 'little')
    sign = y >> 255
    y &= (1 << 255) - 1
    x = _recover_x(y, sign)
    return None if x is None else (x, y, 1, x * y % _p)


def _expand(secret):
    h = _sha512(secret)
    a = int.from_bytes(h[:32], 'little')
    a &= (1 << 254) - 8
    a |= 1 << 254
    return a, h[32:]


def public_key(secret):
    return _compress(_mul(_expand(secret)[0], _G))


def sign(secret, message):
    a, prefix = _expand(secret)
    A = _compress(_mul(a, _G))
    r = int.from_bytes(_sha512(prefix + message), 'little') % _q
    R = _compress(_mul(r, _G))
    h = int.from_bytes(_sha512(R + A + message), 'little') % _q
    return R + int.to_bytes((r + h * a) % _q, 32, 'little')


def verify_signature(public, message, signature):
    if len(public) != 32 or len(signature) != 64:
        return False
    A, R = _decompress(public), _decompress(signature[:32])
    if A is None or R is None:
        return False
    s = int.from_bytes(signature[32:], 'little')
    if s >= _q:
        return False
    h = int.from_bytes(_sha512(signature[:32] + public + message), 'little') % _q
    return _equal(_mul(s, _G), _add(R, _mul(h, A)))


# ---- The lock -----------------------------------------------------------------------------------------------------
def secret_from_password(password, salt_hex):
    return hashlib.scrypt(password.encode('utf-8'), salt=b'diva-lock:' + bytes.fromhex(salt_hex),
                          n=2 ** 14, r=8, p=1, dklen=32)


def file_hash(data):
    return hashlib.sha256(data.replace(b'\r\n', b'\n')).hexdigest()


def read_file(path, staged=False, root=ROOT):
    """Committed-to-be content (the git index) with staged=True, else the working tree. None if missing."""
    if staged:
        result = subprocess.run(['git', 'show', ':' + path], cwd=root, capture_output=True)
        return result.stdout if result.returncode == 0 else None
    full = Path(root) / path
    return full.read_bytes() if full.is_file() else None


def signed_message(manifest):
    body = {k: manifest[k] for k in ('version', 'salt', 'public_key', 'files')}
    return json.dumps(body, sort_keys=True, separators=(',', ':')).encode('utf-8')


def load_manifest(staged=False, root=ROOT):
    data = read_file('Tools/lock/lock.json', staged, root)
    return None if data is None else json.loads(data.decode('utf-8'))


def pinned_key(staged=False, root=ROOT):
    data = read_file('Tools/lock/diva_lock.py', staged, root)
    match = data and re.search(rb"^PINNED_PUBLIC_KEY = '([0-9a-f]*)'", data, re.M)
    return match.group(1).decode() if match else ''


def check(staged=False, root=ROOT):
    """(problems, changed files). No problems = every locked file is exactly as Eva signed it."""
    manifest = load_manifest(staged, root)
    if manifest is None:
        return ['Tools/lock/lock.json is missing'], []
    problems, changed = [], []
    key = pinned_key(staged, root)
    if not key or manifest.get('public_key') != key:
        problems.append("lock.json's key is not Eva's pinned key (PINNED_PUBLIC_KEY in Tools/lock/diva_lock.py)")
    try:
        ok = verify_signature(bytes.fromhex(manifest['public_key']), signed_message(manifest),
                              bytes.fromhex(manifest['signature']))
    except (KeyError, ValueError):
        ok = False
    if not ok:
        problems.append('lock.json is not signed with Eva\'s password (edited by hand?)')
    for path, digest in sorted(manifest.get('files', {}).items()):
        data = read_file(path, staged, root)
        if data is None or file_hash(data) != digest:
            changed.append(path)
    return problems, changed


def explain(problems, changed):
    lines = ['', '🔒 Diva v2.0 lock: the action detection, controls and camera / player lock are password-locked.']
    lines += ['   ' + p for p in problems]
    if changed:
        lines.append('   Changed locked file(s):')
        lines += ['     - ' + c for c in changed]
    lines += ['   Undo the change (git checkout -- <file>), or ask Eva: only her password can approve it',
              '   (python3 Tools/lock/diva_lock.py sign). See GESTURES_LOCKED.md.', '']
    return '\n'.join(lines)


SHOW = False   # --show: the password is visible while typing (some terminals show nothing at a hidden prompt)


def ask_password(prompt='Password: ', confirm=False):
    read = input if SHOW else getpass.getpass
    password = read(prompt).strip()
    if confirm:
        if len(password) < 8:
            sys.exit('Use at least 8 characters.')
        if read('Type it again: ').strip() != password:
            sys.exit('The two passwords differ; nothing changed.')
    return password


def write_signed(files, salt, secret, version):
    manifest = {'version': version, 'salt': salt, 'public_key': public_key(secret).hex(), 'files': {}}
    for path in sorted(set(files)):
        data = read_file(path)
        if data is None:
            sys.exit(f'{path} does not exist; nothing signed.')
        manifest['files'][path] = file_hash(data)
    manifest['signature'] = sign(secret, signed_message(manifest)).hex()
    MANIFEST.write_text(json.dumps(manifest, indent=2, sort_keys=True) + '\n', encoding='utf-8')
    return manifest


def set_pinned_key(key, others=()):
    """Pin the key in this tool and in the CI workflow (both locked files: re-signed right after)."""
    TOOL.write_text(re.sub(r"^PINNED_PUBLIC_KEY = '[0-9a-f]*'", f"PINNED_PUBLIC_KEY = '{key}'",
                           TOOL.read_text(encoding='utf-8'), count=1, flags=re.M), encoding='utf-8')
    workflow = ROOT / '.github' / 'workflows' / 'lock.yml'
    if workflow.is_file():
        workflow.write_text(re.sub(r'DIVA_LOCK_KEY: [0-9a-f]*', f'DIVA_LOCK_KEY: {key}', workflow.read_text(encoding='utf-8')),
                            encoding='utf-8')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('command', nargs='?', default='verify', choices=('verify', 'status', 'sign', 'change-password', 'init'))
    parser.add_argument('--staged', action='store_true', help='check what is staged for the next commit (pre-commit hook)')
    parser.add_argument('--add', action='append', default=[], metavar='PATH', help='sign: lock another file')
    parser.add_argument('--remove', action='append', default=[], metavar='PATH', help='sign: release a file')
    parser.add_argument('--version', default=None, help='sign: version label (default: keep)')
    parser.add_argument('--show', action='store_true', help='show the password while typing (clear the window afterwards: Cmd+K)')
    args = parser.parse_args(argv)
    global SHOW
    SHOW = args.show

    if args.command in ('verify', 'status'):
        problems, changed = check(args.staged)
        if args.command == 'status':
            manifest = load_manifest(args.staged) or {}
            print(f"Diva lock {manifest.get('version', '?')}: {len(manifest.get('files', {}))} locked files")
            for path in sorted(manifest.get('files', {})):
                print(('  CHANGED  ' if path in changed else '  ok       ') + path)
        if problems or changed:
            print(explain(problems, changed), file=sys.stderr)
            return 1
        if args.command == 'verify':
            print('🔒 Diva lock: all locked files are exactly as signed.')
        return 0

    if args.command == 'init':
        if PINNED_PUBLIC_KEY:
            sys.exit('A password is already set. Use change-password (needs the current password).')
        print('Choose the Diva lock password. It is never stored; keep it safe (without it the locked files cannot be changed).')
        password = ask_password('New password: ', confirm=True)
        salt = secrets.token_hex(16)
        secret = secret_from_password(password, salt)
        key = public_key(secret).hex()
        set_pinned_key(key)
        write_signed(DEFAULT_FILES, salt, secret, args.version or '2.0')
        print(f'Password set. {len(DEFAULT_FILES)} files locked. Public key {key[:16]}...')
        if SHOW:
            print('Now press Cmd+K to clear this window (the password is on screen).')
        return 0

    manifest = load_manifest()
    if manifest is None or not PINNED_PUBLIC_KEY:
        sys.exit('No lock yet: run init first.')
    password = ask_password('Diva lock password: ')
    secret = secret_from_password(password, manifest['salt'])
    if public_key(secret).hex() != PINNED_PUBLIC_KEY:
        sys.exit('Wrong password; nothing changed.')
    files = (set(manifest['files']) | set(args.add)) - set(args.remove)
    version = args.version or manifest.get('version', '2.0')
    if args.command == 'change-password':
        new_password = ask_password('New password: ', confirm=True)
        salt = secrets.token_hex(16)
        secret = secret_from_password(new_password, salt)
        set_pinned_key(public_key(secret).hex())
        write_signed(files, salt, secret, version)
        print('Password changed; the lock is re-signed.')
        return 0
    changed = check()[1]
    write_signed(files, manifest['salt'], secret, version)
    print(f'Signed {len(files)} locked files' + (f" (accepted changes: {', '.join(changed)})" if changed else '') + '.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
