"""Build Syphon FFmpeg/FFprobe offline from a closed, hash-locked input set.

Use a new dedicated work directory. No downloads, global installations or
release-cache updates occur here. Windows x64 only.
"""
import argparse,hashlib,json,os,shutil,subprocess,tarfile,zipfile
from pathlib import Path,PurePosixPath
from dependencies import plain_path,verify_file,digest

LOCK=Path(__file__).with_name('ffmpeg-build.win-x64.json')
FFMPEG_FLAGS=['--target-os=mingw32','--arch=x86_64','--cc=gcc','--cxx=g++',
 '--disable-autodetect','--enable-schannel','--enable-zlib','--enable-libmp3lame',
 '--enable-libx264','--enable-libxml2','--enable-libwebp','--enable-gpl',
 '--enable-version3','--disable-debug','--disable-doc','--disable-ffplay',
 '--disable-shared','--enable-static','--extra-ldflags=-static',
 '--extra-version=syphon-source-build']

def member(name,seen,directory=False):
 p=PurePosixPath(name)
 if not name or '\\' in name or ':' in name or p.is_absolute() or '..' in p.parts:
  raise ValueError('Unsafe archive member: '+name)
 if any(part.endswith((' ','.')) or part.split('.')[0].lower() in {'con','prn','aux','nul',*[f'com{i}' for i in range(1,10)],*[f'lpt{i}' for i in range(1,10)]} for part in p.parts):
  raise ValueError('Windows-ambiguous archive member: '+name)
 key=str(p).casefold()
 if key in seen and not (directory and seen[key]):raise ValueError('Duplicate archive member: '+name)
 seen[key]=directory
 return Path(*p.parts)

def unpack(archive,target):
 target=plain_path(target)
 if target.exists():raise ValueError('Extraction requires a new directory')
 seen={}
 if zipfile.is_zipfile(archive):
  with zipfile.ZipFile(archive) as z:
   items=[]
   for item in z.infolist():
    name=member(item.filename,seen,item.is_dir())
    if ((item.external_attr>>16)&0o170000) not in (0,0o100000,0o040000):raise ValueError('Archive link/special file')
    items.append((item,name))
   target.mkdir(parents=True)
   for item,name in items:
    if item.is_dir():continue
    out=target/name;plain_path(out);out.parent.mkdir(parents=True,exist_ok=True)
    with z.open(item) as src,out.open('wb') as dst:shutil.copyfileobj(src,dst)
 else:
  with tarfile.open(archive) as t:
   items=[]
   for item in t:
    name=member(item.name,seen,item.isdir())
    if not(item.isdir() or item.isfile()):raise ValueError('Archive link/special file')
    items.append((item,name))
   target.mkdir(parents=True)
   for item,name in items:
    if item.isdir():continue
    out=target/name;plain_path(out);out.parent.mkdir(parents=True,exist_ok=True)
    with t.extractfile(item) as src,out.open('wb') as dst:shutil.copyfileobj(src,dst)

def preflight(lock,inputs,work):
 inputs=plain_path(inputs);work=plain_path(work)
 if work.exists():raise ValueError('Use a new dedicated build work directory')
 if inputs==work or inputs in work.parents or work in inputs.parents:raise ValueError('Inputs/work must be separate')
 names={}
 for item in lock['files']:
  name=member(item['file'],names)
  if len(name.parts)!=1:raise ValueError('Input names must be flat')
  verify_file(inputs/name,item)
 return inputs,work

