import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'packaging'))
from prepare_public_page import prepare

class PublicPageTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.preview = self.root / 'preview'
        self.preview.mkdir()
        (self.preview / 'assets').mkdir()
        (self.preview / 'assets/site.css').write_text('body{}')
        (self.preview / 'index.html').write_text('<meta name="robots" content="noindex,nofollow"><p>Local release preview <span>Not published</span></p><a href="setup.exe">Download</a><a href="SHA256SUMS.txt">Verify</a><h2 id="sources-title">Sources</h2>')
        self.inputs = self.root / 'release'
        self.inputs.mkdir()
        (self.inputs / 'SHA256SUMS.txt').write_text('checksum fixture')
        item = {'file': 'setup.exe', 'bytes': 4, 'sha256': hashlib.sha256(b'test').hexdigest()}
        (self.preview / 'release.json').write_text(json.dumps({'version': '0.1.0-beta.17', 'publiclyPublished': False, 'installer': item, 'extension': item, 'sources': [item], 'pending': ['Public release authorization', 'Owner Beta17 upgrade acceptance']}))
        self.release = {'draft': False, 'prerelease': True, 'tag_name': 'v0.1.0-beta.17', 'html_url': 'https://github.com/HaggaiMakungo/syphon/releases/tag/v0.1.0-beta.17', 'assets': [self.asset('setup.exe',4,item['sha256']),self.asset('SHA256SUMS.txt',16,'a'*64)]}
        self.output = self.root / 'site'
    def asset(self,name,size,digest):
        return {'name':name,'size':size,'state':'uploaded','digest':'sha256:'+digest,'browser_download_url':'https://github.com/HaggaiMakungo/syphon/releases/download/v0.1.0-beta.17/'+name}
    def tearDown(self):
        self.tmp.cleanup()
    def test_only_small_site_files_and_real_release_links(self):
        result = prepare(self.preview,self.inputs,self.release,self.output)
        page = (self.output/'index.html').read_text()
        self.assertIn(self.release['assets'][0]['browser_download_url'],page)
        self.assertNotIn('Not published',page)
        self.assertNotIn('noindex',page)
        self.assertTrue(result['publiclyPublished'])
        self.assertTrue((self.output/'.nojekyll').exists())
        self.assertFalse((self.output/'setup.exe').exists())
        self.assertNotIn('Public release authorization',result['pending'])
    def test_draft_release_refused(self):
        self.release['draft']=True
        with self.assertRaises(ValueError):prepare(self.preview,self.inputs,self.release,self.output)
        self.assertFalse(self.output.exists())
    def test_wrong_binary_digest_refused(self):
        self.release['assets'][0]['digest']='sha256:'+'0'*64
        with self.assertRaises(ValueError):prepare(self.preview,self.inputs,self.release,self.output)
        self.assertFalse(self.output.exists())
    def test_wrong_asset_origin_refused(self):
        self.release['assets'][0]['browser_download_url']='https://example.com/setup.exe'
        with self.assertRaises(ValueError):prepare(self.preview,self.inputs,self.release,self.output)
        self.assertFalse(self.output.exists())

if __name__=='__main__':unittest.main()
