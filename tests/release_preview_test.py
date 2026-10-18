import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'packaging'))
from prepare_release import prepare, verify


class ReleasePreviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='Syphon.Release.Tests-')
        self.root = Path(self.temp.name)
        self.installer = self.root / 'Syphon-Setup-0.1.0-beta.17-win-x64.exe'
        self.installer.write_bytes(b'fixture installer')
        self.extracted = self.root / 'extracted'
        extension = self.extracted / 'extensions'
        extension.mkdir(parents=True)
        (extension / 'manifest.json').write_text(json.dumps({'version': '0.7.4', 'key': 'identity-fixture'}))
        (extension / 'connection.js').write_text('exact beta17 fixture')
        (self.extracted / 'build-inputs.json').write_text(json.dumps({
            'version': '0.1.0-beta.17', 'architecture': 'win-x64',
            'sourceDirty': False, 'sourceRevision': 'a' * 40}))
        self.manifest = self.root / 'release-manifest.json'
        self.manifest.write_text(json.dumps({'Version': '0.1.0-beta.17', 'Files': {
            p.relative_to(self.extracted).as_posix(): {'Length': p.stat().st_size,
                'Sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
            for p in self.extracted.rglob('*') if p.is_file()}}))
        self.record = self.root / 'self-sign-summary.json'
        self.record.write_text(json.dumps({'sha256': hashlib.sha256(self.installer.read_bytes()).hexdigest(),
            'bytes': self.installer.stat().st_size, 'signed': True,
            'cryptographicSignatureValid': True, 'fileDigestValid': True,
            'certificateThumbprint': 'B' * 40, 'publiclyTrusted': False}))
        self.sources = []
        for name in ('native', 'ffmpeg'):
            archive = self.root / (name + '.zip')
            archive.write_bytes(name.encode())
            self.sources.append({'file': archive.name, 'bytes': archive.stat().st_size,
                'sha256': hashlib.sha256(archive.read_bytes()).hexdigest()})
        self.output = self.root / 'preview'

    def tearDown(self):
        self.temp.cleanup()

    def build(self):
        return prepare(self.installer, self.record, self.extracted, self.manifest, self.output, '2026-10-18', self.sources)

    def test_exact_installer_extension_and_complete_local_links(self):
        result = self.build()
        self.assertFalse(result['publiclyPublished'])
        self.assertFalse(result['acceptance']['ownerReportedFunctionalAndInstallerPassOnThisPC'])
        self.assertEqual(len(result['sources']), 2)
        for source in self.sources:
            self.assertEqual((self.output / source['file']).read_bytes(), (self.root / source['file']).read_bytes())
        self.assertEqual((self.output / self.installer.name).read_bytes(), self.installer.read_bytes())
        with zipfile.ZipFile(self.output / result['extension']['file']) as archive:
            self.assertEqual(set(archive.namelist()), {'manifest.json', 'connection.js'})
            self.assertEqual(archive.read('connection.js'), b'exact beta17 fixture')
            self.assertEqual(json.loads(archive.read('manifest.json'))['key'], 'identity-fixture')
        verify(self.output)

    def test_source_tampering_refused_before_output(self):
        (self.root / self.sources[0]['file']).write_bytes(b'corrupt')
        with self.assertRaises(ValueError): self.build()
        self.assertFalse(self.output.exists())

    def test_installer_tampering_refused_before_output(self):
        self.installer.write_bytes(b'wrong installer')
        with self.assertRaises(ValueError): self.build()
        self.assertFalse(self.output.exists())

    def test_extension_tampering_refused_before_output(self):
        (self.extracted / 'extensions/connection.js').write_text('stale extension')
        with self.assertRaises(ValueError): self.build()
        self.assertFalse(self.output.exists())

    def test_provenance_version_mismatch_refused(self):
        value = json.loads(self.manifest.read_text())
        value['Version'] = '0.1.0-beta.12'
        self.manifest.write_text(json.dumps(value))
        with self.assertRaises(ValueError): self.build()
        self.assertFalse(self.output.exists())

    def test_existing_output_preserved(self):
        self.output.mkdir()
        keep = self.output / 'keep.txt'
        keep.write_text('keep')
        with self.assertRaises(ValueError): self.build()
        self.assertEqual(keep.read_text(), 'keep')

    def test_unverified_signing_receipt_refused(self):
        value = json.loads(self.record.read_text())
        value['fileDigestValid'] = False
        self.record.write_text(json.dumps(value))
        with self.assertRaises(ValueError): self.build()
        self.assertFalse(self.output.exists())

    def test_broken_page_link_detected_even_with_updated_checksum(self):
        self.build()
        page = self.output / 'index.html'
        page.write_text(page.read_text(encoding='utf-8').replace('RELEASE-NOTES.md', 'missing.md'), encoding='utf-8')
        sums = self.output / 'SHA256SUMS.txt'
        lines = sums.read_text(encoding='utf-8').splitlines()
        sums.write_text('\n'.join(hashlib.sha256(page.read_bytes()).hexdigest() + '  index.html'
            if line.endswith('  index.html') else line for line in lines) + '\n', encoding='utf-8')
        with self.assertRaisesRegex(ValueError, 'Broken local page link'): verify(self.output)

    def test_post_build_tampering_detected(self):
        result = self.build()
        (self.output / result['extension']['file']).write_bytes(b'damaged')
        with self.assertRaises(ValueError): verify(self.output)


if __name__ == '__main__':
    unittest.main()