# Only shell condition syntax changes; the encoder source is unmodified.
X264_REPLACEMENTS={
 '[[ "$arg" = -falign-loops* ]]':('syphon_match "$arg" \'-falign-loops*\'',1),
 '[[ "$arg" = -mpreferred-stack-boundary* ]]':('syphon_match "$arg" \'-mpreferred-stack-boundary*\'',1),
 '[[ "$arg" = -l* ]]':('syphon_match "$arg" \'-l*\'',1),
 '[[ "$arg" = -L* ]]':('syphon_match "$arg" \'-L*\'',1),
 '[[ "$arg" = -Qdiag-error* ]]':('syphon_match "$arg" \'-Qdiag-error*\'',1),
 '[[ $path != "$base/"* ]]':('! syphon_match "$path" "$base/"\'*\'',1),
 '[[ $host_os = mingw* || $host_os = msys* || $host_os = cygwin* ]]':('syphon_match "$host_os" \'mingw*\' || syphon_match "$host_os" \'msys*\' || syphon_match "$host_os" \'cygwin*\'',1),
 '[[ "$cc_base" = icl || "$cc_base" = icl[\\ .]* ]]':('syphon_match "$cc_base" \'icl\' || syphon_match "$cc_base" \'icl[ .]*\'',1),
 '[[ $host_os = cygwin* ]]':('syphon_match "$host_os" \'cygwin*\'',2),
 '[[ "$cc_base" = cl || "$cc_base" = cl[\\ .]* ]]':('syphon_match "$cc_base" \'cl\' || syphon_match "$cc_base" \'cl[ .]*\'',1),
 '[[ "$cc_base" = icc || "$cc_base" = icc[\\ .]* ]]':('syphon_match "$cc_base" \'icc\' || syphon_match "$cc_base" \'icc[ .]*\'',1),
 '[[ "$asm" == auto && "$CFLAGS" != *-march* ]]':('[ "$asm" = auto ] && ! syphon_match "$CFLAGS" \'*-march*\'',1),
 '[[ "$asm" == auto && "$CFLAGS" != *-mfpmath* ]]':('[ "$asm" = auto ] && ! syphon_match "$CFLAGS" \'*-mfpmath*\'',1),
 '[[ "$ASFLAGS" != *"-DPIC"* ]]':('! syphon_match "$ASFLAGS" \'*-DPIC*\'',1),
 '[[ -z "$bashcompletionsdir" && "$prefix" != "/usr" && "$prefix" != "/usr/"* ]]':('[ -z "$bashcompletionsdir" ] && [ "$prefix" != /usr ] && ! syphon_match "$prefix" \'/usr/*\'',1),
}

def portable_x264(text):
 if not text.startswith('#!/bin/bash\n'):raise ValueError('Unexpected x264 configure shell')
 for old,(new,count) in X264_REPLACEMENTS.items():
  if text.count(old)!=count:raise ValueError('Unexpected x264 condition: '+old)
  text=text.replace(old,new)
 if '[[' in text:raise ValueError('Unconverted x264 Bash condition')
 return text.replace('#!/bin/bash\n', '#!/bin/sh\n# Syphon portable-shell patch\nsyphon_match() { case "$1" in $2) return 0;; *) return 1;; esac; }\n',1)

def lame_configure(prefix):
 return ['./configure','--prefix='+prefix.as_posix(),'--build=x86_64-w64-mingw32',
  '--host=x86_64-w64-mingw32','--disable-shared','--enable-static',
  '--disable-frontend','--disable-nasm']

def zlib_static_pc(text):
 # zlib1.3.2 installs libzs.a on Windows but its .pc still requests -lz.
 if text.count(' -lz\n')!=1:raise ValueError('Unexpected zlib pkg-config template')
 return text.replace(' -lz\n',' -lzs\n')

def lame_install(binary):
 return ['make','install','INSTALL='+(binary/'install.exe').as_posix()+' -c']

def ffmpeg_configure(prefix):
 flags=[x for x in FFMPEG_FLAGS if not x.startswith('--extra-ldflags=')]
 return ['./configure','--prefix='+prefix.as_posix(),*flags,
  '--pkg-config-flags=--static','--extra-cflags=-I'+(prefix/'include').as_posix(),
  '--extra-ldflags=-static -L'+(prefix/'lib').as_posix()]

def command_path(name,binary):
 if name not in ('cmake','make'):raise ValueError('Unknown private build tool: '+name)
 return str(binary/(name+'.exe'))

