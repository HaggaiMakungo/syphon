from pathlib import Path
import os,shutil,subprocess,json,hashlib,argparse
from dependencies import DEFAULT_CACHE, LOCK, digest, load_lock, plain_path, verify_dependencies
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument('--version',default='0.1.0-beta.8')
parser.add_argument('--stage',type=Path,default=root/'artifacts/setup-stage')
parser.add_argument('--core',type=Path,default=root/'artifacts/setup-core/Syphon.Setup.exe')
parser.add_argument('--dependency-root',type=Path,default=DEFAULT_CACHE)
args=parser.parse_args()
lock=load_lock();deps=verify_dependencies(args.dependency_root,lock)
stage=plain_path(args.stage);core=plain_path(args.core);app=stage/'app';media=app/'.tools/media'
if not (app/'Syphon.exe').is_file() or not (stage/'host/Syphon.NativeHost.exe').is_file() or not core.is_file():
 raise SystemExit('Publish app, host and setup core into a fresh staging directory first.')
if (stage/'extensions').exists():raise SystemExit('Staging directory was already prepared. Start a new build to avoid stale files.')
if media.exists() and {p.name for p in media.iterdir()}-{'ffmpeg.exe','ffprobe.exe','yt-dlp.exe','node.exe'}:
 raise SystemExit('Unexpected files in the staged media folder.')
media.mkdir(parents=True,exist_ok=True)
def link(source,target):
 source=Path(source);target=Path(target);target.parent.mkdir(parents=True,exist_ok=True)
 if target.exists():
  if digest(target)==digest(source):return
  target.unlink()
 try:os.link(source,target)
 except OSError:shutil.copy2(source,target)
for name in ['ffmpeg.exe','ffprobe.exe','yt-dlp.exe','node.exe']:link(deps[name],media/name)
link(deps['MicrosoftEdgeWebview2Setup.exe'],stage/'prerequisites/MicrosoftEdgeWebview2Setup.exe')
shutil.copy2(root/'packaging/browser-distribution.json',stage/'browser-distribution.json')
licenses=stage/'licenses';licenses.mkdir(exist_ok=True)
for source in (root/'packaging/licenses').glob('*.txt'):shutil.copy2(source,licenses/source.name)
for name in ['FFmpeg-GPLv3.txt','FFmpeg-build-and-source.txt']:shutil.copy2(deps[name],licenses/name)
for folder in ['DesktopAssets','ChartAssets']:
 p=app/folder/'THIRD-PARTY-NOTICES.txt'
 if p.exists():shutil.copy2(p,licenses/(folder+'-THIRD-PARTY-NOTICES.txt'))
# Publish the host separately, but deduplicate identical runtime files on this NTFS
# staging volume. The archive contains ordinary files, never hardlink metadata.
for f in (stage/'host').rglob('*'):
 if f.is_file():
  peer=app/f.relative_to(stage/'host')
  if peer.exists() and f.stat().st_size==peer.stat().st_size and digest(f)==digest(peer):
   f.unlink();link(peer,f)
shutil.copytree(root/'extensions/syphon',stage/'extensions',dirs_exist_ok=True)
link(core,stage/'Uninstall.exe')
provenance={'build':args.version,'dependencyLockSha256':digest(LOCK),'components':{name:dict(lock['files'][name]) for name in ['ffmpeg.exe','ffprobe.exe','yt-dlp.exe','node.exe']},'webview2':dict(lock['files']['MicrosoftEdgeWebview2Setup.exe'],mode='Microsoft-signed pinned Evergreen bootstrapper snapshot')}
(stage/'dependency-provenance.json').write_text(json.dumps(provenance,indent=2,sort_keys=True)+'\n',encoding='utf-8')
(stage/'BROWSER-SETUP.txt').write_text('''Syphon browser setup (beta)\n\nThe installer connects the native host for the included extension identity when Browser integration is enabled. Chrome and Edge still require you to install the extension yourself.\n\nUntil store distribution is available:\n1. Open chrome://extensions or edge://extensions in your browser.\n2. Enable Developer mode, choose Load unpacked, and select the extensions folder beside this file.\n3. Refresh existing video tabs. Open the Syphon extension to enable optional capture/video features.\n\nTo remove it, use the browser extension management page. Syphon uninstall does not change your browser permissions or delete downloaded files.\n\nIf a development checkout previously registered the Syphon host, this installer replaces that registration. Run the development connect-browser.ps1 again only if you intend to switch back to the development copy.\n''',encoding='utf-8')
(stage/'RELEASE-NOTES.txt').write_text('''Syphon 0.1.0-beta.1 - local installer candidate\n\nIncludes the Windows x64 desktop app, private .NET runtimes, native browser host, extension files, FFmpeg, FFprobe, yt-dlp and Node.js. Media tools do not alter system PATH. Microsoft WebView2 is downloaded only if absent; internet is required for that step.\n\nNew downloads using the default root are sorted into Videos, Music, Documents, Programs and Misc. Explicit custom destinations are preserved.\n\nKnown limits: media jobs restart on resume; ordinary-file bandwidth caps do not cover media subprocesses; source support varies; no DRM bypass or live recording. Browser store distribution is not configured. This installer is unsigned and intended for local evaluation, not a certified public release.\n\nThird-party licences are in licenses. Public redistribution remains gated on corresponding-source obligations for the exact GPL media binaries and their dependencies, signing, and clean-machine acceptance. Local SHA-256 checks detect corruption, not publisher authenticity.\n''',encoding='utf-8')
print('Prepared bundled files:',sum(1 for p in stage.rglob('*') if p.is_file()))

notes=stage/'RELEASE-NOTES.txt';notes.write_text(notes.read_text(encoding='utf-8').replace('0.1.0-beta.1',args.version),encoding='utf-8')

(stage/'extensions'/'README.txt').write_text((stage/'BROWSER-SETUP.txt').read_text(encoding='utf-8'),encoding='utf-8')

notes=stage/'RELEASE-NOTES.txt';notes.write_text(notes.read_text(encoding='utf-8')+"\nThis all-user edition installs inside Program Files and requests administrator approval. Launch Syphon normally from the Start menu after setup. Download data stays per user. Older per-user native-host overrides are repaired for the account running setup; other Windows accounts with older beta registrations may need to remove their own override.\n",encoding='utf-8')
