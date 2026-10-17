import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'packaging'))
from dependencies import FILE_NAMES, import_dependencies, load_lock, verify_dependencies
from assemble_installer import assemble, REQUIRED, STAGED_DEPENDENCIES


class PackagingTests(unittest.TestCase):
 def setUp(self):
  self.temp = tempfile.TemporaryDirectory(prefix='Syphon.Packaging.Tests-')
  self.root = Path(self.temp.name)
  self.sources = {}
  files = {}
  for name in FILE_NAMES:
   source = self.root / 'sources' / name
   source.parent.mkdir(exist_ok=True)
   source.write_bytes(('fixture: ' + name).encode())
   self.sources[name] = source
   files[name] = dict(bytes=source.stat().st_size, sha256=hashlib.sha256(source.read_bytes()).hexdigest(), version='fixture', source='https://example.test/' + name)
  self.lock_path = self.root / 'lock.json'
  self.lock_path.write_text(json.dumps(dict(schema=1, architecture='win-x64', files=files)), encoding='utf-8')
  self.lock = load_lock(self.lock_path)
  self.cache = self.root / 'cache with spaces'

 def tearDown(self):
  self.temp.cleanup()

 def test_import_and_verify_all_pinned_inputs(self):
  import_dependencies(self.sources, self.cache, self.lock)
  self.assertEqual(set(verify_dependencies(self.cache, self.lock)), set(FILE_NAMES))

 def test_bad_source_changes_nothing(self):
  self.sources['node.exe'].write_bytes(b'wrong binary')
  with self.assertRaises(ValueError): import_dependencies(self.sources, self.cache, self.lock)
  self.assertFalse(self.cache.exists())

 def test_corrupt_cache_rejected(self):
  import_dependencies(self.sources, self.cache, self.lock)
  (self.cache / 'node.exe').write_bytes(b'changed')
  with self.assertRaises(ValueError): verify_dependencies(self.cache, self.lock)

 def test_missing_input_rejected(self):
  del self.sources['ffprobe.exe']
  with self.assertRaises(ValueError): import_dependencies(self.sources, self.cache, self.lock)

 def test_unexpected_cache_file_rejected(self):
  import_dependencies(self.sources, self.cache, self.lock)
  (self.cache / 'user-data.db').write_bytes(b'not a release dependency')
  with self.assertRaises(ValueError): verify_dependencies(self.cache, self.lock)

 def test_input_update_does_not_modify_original_source(self):
  import_dependencies(self.sources, self.cache, self.lock)
  (self.cache / 'node.exe').write_bytes(b'bad cache')
  self.assertNotEqual(self.sources['node.exe'].read_bytes(), b'bad cache')
  import_dependencies(self.sources, self.cache, self.lock)
  verify_dependencies(self.cache, self.lock)

 def test_invalid_lock_paths_rejected(self):
  data=json.loads(self.lock_path.read_text()); data['files']['../escape.exe']=data['files'].pop('node.exe')
  self.lock_path.write_text(json.dumps(data))
  with self.assertRaises(ValueError): load_lock(self.lock_path)

 def test_invalid_lock_hash_rejected(self):
  data=json.loads(self.lock_path.read_text()); data['files']['node.exe']['sha256']='invalid'
  self.lock_path.write_text(json.dumps(data))
  with self.assertRaises(ValueError): load_lock(self.lock_path)

 def stage(self):
  stage=self.root/'stage'; stage.mkdir()
  for name in REQUIRED:
   file=stage/name; file.parent.mkdir(parents=True,exist_ok=True); file.write_bytes(('payload '+name).encode())
  core=self.root/'core.exe'; core.write_bytes((stage/'Uninstall.exe').read_bytes())
  return stage,core

 def test_identical_assembly_ignores_file_timestamps_and_output_paths(self):
  stage,core=self.stage()
  first=assemble(stage,core,self.root/'one','0.1.0-test')
  for file in stage.rglob('*'):
   if file.is_file(): os.utime(file,(1750000000,1750000000))
  second=assemble(stage,core,self.root/'two','0.1.0-test')
  self.assertEqual(first['sha256'],second['sha256'])
  self.assertEqual(Path(first['installer']).read_bytes(),Path(second['installer']).read_bytes())

 def test_stage_output_overlap_rejected(self):
  stage,core=self.stage()
  with self.assertRaises(ValueError): assemble(stage,core,stage/'output','0.1.0-test')

 def test_wrong_uninstaller_core_rejected(self):
  stage,core=self.stage(); core.write_bytes(b'another build')
  with self.assertRaises(ValueError): assemble(stage,core,self.root/'out','0.1.0-test')

 def test_missing_required_payload_rejected(self):
  stage,core=self.stage(); (stage/'app/Syphon.exe').unlink()
  with self.assertRaises(ValueError): assemble(stage,core,self.root/'out','0.1.0-test')

 def test_project_license_cannot_be_omitted(self):
  stage,core=self.stage(); (stage/'LICENSE.txt').unlink()
  with self.assertRaises(ValueError): assemble(stage,core,self.root/'out','0.1.0-test')

 def test_staged_dependency_must_still_match_lock(self):
  stage,core=self.stage()
  for name,relative in STAGED_DEPENDENCIES.items():
   target=stage/relative; target.parent.mkdir(parents=True,exist_ok=True); target.write_bytes(self.sources[name].read_bytes())
  assemble(stage,core,self.root/'verified','0.1.0-test',self.lock)
  (stage/STAGED_DEPENDENCIES['node.exe']).write_bytes(b'changed after import')
  with self.assertRaises(ValueError): assemble(stage,core,self.root/'bad','0.1.0-test',self.lock)

 def test_stage_link_rejected(self):
  stage,core=self.stage()
  try: (stage/'shortcut').symlink_to(core)
  except OSError: self.skipTest('Creating symlinks is not permitted for this account')
  with self.assertRaises(ValueError): assemble(stage,core,self.root/'out','0.1.0-test')


if __name__ == '__main__': unittest.main()