def build(inputs,work,jobs=2):
 lock=json.loads(LOCK.read_text(encoding='utf-8'));inputs,work=preflight(lock,inputs,work)
 if os.name!='nt':raise ValueError('Windows native build only')
 if not 1<=jobs<=32:raise ValueError('Invalid job count')
 work.mkdir(parents=True)
 # Windows tar supports the verified 7z SFX without executing its extractor.
 tool=next(x for x in lock['files'] if x.get('kind')=='toolchain')
 archive=inputs/tool['file']
 listing=subprocess.check_output([str(Path(os.environ['SystemRoot'])/'System32/tar.exe'),'-tf',str(archive)],text=True)
 seen={}
 for line in listing.splitlines():member(line,seen,line.endswith("/"))
 tools=work/'tools';tools.mkdir()
 subprocess.run([str(Path(os.environ['SystemRoot'])/'System32/tar.exe'),'-xf',str(archive),'-C',str(tools)],check=True)
 nasm=next(x for x in lock['files'] if x.get('kind')=='nasm-binary')
 unpack(inputs/nasm['file'],work/'nasm')
 source=work/'sources';source.mkdir()
 dirs={}
 for item in lock['files']:
  if item.get('kind')!='library':continue
  dest=source/item['component'];unpack(inputs/item['file'],dest)
  roots=list(dest.iterdir())
  if len(roots)!=1 or not roots[0].is_dir():raise ValueError('Unexpected source root')
  dirs[item['component']]=roots[0]
 compile_sources(lock,work,dirs,jobs)

def native_environment(binary,nasm,prefix,epoch):
 env={k:v for k,v in os.environ.items() if k.upper() in {'SYSTEMROOT','WINDIR','COMSPEC','TEMP','TMP','NUMBER_OF_PROCESSORS'}}
 env.update(PATH=binary.as_posix()+';'+nasm.as_posix()+';'+Path(os.environ['SystemRoot']).as_posix()+'/System32',
  PATH_SEPARATOR=';',ac_executable_extensions='.exe',CONFIG_SHELL=(binary/'sh.exe').as_posix(),PKG_CONFIG_PATH='',PKG_CONFIG_LIBDIR=prefix.as_posix()+'/lib/pkgconfig',
  CFLAGS='-O2',CXXFLAGS='-O2',LDFLAGS='-static',LC_ALL='C',TZ='UTC',SOURCE_DATE_EPOCH=str(epoch))
 env.update(TMPDIR=(prefix.parent/'tmp').as_posix(),TMP=(prefix.parent/'tmp').as_posix(),TEMP=(prefix.parent/'tmp').as_posix())
 return env

