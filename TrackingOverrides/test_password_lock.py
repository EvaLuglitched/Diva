"""🔒 Diva v2.0 password lock (Tools/lock): the action detection, the controls and the camera / player lock are exactly
as Eva signed them. If this fails, a locked file changed: undo it, or ask Eva (only her password can re-sign).
Do not edit Tools/lock/lock.json, the pinned key or this test to make it pass."""
import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('diva_lock', ROOT / 'Tools' / 'lock' / 'diva_lock.py')
lock = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lock)


class Ed25519Tests(unittest.TestCase):
    def test_rfc8032_vectors(self):
        for secret, public, message, signature in (
                ('9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60',
                 'd75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a', '',
                 'e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b'),
                ('4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb',
                 '3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c', '72',
                 '92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00')):
            secret, message = bytes.fromhex(secret), bytes.fromhex(message)
            self.assertEqual(lock.public_key(secret).hex(), public)
            self.assertEqual(lock.sign(secret, message).hex(), signature)
            self.assertTrue(lock.verify_signature(bytes.fromhex(public), message, bytes.fromhex(signature)))
            self.assertFalse(lock.verify_signature(bytes.fromhex(public), message + b'!', bytes.fromhex(signature)))


class RepositoryLockTests(unittest.TestCase):
    def test_locked_files_are_exactly_as_signed(self):
        problems, changed = lock.check()
        self.assertEqual((problems, changed), ([], []), lock.explain(problems, changed))

    def test_the_core_files_are_locked(self):
        files = json.loads((ROOT / 'Tools' / 'lock' / 'lock.json').read_text(encoding='utf-8'))['files']
        for path in ('TrackingOverrides/bridge.py', 'TrackingOverrides/diva_gestures.py', 'TrackingOverrides/diva_gestures.json',
                     'Assets/DigiPhant/Runtime/DivaControlState.cs', 'Assets/DigiPhant/Runtime/DivaDemo.cs',
                     'Assets/DigiPhant/Runtime/DigiPhantController.cs', 'Assets/DigiPhant/Runtime/DigiPhantLocomotion.cs',
                     'Assets/DigiPhant/Runtime/DigiPhantCameraPreview.cs', 'Assets/DivaShow/Runtime/DivaShowCameraCheck.cs',
                     'Tools/lock/diva_lock.py', 'TrackingOverrides/test_password_lock.py'):
            self.assertIn(path, files)


class TamperTests(unittest.TestCase):
    """A copy of the repository's locked files: every way of changing them without the password is caught."""
    def setUp(self):
        self.dir = Path(tempfile.mkdtemp())
        manifest = json.loads((ROOT / 'Tools' / 'lock' / 'lock.json').read_text(encoding='utf-8'))
        for path in list(manifest['files']) + ['Tools/lock/lock.json']:
            (self.dir / path).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(ROOT / path, self.dir / path)

    def tearDown(self):
        shutil.rmtree(self.dir)

    def manifest(self):
        return json.loads((self.dir / 'Tools/lock/lock.json').read_text(encoding='utf-8'))

    def save(self, manifest):
        (self.dir / 'Tools/lock/lock.json').write_text(json.dumps(manifest), encoding='utf-8')

    def test_untouched_copy_passes(self):
        self.assertEqual(lock.check(root=self.dir), ([], []))

    def test_windows_line_endings_still_pass(self):
        path = self.dir / 'TrackingOverrides/diva_gestures.py'
        path.write_bytes(path.read_bytes().replace(b'\n', b'\r\n'))
        self.assertEqual(lock.check(root=self.dir), ([], []))

    def test_editing_a_locked_file_is_caught(self):
        path = self.dir / 'TrackingOverrides/diva_gestures.json'
        path.write_text(path.read_text(encoding='utf-8').replace('0.14', '0.10', 1), encoding='utf-8')
        self.assertEqual(lock.check(root=self.dir)[1], ['TrackingOverrides/diva_gestures.json'])

    def test_updating_the_hash_by_hand_breaks_the_signature(self):
        path = self.dir / 'TrackingOverrides/diva_gestures.json'
        path.write_text(path.read_text(encoding='utf-8') + ' ', encoding='utf-8')
        manifest = self.manifest()
        manifest['files']['TrackingOverrides/diva_gestures.json'] = lock.file_hash(path.read_bytes())
        self.save(manifest)
        problems, changed = lock.check(root=self.dir)
        self.assertEqual(changed, [])
        self.assertTrue(any('not signed' in p for p in problems))

    def test_unlocking_a_file_by_hand_breaks_the_signature(self):
        manifest = self.manifest()
        del manifest['files']['TrackingOverrides/bridge.py']
        self.save(manifest)
        self.assertTrue(lock.check(root=self.dir)[0])

    def test_signing_with_another_password_is_caught(self):
        manifest = self.manifest()
        secret = lock.secret_from_password('not the password', manifest['salt'])
        manifest['public_key'] = lock.public_key(secret).hex()
        manifest['signature'] = lock.sign(secret, lock.signed_message(manifest)).hex()
        self.save(manifest)
        problems, _ = lock.check(root=self.dir)
        self.assertTrue(any('pinned' in p for p in problems))

    def test_deleting_a_locked_file_is_caught(self):
        (self.dir / 'Assets/DigiPhant/Runtime/DivaDemo.cs').unlink()
        self.assertEqual(lock.check(root=self.dir)[1], ['Assets/DigiPhant/Runtime/DivaDemo.cs'])


if __name__ == '__main__':
    unittest.main()
