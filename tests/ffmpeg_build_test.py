import io,json,sys,tarfile,tempfile,unittest,zipfile,hashlib
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'packaging'))
import build_ffmpeg as b

class BuildTests(unittest.TestCase):
 def test_missing_or_corrupt_inputs_never_create_workspace(self):
  with tempfile.TemporaryDirectory() as d:
   root=Path(d);inputs=root/'inputs';inputs.mkdir();work=root/'work'
   lock={'files':[{'file':'source.tar','bytes':3,'sha256':hashlib.sha256(b'abc').hexdigest()}]}
   for data in [None,b'bad']:
    if data is not None:(inputs/'source.tar').write_bytes(data)
    with self.assertRaises((ValueError,OSError)):b.preflight(lock,inputs,work)
    self.assertFalse(work.exists())
 def test_existing_or_nested_work_rejected(self):
  with tempfile.TemporaryDirectory() as d:
   root=Path(d);inputs=root/'inputs';inputs.mkdir()
   for work in [root,inputs,inputs/'build']:
    with self.assertRaises(ValueError):b.preflight({'files':[]},inputs,work)
 def test_tar_traversal_and_links_rejected_before_extracting(self):
  with tempfile.TemporaryDirectory() as d:
   root=Path(d)
   for name,kind in [('../escape',tarfile.REGTYPE),('C:/escape',tarfile.REGTYPE),('pkg/link',tarfile.SYMTYPE)]:
    archive=root/'bad.tar'
    with tarfile.open(archive,'w') as t:
     good=tarfile.TarInfo('pkg/good');good.size=1;t.addfile(good,io.BytesIO(b'x'))
     item=tarfile.TarInfo(name);item.type=kind;item.linkname='../escape';t.addfile(item)
    out=root/'out'
    with self.assertRaises(ValueError):b.unpack(archive,out)
    self.assertFalse(out.exists())
 def test_zip_traversal_and_link_rejected(self):
  with tempfile.TemporaryDirectory() as d:
   root=Path(d)
   for name,mode in [('a/../../escape',0),('link',0o120777<<16),('pkg/File',0)]:
    a=root/'bad.zip'
    with zipfile.ZipFile(a,'w') as z:
     z.writestr('pkg/file',b'a');i=zipfile.ZipInfo(name);i.external_attr=mode;z.writestr(i,b'bad')
    with self.assertRaises(ValueError):b.unpack(a,root/'out')
    self.assertFalse((root/'out').exists())
 def test_repeated_directories_are_safe_but_file_directory_aliases_are_not(self):
  with tempfile.TemporaryDirectory() as d:
   root=Path(d);a=root/'dirs.tar'
   with tarfile.open(a,'w') as t:
    for name in ['pkg/','pkg/']:
     i=tarfile.TarInfo(name);i.type=tarfile.DIRTYPE;t.addfile(i)
    i=tarfile.TarInfo('pkg/a');i.size=1;t.addfile(i,io.BytesIO(b'x'))
   b.unpack(a,root/'out');self.assertEqual((root/'out/pkg/a').read_bytes(),b'x')
 def test_extract_regular_files(self):
  with tempfile.TemporaryDirectory() as d:
   root=Path(d);a=root/'good.tar'
   with tarfile.open(a,'w') as t:
    i=tarfile.TarInfo('pkg/a.txt');i.size=2;t.addfile(i,io.BytesIO(b'ok'))
   b.unpack(a,root/'out');self.assertEqual((root/'out/pkg/a.txt').read_bytes(),b'ok')
 def test_native_program_lookup_uses_only_private_toolchain(self):
  tools=Path('D:/private/bin')
  self.assertEqual(b.command_path('cmake',tools),str(tools/'cmake.exe'))
  self.assertEqual(b.command_path('make',tools),str(tools/'make.exe'))
  with self.assertRaises(ValueError):b.command_path('unknown-tool',tools)
 def test_lame_targets_native_windows_explicitly(self):
  args=b.lame_configure(Path('D:/prefix'))
  self.assertIn('--build=x86_64-w64-mingw32',args)
  self.assertIn('--host=x86_64-w64-mingw32',args)
 def test_static_zlib_metadata_selects_actual_windows_library(self):
  original='Libs: -L${libdir} -lz\nRequires: unrelated\n'
  self.assertEqual(b.zlib_static_pc(original),'Libs: -L${libdir} -lzs\nRequires: unrelated\n')
  with self.assertRaises(ValueError):b.zlib_static_pc('unexpected')
 def test_old_autoconf_can_find_windows_executable_names(self):
  env=b.native_environment(Path('D:/private/bin'),Path('D:/nasm'),Path('D:/prefix'),1)
  self.assertEqual(env['PATH_SEPARATOR'],';')
  self.assertNotIn('\\',env['PATH'])
  self.assertEqual(env['ac_executable_extensions'],'.exe')
  self.assertEqual(env['CONFIG_SHELL'],'D:/private/bin/sh.exe')
 def test_static_linking_uses_private_prefix_and_transitive_dependencies(self):
  args=b.ffmpeg_configure(Path('D:/prefix'))
  self.assertIn('--pkg-config-flags=--static',args)
  self.assertIn('--extra-cflags=-ID:/prefix/include',args)
  self.assertIn('--extra-ldflags=-static -LD:/prefix/lib',args)
 def test_lame_install_has_an_explicit_portable_install_program(self):
  self.assertEqual(b.lame_install(Path('D:/private/bin')),
   ['make','install','INSTALL=D:/private/bin/install.exe -c'])
 def test_x264_shell_patch_is_guarded_and_retains_surrounding_code(self):
  original='#!/bin/bash\n'+''.join((old+'\n')*count for old,(_,count) in b.X264_REPLACEMENTS.items())+'\n# encoder settings remain here\n'
  result=b.portable_x264(original)
  self.assertTrue(result.startswith('#!/bin/sh\n'))
  self.assertNotIn('[[',result)
  self.assertIn('# encoder settings remain here',result)
  with self.assertRaises(ValueError):b.portable_x264(result)
  with self.assertRaises(ValueError):b.portable_x264(original.replace(next(iter(b.X264_REPLACEMENTS)),''))
 def test_shell_compiler_probes_use_private_forward_slash_temp_paths(self):
  env=b.native_environment(Path('D:/private/bin'),Path('D:/nasm'),Path('D:/build/prefix'),1)
  self.assertEqual(env['TMPDIR'],'D:/build/tmp')
  self.assertEqual(env['TMP'],env['TMPDIR'])
  self.assertEqual(env['TEMP'],env['TMPDIR'])
 def test_recipe_record_captures_lock_and_helpers_before_build(self):
  with tempfile.TemporaryDirectory() as d:
   work=Path(d);lock={'schema':1,'sourceDateEpoch':123,'files':[]}
   identity=b.capture_recipe(work,lock)
   retained=work/'recipes'/identity['recipeSha256']
   self.assertEqual(json.loads((retained/'ffmpeg-build.win-x64.json').read_text()),lock)
   self.assertTrue((retained/'dependencies.py').is_file())
   (retained/'build_ffmpeg.py').write_text('tampered')
   self.assertNotEqual(hashlib.sha256((retained/'build_ffmpeg.py').read_bytes()).hexdigest(),identity['recipeSha256'])
 def test_ffmpeg_make_disables_quiet_shell_prefixes(self):
  self.assertEqual(b.ffmpeg_make(2),['make','V=1','-j2'])
 def test_flags_require_windows_tls_and_no_autodetection(self):
  flags=b.FFMPEG_FLAGS
  for flag in ['--disable-autodetect','--enable-schannel','--enable-libmp3lame','--enable-libxml2','--enable-libx264','--enable-libwebp','--enable-gpl','--enable-version3']:
   self.assertIn(flag,flags)
  self.assertNotIn('--enable-nonfree',flags)
if __name__=='__main__':unittest.main()