def capture_recipe(work,lock):
 recipe=Path(__file__).read_bytes();helper=Path(__file__).with_name('dependencies.py').read_bytes()
 identity={'recipeSha256':hashlib.sha256(recipe).hexdigest(),
  'buildLockSha256':hashlib.sha256((json.dumps(lock,sort_keys=True,separators=(',',':'))+'\n').encode()).hexdigest(),
  'dependenciesSha256':hashlib.sha256(helper).hexdigest()}
 target=work/'recipes'/identity['recipeSha256'];target.mkdir(parents=True,exist_ok=False)
 (target/'build_ffmpeg.py').write_bytes(recipe);(target/'dependencies.py').write_bytes(helper)
 (target/'ffmpeg-build.win-x64.json').write_text(json.dumps(lock,sort_keys=True,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
 return identity

def ffmpeg_make(jobs):
 return ['make','V=1','-j'+str(jobs)]

def compile_sources(lock,work,dirs,jobs,start='zlib'):
 stages=['zlib','libxml2','libwebp','lame','x264','ffmpeg']
 if start not in stages:raise ValueError('Unknown build stage')
 tools=work/'tools'
 prefix=work/'prefix';prefix.mkdir(exist_ok=True);binary=tools/'w64devkit/bin'
 (work/'tmp').mkdir(exist_ok=True)
 env=native_environment(binary,work/'nasm/nasm-3.02',prefix,lock['sourceDateEpoch'])
 shell=binary/'sh.exe'
 identity=capture_recipe(work,lock)
 log=(work/'build.log').open('a',encoding='utf-8')
 commands=json.loads((work/'commands.json').read_text(encoding='utf-8')) if (work/'commands.json').exists() else []
 def run(component,args):
  if stages.index(component)<stages.index(start):return
  command=[str(x) for x in args]
  if command[0] in ('cmake','make'):command[0]=command_path(command[0],binary)
  commands.append({'component':component,'argv':command})
  log.write('\nRUN '+component+' '+repr(command)+'\n');log.flush()
  (work/'commands.json').write_text(json.dumps(commands,indent=2)+'\n',encoding='utf-8')
  subprocess.run(command,cwd=dirs[component],env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
 def cmake(component,options):
  run(component,['cmake','-S','.','-B','build','-G','MinGW Makefiles',
   '-DCMAKE_MAKE_PROGRAM='+str(binary/'make.exe'),'-DCMAKE_BUILD_TYPE=Release',
   '-DCMAKE_INSTALL_PREFIX='+prefix.as_posix(),'-DBUILD_SHARED_LIBS=OFF',*options])
  run(component,['cmake','--build','build','--parallel',str(jobs)])
  run(component,['cmake','--install','build'])
 try:
  cmake('zlib',['-DZLIB_BUILD_TESTING=OFF','-DZLIB_BUILD_SHARED=OFF'])
  pc=prefix/'lib/pkgconfig/zlib.pc'
  if start=='zlib':pc.write_text(zlib_static_pc(pc.read_text(encoding='utf-8')),encoding='utf-8')
  cmake('libxml2',['-DLIBXML2_WITH_PYTHON=OFF','-DLIBXML2_WITH_PROGRAMS=OFF','-DLIBXML2_WITH_TESTS=OFF','-DLIBXML2_WITH_ICONV=OFF','-DLIBXML2_WITH_ICU=OFF','-DLIBXML2_WITH_LZMA=OFF'])
  cmake('libwebp',['-DWEBP_BUILD_ANIM_UTILS=OFF','-DWEBP_BUILD_CWEBP=OFF','-DWEBP_BUILD_DWEBP=OFF','-DWEBP_BUILD_GIF2WEBP=OFF','-DWEBP_BUILD_IMG2WEBP=OFF','-DWEBP_BUILD_VWEBP=OFF','-DWEBP_BUILD_WEBPINFO=OFF','-DWEBP_BUILD_WEBPMUX=OFF','-DWEBP_BUILD_EXTRAS=OFF'])
  run('lame',[shell,*lame_configure(prefix)])
  run('lame',['make','-j'+str(jobs)]);run('lame',lame_install(binary))
  import difflib
  patches=work/'patches';patches.mkdir(exist_ok=True)
  config=dirs['x264']/'configure';before=config.read_text(encoding='utf-8')
  saved=patches/'x264-configure.patched'
  if not saved.exists():
   after=portable_x264(before)
   (patches/'x264-configure.original').write_text(before,encoding='utf-8')
   saved.write_text(after,encoding='utf-8')
   (patches/'x264-configure.patch').write_text(''.join(difflib.unified_diff(before.splitlines(True),after.splitlines(True),fromfile='a/configure',tofile='b/configure')),encoding='utf-8')
   config.write_text(after,encoding='utf-8')
  elif before!=saved.read_text(encoding='utf-8'):raise ValueError('x264 patched configure differs from retained patch')
  run('x264',[shell,'./configure','--host=x86_64-w64-mingw32','--prefix='+prefix.as_posix(),'--enable-static','--disable-cli','--disable-opencl','--extra-ldflags=-static'])
  run('x264',['make','-j'+str(jobs)]);run('x264',['make','install'])
  run('ffmpeg',[shell,*ffmpeg_configure(prefix)])
  run('ffmpeg',ffmpeg_make(jobs))
 finally:log.close()
 finish_candidate(lock,work,dirs,identity)

def finish_candidate(lock,work,dirs,identity):
 out=work/'output';out.mkdir()
 for name in ['ffmpeg.exe','ffprobe.exe']:shutil.copyfile(dirs['ffmpeg']/name,out/name)
 record={'schema':1,'variant':'syphon-source-build',**identity,'sourceDateEpoch':lock['sourceDateEpoch'],
  'inputs':lock['files'],'outputs':[{'file':p.name,'bytes':p.stat().st_size,'sha256':digest(p)} for p in out.iterdir()],
  'limits':'Local native source build; compatibility review and source distribution inventory required separately. No cross-machine byte reproducibility claimed.'}
 for name in ['ffmpeg','ffprobe']:
  probe=subprocess.run([str(out/(name+'.exe')),'-version'],capture_output=True,text=True,check=True)
  (work/(name+'-version.txt')).write_text(probe.stdout,encoding='utf-8')
 (work/'build-record.json').write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8')
 print(json.dumps(record['outputs']))

if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--inputs',type=Path,required=True);parser.add_argument('--work',type=Path,required=True);parser.add_argument('--jobs',type=int,default=2);args=parser.parse_args()
 try:build(args.inputs,args.work,args.jobs)
 except (ValueError,OSError,subprocess.SubprocessError) as e:raise SystemExit(str(e))
