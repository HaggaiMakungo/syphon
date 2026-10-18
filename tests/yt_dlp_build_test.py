import sys,unittest,tempfile,zipfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'packaging'))
from build_ytdlp import safe_member,patch_version,build_environment,verify_native_origins
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
 def test_build_environment_excludes_unpinned_host_library_paths_and_cache(self):
  from unittest.mock import patch
  import os
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);runtime=root/'python';work=root/'work'
   with patch.dict(os.environ,{'PATH':'unreviewed-library-directory','PYTHONPATH':'unreviewed-module-directory','SystemRoot':str(root/'Windows')}):
    env=build_environment(runtime,work)
   self.assertNotIn('unreviewed',env['PATH'])
   self.assertNotIn('PYTHONPATH',env)
   self.assertEqual(env['PYINSTALLER_CONFIG_DIR'],str(work/'pyinstaller-cache'))
   self.assertIn(str(runtime),env['PATH'])
 def test_binary_origin_outside_private_inputs_is_rejected(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);private=root/'python';private.mkdir();outside=root/'host.dll';outside.write_bytes(b'host')
   toc=root/'PKG-00.toc';toc.write_text(repr(('output',[('ucrtbase.dll',str(outside),'BINARY')],False)))
   with self.assertRaises(ValueError):verify_native_origins(toc,private)
 def test_private_binary_origins_are_recorded_without_machine_paths(self):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);private=root/'python';private.mkdir();binary=private/'python313.dll';binary.write_bytes(b'pinned')
   toc=root/'PKG-00.toc';toc.write_text(repr(('output',[('python313.dll',str(binary),'BINARY')],False)))
   records=verify_native_origins(toc,private)
   self.assertEqual(records[0]['privateInput'],'python313.dll')
   self.assertNotIn(folder,str(records))
if __name__=='__main__':unittest.main()
