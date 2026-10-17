"""Build the reviewed Syphon-local yt-dlp offline from hash-locked inputs.

Downloads are deliberately separate. Use a new dedicated --work directory on a
volume with sufficient space. No global Python, PATH or user data is changed.
"""
import argparse,hashlib,json,os,re,shutil,subprocess,tarfile,zipfile
from pathlib import Path,PurePosixPath
from dependencies import plain_path,verify_file,digest

LOCK=Path(__file__).with_name('yt-dlp-build.win-x64.json')

def safe_member(name):
 if not name or '\\' in name or ':' in name or name.startswith('/') or '..' in PurePosixPath(name).parts:raise ValueError('Unsafe archive member')
 return Path(*PurePosixPath(name).parts)

def patch_version(text):
 for old in ['VARIANT = None','UPDATE_HINT = None',"ORIGIN = 'yt-dlp/yt-dlp'"]:
  if text.count(old)!=1:raise ValueError('Unexpected upstream version template')
 return text.replace('VARIANT = None',"VARIANT = 'syphon_win_x64_py313'").replace('UPDATE_HINT = None',"UPDATE_HINT = 'Update this bundled tool through Syphon setup.'").replace("ORIGIN = 'yt-dlp/yt-dlp'","ORIGIN = 'HaggaiMakungo/syphon'")

def unpack_zip(archive,target):
 with zipfile.ZipFile(archive) as z:
  for item in z.infolist():
   name=safe_member(item.filename)
   if (item.external_attr>>16)&0o170000==0o120000:raise ValueError('Archive link rejected')
   if not item.is_dir():
    p=target/name;plain_path(p);p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(z.read(item))

def build(inputs,work):
 manifest=json.loads(LOCK.read_text(encoding='utf-8'));inputs=plain_path(inputs);work=plain_path(work)
 if work.exists():raise ValueError('Use a new dedicated build work directory')
 if work==inputs or inputs in work.parents or work in inputs.parents:raise ValueError('Build work and inputs must be separate')
 for item in manifest['files']:verify_file(inputs/item['file'],item)
 work.mkdir(parents=True);runtime=work/'python';source=work/'source';runtime.mkdir();source.mkdir()
 unpack_zip(inputs/next(i['file'] for i in manifest['files'] if i['kind']=='python'),runtime)
 (runtime/'python313._pth').write_bytes(b'python313.zip\n.\nLib/site-packages\nimport site\n')
 site=runtime/'Lib/site-packages';site.mkdir(parents=True)
 for item in manifest['files']:
  if item['kind']=='wheel':unpack_zip(inputs/item['file'],site)
 with tarfile.open(inputs/next(i['file'] for i in manifest['files'] if i['kind']=='source')) as archive:
  for member in archive:
   name=safe_member(member.name)
   if member.isdir():continue
   if not member.isfile():raise ValueError('Source archive link or special file rejected')
   name=Path(*name.parts[1:]);target=source/name;plain_path(target);target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(archive.extractfile(member).read())
 version=source/'yt_dlp/version.py';version.write_bytes(patch_version(version.read_text(encoding='utf-8')).encode())
 python=runtime/'python.exe';env=os.environ.copy();env['PYTHONNOUSERSITE']='1';env['PYTHONHASHSEED']='0'
 # Private _pth excludes user/system packages. Direct entry script adds source root.
 with (work/'build.log').open('w',encoding='utf-8') as log:
  for command in [[str(python),'devscripts/make_lazy_extractors.py'],[str(python),'bundle/pyinstaller.py']]:
   subprocess.run(command,cwd=source,env=env,stdout=log,stderr=subprocess.STDOUT,check=True,timeout=900)
 exe=source/'dist/yt-dlp.exe';verify=work/'runtime-inspection.txt'
 probe=subprocess.run([str(exe),'--ignore-config','--no-plugin-dirs','--no-cache-dir','--verbose','--simulate','--','syphon-runtime-check:'],capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=45)
 from media_runtime import validate_runtime
 report=validate_runtime(probe.stderr)
 if probe.returncode!=1 or 'Unsupported url scheme' not in probe.stderr or 'syphon_win_x64_py313' not in probe.stderr:raise ValueError('Built tool runtime probe failed')
 verify.write_bytes(probe.stderr.encode())
 notices=work/'notices';notices.mkdir()
 for p in [runtime/'LICENSE.txt',source/'LICENSE',source/'THIRD_PARTY_LICENSES.txt']:
  if p.is_file():shutil.copyfile(p,notices/(('CPython-' if p.parent==runtime else 'yt-dlp-')+p.name))
 for item in manifest['files']:
  if item['kind']!='wheel':continue
  with zipfile.ZipFile(inputs/item['file']) as z:
   for part in z.infolist():
    if not part.is_dir() and any(x in Path(part.filename).name.lower() for x in ['license','copying','notice']):
     name=item['file'].removesuffix('.whl')+'--'+part.filename.replace('/','_');(notices/name).write_bytes(z.read(part))
 record={'schema':1,'variant':manifest['variant'],'sourceRevision':manifest['sourceRevision'],'releaseGitHead':manifest['releaseGitHead'],'buildLockSha256':digest(LOCK),'runtime':report,'output':{'file':'yt-dlp.exe','bytes':exe.stat().st_size,'sha256':digest(exe)},'inputs':manifest['files'],'limits':'Syphon-local build; not an official upstream EXE. Hash-locked inputs do not imply cross-machine bit reproducibility or complete corresponding-source distribution.'}
 (work/'build-record.json').write_bytes((json.dumps(record,indent=2)+'\n').encode());print(json.dumps(record['output']))

if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--inputs',required=True,type=Path);parser.add_argument('--work',required=True,type=Path);args=parser.parse_args()
 try:build(args.inputs,args.work)
 except (ValueError,OSError,subprocess.SubprocessError) as e:raise SystemExit(str(e))
