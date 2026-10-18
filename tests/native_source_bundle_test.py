"""Integrity checks for the source companion; no dependency code is executed."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'packaging'))
from verify_native_sources import verify_bundle, lock_identity


class SourceBundleTests(unittest.TestCase):
    def test_release_identity_survives_checkout_newline_and_formatting_changes(self):
        with tempfile.TemporaryDirectory() as folder:
            a, b = Path(folder) / 'a.json', Path(folder) / 'b.json'
            a.write_bytes(b'{"schema":1,"files":{}}\n')
            b.write_bytes(b'{\r\n  "files": {}, "schema": 1\r\n}\r\n')
            self.assertEqual(lock_identity(a), lock_identity(b))

    def fixture(self, root, *, content=b'source', expected=b'source', name='sources/file.c', extra=False, identity='a' * 64):
        path = root / 'sources.zip'
        manifest = {'schema': 1, 'dependencyLockSha256': identity,
                    'files': {name: {'bytes': len(expected), 'sha256': hashlib.sha256(expected).hexdigest()}}}
        with zipfile.ZipFile(path, 'w') as archive:
            archive.writestr(name, content)
            archive.writestr('native-source-bundle.json', json.dumps(manifest))
            if extra:
                archive.writestr('surprise.txt', b'untracked')
        receipt = {'bytes': path.stat().st_size, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
        return path, receipt

    def test_all_members_and_current_identity_are_verified(self):
        with tempfile.TemporaryDirectory() as folder:
            path, receipt = self.fixture(Path(folder))
            self.assertEqual(verify_bundle(path, receipt, 'a' * 64), 1)

    def test_corrupt_member_cannot_be_hidden_by_matching_archive_receipt(self):
        with tempfile.TemporaryDirectory() as folder:
            path, receipt = self.fixture(Path(folder), content=b'broken')
            with self.assertRaises(ValueError):
                verify_bundle(path, receipt, 'a' * 64)

    def test_unlisted_member_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path, receipt = self.fixture(Path(folder), extra=True)
            with self.assertRaises(ValueError):
                verify_bundle(path, receipt, 'a' * 64)

    def test_wrong_release_identity_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path, receipt = self.fixture(Path(folder))
            with self.assertRaises(ValueError):
                verify_bundle(path, receipt, 'b' * 64)

    def test_unsafe_paths_are_rejected_even_without_extraction(self):
        for name in ['../escape', 'C:/escape', '/absolute', 'x\\escape', 'a/../escape']:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as folder:
                path, receipt = self.fixture(Path(folder), name=name)
                with self.assertRaises(ValueError):
                    verify_bundle(path, receipt, 'a' * 64)

    def test_wrong_archive_receipt_is_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path, receipt = self.fixture(Path(folder))
            receipt['sha256'] = '0' * 64
            with self.assertRaises(ValueError):
                verify_bundle(path, receipt, 'a' * 64)


if __name__ == '__main__':
    unittest.main()
