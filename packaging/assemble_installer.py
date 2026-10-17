"""Assemble identical installer bytes from identical core/payload inputs."""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import zipfile
from dependencies import digest, plain_path, verify_file

REQUIRED=('app/Syphon.exe','host/Syphon.NativeHost.exe','Uninstall.exe','extensions/manifest.json','app/.tools/media/ffmpeg.exe','app/.tools/media/ffprobe.exe','app/.tools/media/yt-dlp.exe','app/.tools/media/node.exe','prerequisites/MicrosoftEdgeWebview2Setup.exe')
STAGED_DEPENDENCIES={name:'app/.tools/media/'+name for name in ('ffmpeg.exe','ffprobe.exe','yt-dlp.exe','node.exe')}
STAGED_DEPENDENCIES.update({'FFmpeg-GPLv3.txt':'licenses/FFmpeg-GPLv3.txt','FFmpeg-build-and-source.txt':'licenses/FFmpeg-build-and-source.txt','MicrosoftEdgeWebview2Setup.exe':'prerequisites/MicrosoftEdgeWebview2Setup.exe'})


def assemble(stage,core,out,version,dependency_lock=None):
 stage=plain_path(stage); core=plain_path(core); out=plain_path(out)
 if stage==out or stage in out.parents or out in stage.parents or core==out or out in core.parents: raise ValueError('Installer output must be separate from its stage and core')
 if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[a-zA-Z0-9.]+)?',version): raise ValueError('Invalid release version')
 if not stage.is_dir() or not core.is_file(): raise ValueError('Published stage and setup core are required')
 for required in REQUIRED:
  if not plain_path(stage/required).is_file(): raise ValueError(f'Missing payload file: {required}')
 pinned={}
 if dependency_lock:
  for name,relative in STAGED_DEPENDENCIES.items():
   item=dependency_lock['files'][name]; verify_file(stage/relative,item); pinned[relative]=item
 if digest(stage/'Uninstall.exe')!=digest(core): raise ValueError('Staged uninstaller and setup core are different builds')
 files=[]
 for current,dirs,names in os.walk(stage,followlinks=False):
  for name in dirs+names: plain_path(Path(current)/name)
  files.extend(Path(current)/name for name in names if Path(current)/name!=stage/'release-manifest.json')
 files.sort(key=lambda file:file.relative_to(stage).as_posix())
 manifest={'Version':version,'Files':{p.relative_to(stage).as_posix():{'Length':p.stat().st_size,'Sha256':digest(p)} for p in files}}
 out.mkdir(parents=True,exist_ok=True)
 manifest_path=out/'release-manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2,sort_keys=True)+'\n',encoding='utf-8')
 payload=out/'payload.zip'
 with zipfile.ZipFile(payload,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9,allowZip64=True) as archive:
  for file,name in [(p,p.relative_to(stage).as_posix()) for p in files]+[(manifest_path,'release-manifest.json')]:
   info=zipfile.ZipInfo(name,date_time=(1980,1,1,0,0,0)); info.create_system=3; info.external_attr=0o100644<<16; info.compress_type=zipfile.ZIP_DEFLATED; info._compresslevel=9
   expected=manifest['Files'].get(name)
   written=0; fingerprint=hashlib.sha256()
   with archive.open(info,'w',force_zip64=True) as target,file.open('rb') as source:
    while block:=source.read(1048576): target.write(block); fingerprint.update(block); written+=len(block)
   if expected and (written!=expected['Length'] or fingerprint.hexdigest()!=expected['Sha256']): raise ValueError(f'Staged file changed during assembly: {name}')
   if name in pinned and (written!=pinned[name]['bytes'] or fingerprint.hexdigest()!=pinned[name]['sha256']): raise ValueError(f'Pinned dependency changed during assembly: {name}')
 setup=out/f'Syphon-Setup-{version}-win-x64.exe'
 with setup.open('wb') as target,core.open('rb') as source: shutil.copyfileobj(source,target,1048576)
 if digest(setup)!=manifest['Files']['Uninstall.exe']['Sha256']: raise ValueError('Setup core changed during assembly')
 with setup.open('ab') as target,payload.open('rb') as source:
  shutil.copyfileobj(source,target,1048576)
  target.write(struct.pack('<16sqq32s',b'SYPHON-PAYLOAD01',core.stat().st_size,payload.stat().st_size,bytes.fromhex(digest(payload))))
 sha=digest(setup); (out/'SHA256SUMS.txt').write_text(f'{sha}  {setup.name}\n',encoding='utf-8')
 summary={'installer':str(setup),'bytes':setup.stat().st_size,'MB':round(setup.stat().st_size/1e6,1),'MiB':round(setup.stat().st_size/1048576,1),'installedBytes':sum(p.stat().st_size for p in files),'sha256':sha,'files':len(files),'signed':False,'status':'local evaluation candidate; public release gates documented'}
 (out/'build-summary.json').write_text(json.dumps(summary,indent=2)+'\n',encoding='utf-8')
 return summary
