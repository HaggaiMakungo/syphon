"""Import and verify exact release inputs. This never resolves 'latest' downloads."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import tempfile

ROOT = Path(__file__).resolve().parents[1]
LOCK = Path(__file__).with_name('dependencies.win-x64.json')
DEFAULT_CACHE = ROOT / '.tools/release-deps/win-x64'
FILE_NAMES = ('ffmpeg.exe', 'ffprobe.exe', 'yt-dlp.exe', 'node.exe', 'FFmpeg-GPLv3.txt', 'FFmpeg-build-and-source.txt', 'MicrosoftEdgeWebview2Setup.exe')


def plain_path(path):
 path=Path(path).absolute()
 for part in (path,*path.parents):
  if part.is_symlink(): raise ValueError(f'Release input cannot be a link: {part}')
  try: attrs=part.stat(follow_symlinks=False)
  except FileNotFoundError: continue
  if getattr(attrs,'st_file_attributes',0)&0x400: raise ValueError(f'Release input cannot be a reparse point: {part}')
 return path


def digest(path):
 with Path(path).open('rb') as source: return hashlib.file_digest(source,'sha256').hexdigest()


def load_lock(path=LOCK):
 data=json.loads(Path(path).read_text(encoding='utf-8-sig'))
 if data.get('schema')!=1 or data.get('architecture')!='win-x64' or set(data.get('files',{}))!=set(FILE_NAMES):
  raise ValueError('Unsupported or incomplete Windows dependency lock')
 for name,item in data['files'].items():
  if not re.fullmatch('[0-9a-f]{64}',item.get('sha256','')) or type(item.get('bytes')) is not int or item['bytes']<=0:
   raise ValueError(f'Invalid checksum/length for {name}')
  if not isinstance(item.get('version'),str) or not item['version'] or not isinstance(item.get('source'),str) or not item['source'].startswith('https://'):
   raise ValueError(f'Missing version/source provenance for {name}')
 return data


def verify_file(path,item):
 path=plain_path(path)
 if not path.is_file(): raise ValueError(f'Missing pinned dependency: {path}')
 if path.stat().st_size!=item['bytes'] or digest(path)!=item['sha256']:
  raise ValueError(f'Pinned dependency differs from the reviewed lock: {path.name}')
 return path


def verify_dependencies(root,lock=None):
 lock=lock or load_lock(); root=plain_path(root)
 if not root.is_dir(): raise ValueError(f'Dependency cache not found: {root}. Import the pinned inputs with packaging/dependencies.py import.')
 if {p.name for p in root.iterdir()}!=set(lock['files']): raise ValueError('Dependency cache contains missing or unexpected files; use a dedicated release cache.')
 return {name:verify_file(root/name,item) for name,item in lock['files'].items()}


def import_dependencies(sources,destination,lock=None):
 lock=lock or load_lock()
 if set(sources)!=set(lock['files']): raise ValueError('Supply every pinned dependency and no additional files')
 # Verify all source inputs before creating/changing the destination.
 verified={name:verify_file(path,lock['files'][name]) for name,path in sources.items()}
 destination=plain_path(destination)
 if destination.exists() and {p.name for p in destination.iterdir()}-set(lock['files']): raise ValueError('Import destination contains unrelated files')
 destination.mkdir(parents=True,exist_ok=True)
 for name,source in verified.items():
  target=destination/name; plain_path(target)
  if target.exists() and target.stat().st_size==lock['files'][name]['bytes'] and digest(target)==lock['files'][name]['sha256']: continue
  fd,temporary=tempfile.mkstemp(prefix='.import-',dir=destination); os.close(fd)
  try:
   shutil.copyfile(source,temporary); verify_file(temporary,lock['files'][name]); os.replace(temporary,target)
  finally:
   if Path(temporary).exists(): Path(temporary).unlink()
 return verify_dependencies(destination,lock)


def main():
 parser=argparse.ArgumentParser(description=__doc__)
 parser.add_argument('action',choices=['import','verify'])
 parser.add_argument('--root',type=Path,default=DEFAULT_CACHE)
 parser.add_argument('--lock',type=Path,default=LOCK)
 parser.add_argument('--ffmpeg-directory',type=Path,help='Extracted FFmpeg distribution containing bin/, LICENSE and README.txt')
 parser.add_argument('--node',type=Path)
 parser.add_argument('--yt-dlp',type=Path)
 parser.add_argument('--webview2',type=Path)
 args=parser.parse_args(); lock=load_lock(args.lock)
 if args.action=='import':
  if any(value is None for value in (args.ffmpeg_directory,args.node,args.yt_dlp,args.webview2)): parser.error('Import requires --ffmpeg-directory, --node, --yt-dlp and --webview2')
  sources={'ffmpeg.exe':args.ffmpeg_directory/'bin/ffmpeg.exe','ffprobe.exe':args.ffmpeg_directory/'bin/ffprobe.exe','FFmpeg-GPLv3.txt':args.ffmpeg_directory/'LICENSE','FFmpeg-build-and-source.txt':args.ffmpeg_directory/'README.txt','node.exe':args.node,'yt-dlp.exe':args.yt_dlp,'MicrosoftEdgeWebview2Setup.exe':args.webview2}
  import_dependencies(sources,args.root,lock)
 else: verify_dependencies(args.root,lock)
 print(f'Verified {len(lock["files"])} pinned inputs in {args.root}')


if __name__=='__main__':
 try: main()
 except (ValueError,OSError) as error: raise SystemExit(str(error))
