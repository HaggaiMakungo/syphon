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

    def set_build_version(self, version):
        build_path = self.extracted / 'build-inputs.json'
        build = json.loads(build_path.read_text())
        build['version'] = version
        build_path.write_text(json.dumps(build))
        manifest = json.loads(self.manifest.read_text())
        manifest['Version'] = version
        manifest['Files']['build-inputs.json'] = {'Length': build_path.stat().st_size,
            'Sha256': hashlib.sha256(build_path.read_bytes()).hexdigest()}
        self.manifest.write_text(json.dumps(manifest))
        renamed = self.root / f'Syphon-Setup-{version}-win-x64.exe'
        self.installer.rename(renamed)
        self.installer = renamed

    def test_beta20_requested_version_matches_verified_inputs(self):
        self.set_build_version('0.1.0-beta.20')
        result = prepare(self.installer, self.record, self.extracted, self.manifest,
            self.output, '2026-10-27', self.sources, expected_version='0.1.0-beta.20')
        self.assertEqual(result['version'], '0.1.0-beta.20')
        self.assertIn('Owner 0.1.0-beta.20 upgrade acceptance', result['pending'])
        self.assertNotIn('Owner Beta17 upgrade acceptance', result['pending'])
        self.assertTrue(result['extension']['file'].endswith('-beta20.zip'))
        verify(self.output)

    def test_requested_version_mismatch_refused_before_output(self):
        with self.assertRaisesRegex(ValueError, 'Requested release version'):
            prepare(self.installer, self.record, self.extracted, self.manifest,
                self.output, '2026-10-27', self.sources, expected_version='0.1.0-beta.20')
        self.assertFalse(self.output.exists())

    def test_invalid_build_version_refused_before_output(self):
        self.set_build_version('not-a-version')
        with self.assertRaisesRegex(ValueError, 'Release versions'):
            self.build()
        self.assertFalse(self.output.exists())

    def test_non_beta_versions_are_not_labelled_as_beta_releases(self):
        for version in ('1.2.3', '1.2.3-rc.1'):
            with self.subTest(version=version):
                self.set_build_version(version)
                with self.assertRaisesRegex(ValueError, 'Release versions'):
                    self.build()
                self.assertFalse(self.output.exists())

    def test_signing_receipt_version_mismatch_refused_before_output(self):
        receipt = json.loads(self.record.read_text())
        receipt['version'] = '0.1.0-beta.20'
        self.record.write_text(json.dumps(receipt))
        with self.assertRaisesRegex(ValueError, 'Signing receipt version'):
            self.build()
        self.assertFalse(self.output.exists())

    def test_dirty_source_is_still_refused_for_beta20(self):
        self.set_build_version('0.1.0-beta.20')
        path = self.extracted / 'build-inputs.json'
        build = json.loads(path.read_text())
        build['sourceDirty'] = True
        path.write_text(json.dumps(build))
        manifest = json.loads(self.manifest.read_text())
        manifest['Files']['build-inputs.json'] = {'Length': path.stat().st_size,
            'Sha256': hashlib.sha256(path.read_bytes()).hexdigest()}
        self.manifest.write_text(json.dumps(manifest))
        with self.assertRaisesRegex(ValueError, 'clean Windows x64'):
            self.build()
        self.assertFalse(self.output.exists())

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
