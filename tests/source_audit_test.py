from pathlib import Path
import hashlib
import json
import sys
import tempfile
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'packaging'))
from source_audit import ffmpeg_versions, third_party_notices, verify_materials, make_inventory

class SourceAuditTests(unittest.TestCase):
 def test_vendor_versions_preserve_uncertainty(self):
  records=ffmpeg_versions("git-full external libraries' versions:\n\nx264 v0.165.3223\nopenal-soft latest\n")
  self.assertEqual(records[1]['identityStatus'],'unresolved')
  self.assertEqual(records[0]['identityStatus'],'vendor-reported-only')
  self.assertEqual(records[0]['sourceStatus'],'not-collected')

 def test_vendor_list_missing_rejected(self):
  with self.assertRaises(ValueError):ffmpeg_versions('External libraries: x264')

 def test_duplicate_vendor_identity_rejected(self):
  with self.assertRaises(ValueError):ffmpeg_versions("git-full external libraries' versions:\nx264 1\nx264 2")

 def test_incomplete_vendor_version_rejected(self):
  with self.assertRaises(ValueError):ffmpeg_versions("git-full external libraries' versions:\nx264")

 def test_notices_are_not_versions(self):
  records=third_party_notices('----------------\nmutagen | GPL-2.0-or-later\nURL: https://example.test/\n----------------\n')
  self.assertEqual(records[0]['licenseReported'],'GPL-2.0-or-later')
  self.assertIsNone(records[0]['version'])
  self.assertEqual(records[0]['sourceStatus'],'not-collected')

 def test_absent_notice_headers_fail(self):
  with self.assertRaises(ValueError):third_party_notices('an upstream homepage URL is not an inventory')

 def test_source_urls_do_not_mean_sources_collected(self):
  lock={'files':{'ffmpeg.exe':{'version':'2026-git-abcdef0123','source':'https://example.test/'},'yt-dlp.exe':{'version':'2026.08.19','source':'https://example.test/'}}}
  report=make_inventory(lock,'lock-hash',"git-full external libraries' versions:\nx264 1",'mutagen | GPL-2.0-or-later\nURL: https://example.test/\n',[])
  self.assertFalse(report['sourceDistributionComplete'])
  self.assertEqual(report['sourceArchives'],[])
  self.assertGreater(len(report['gaps']),0)

 def test_bad_source_evidence_rejected(self):
  with tempfile.TemporaryDirectory(prefix='Syphon.SourceAudit.Tests-') as folder:
   root=Path(folder);(root/'source-materials.json').write_text('{"schema":1,"dependencyLockSha256":"wrong","sources":[]}')
   with self.assertRaises(ValueError):verify_materials(root,'expected')

 def test_verified_partial_evidence_is_not_marked_complete(self):
  with tempfile.TemporaryDirectory(prefix='Syphon.SourceAudit.Tests-') as folder:
   root=Path(folder);blob=b'retained source evidence';(root/'source.tar.gz').write_bytes(blob)
   record={'file':'source.tar.gz','bytes':len(blob),'sha256':hashlib.sha256(blob).hexdigest()}
   (root/'source-materials.json').write_text(json.dumps({'schema':1,'dependencyLockSha256':'expected','sources':[record],'complete':True}))
   result=verify_materials(root,'expected')
   self.assertEqual(result[0]['status'],'present-file-hash-matched')
   self.assertIn('partial',result[0]['coverage'])
   (root/'source.tar.gz').write_bytes(b'changed')
   with self.assertRaises(ValueError):verify_materials(root,'expected')

 def test_evidence_paths_cannot_escape_root(self):
  with tempfile.TemporaryDirectory(prefix='Syphon.SourceAudit.Tests-') as folder:
   root=Path(folder)
   (root/'source-materials.json').write_text(json.dumps({'schema':1,'dependencyLockSha256':'expected','sources':[{'file':'../source.tar.gz','bytes':1,'sha256':'0'*64}]}))
   with self.assertRaises(ValueError):verify_materials(root,'expected')

if __name__=='__main__':unittest.main()
