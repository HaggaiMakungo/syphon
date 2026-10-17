import sys,unittest,tempfile,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'packaging'))
from build_ytdlp import safe_member,patch_version
class BuildTests(unittest.TestCase):
 def test_traversal(self):
  for name in ['../evil','/root','C:/evil','a\\..\\evil','a:b','a/../b']:
   with self.assertRaises(ValueError):safe_member(name)
 def test_plain_member(self):self.assertEqual(safe_member('pkg/module.py').as_posix(),'pkg/module.py')
 def test_local_variant_preserves_code_identity(self):
  original="VARIANT = None\nUPDATE_HINT = None\nCHANNEL = 'stable'\nORIGIN = 'yt-dlp/yt-dlp'\nRELEASE_GIT_HEAD = 'abc'\n"
  result=patch_version(original)
  self.assertIn("VARIANT = 'syphon_win_x64_py313'",result)
  self.assertIn("CHANNEL = 'stable'",result)
  self.assertIn("RELEASE_GIT_HEAD = 'abc'",result)
  self.assertIn('Update this bundled tool through Syphon setup.',result)
 def test_already_patched_rejected(self):
  with self.assertRaises(ValueError):patch_version("VARIANT = 'unknown'")
if __name__=='__main__':unittest.main()
