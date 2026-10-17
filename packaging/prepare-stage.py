from pathlib import Path
import os,shutil,subprocess,json,hashlib,argparse
parser=argparse.ArgumentParser();parser.add_argument('--version',default='0.1.0-beta.6');args=parser.parse_args()
root=Path(__file__).resolve().parents[1];stage=root/'artifacts/setup-stage';app=stage/'app';media=app/'.tools/media';media.mkdir(parents=True,exist_ok=True)
def link(source,target):
 source=Path(source);target=Path(target);target.parent.mkdir(parents=True,exist_ok=True)
 if target.exists():
  if hashlib.sha256(target.read_bytes()).digest()==hashlib.sha256(source.read_bytes()).digest():return
  target.unlink()
 try:os.link(source,target)
 except OSError:shutil.copy2(source,target)
for name in ['ffmpeg','ffprobe']:link(Path('C:/ffmpeg/bin')/(name+'.exe'),media/(name+'.exe'))
link(root/'.tools/media/yt-dlp.exe',media/'yt-dlp.exe');link(Path('C:/Program Files/nodejs/node.exe'),media/'node.exe')
shutil.copy2(root/'packaging/browser-distribution.json',stage/'browser-distribution.json')
licenses=stage/'licenses';licenses.mkdir(exist_ok=True)
for source in (root/'packaging/licenses').glob('*.txt'):shutil.copy2(source,licenses/source.name)
shutil.copy2('C:/ffmpeg/LICENSE',licenses/'FFmpeg-GPLv3.txt');shutil.copy2('C:/ffmpeg/README.txt',licenses/'FFmpeg-build-and-source.txt')
for folder in ['DesktopAssets','ChartAssets']:
 p=app/folder/'THIRD-PARTY-NOTICES.txt'
 if p.exists():shutil.copy2(p,licenses/(folder+'-THIRD-PARTY-NOTICES.txt'))
# Publish the host separately, but deduplicate identical runtime files on this NTFS
# staging volume. The archive contains ordinary files, never hardlink metadata.
for f in (stage/'host').rglob('*'):
 if f.is_file():
  peer=app/f.relative_to(stage/'host')
  if peer.exists() and f.stat().st_size==peer.stat().st_size and hashlib.sha256(f.read_bytes()).digest()==hashlib.sha256(peer.read_bytes()).digest():
   f.unlink();link(peer,f)
shutil.copytree(root/'extensions/syphon',stage/'extensions',dirs_exist_ok=True)
link(root/'artifacts/setup-core/Syphon.Setup.exe',stage/'Uninstall.exe')
provenance={'build':args.version,'components':{}}
for f in media.glob('*.exe'):
 provenance['components'][f.name]={'bytes':f.stat().st_size,'sha256':hashlib.file_digest(f.open('rb'),'sha256').hexdigest()}
provenance['components']['ffmpeg.exe']['version']='2026-06-01-git-bf608f16fd-full_build-www.gyan.dev'
provenance['components']['ffmpeg.exe']['upstream']='https://www.gyan.dev/ffmpeg/builds/'
provenance['components']['ffprobe.exe']['version']=provenance['components']['ffmpeg.exe']['version']
provenance['components']['node.exe']['version']=subprocess.check_output([str(media/'node.exe'),'--version'],text=True).strip()
record=json.loads((root/'.tools/media/yt-dlp-install.json').read_text(encoding='utf-8-sig'));assert provenance['components']['yt-dlp.exe']['sha256']==record['sha256'];provenance['components']['yt-dlp.exe'].update(record)
bootstrap=stage/'prerequisites/MicrosoftEdgeWebview2Setup.exe';provenance['webview2']={'source':'https://go.microsoft.com/fwlink/p/?LinkId=2124703','sha256':hashlib.file_digest(bootstrap.open('rb'),'sha256').hexdigest(),'mode':'Microsoft-signed online Evergreen bootstrapper'}
(stage/'dependency-provenance.json').write_text(json.dumps(provenance,indent=2))
(stage/'BROWSER-SETUP.txt').write_text('''Syphon browser setup (beta)\n\nThe installer connects the native host for the included extension identity when Browser integration is enabled. Chrome and Edge still require you to install the extension yourself.\n\nUntil store distribution is available:\n1. Open chrome://extensions or edge://extensions in your browser.\n2. Enable Developer mode, choose Load unpacked, and select the extensions folder beside this file.\n3. Refresh existing video tabs. Open the Syphon extension to enable optional capture/video features.\n\nTo remove it, use the browser extension management page. Syphon uninstall does not change your browser permissions or delete downloaded files.\n\nIf a development checkout previously registered the Syphon host, this installer replaces that registration. Run the development connect-browser.ps1 again only if you intend to switch back to the development copy.\n''')
(stage/'RELEASE-NOTES.txt').write_text('''Syphon 0.1.0-beta.1 - local installer candidate\n\nIncludes the Windows x64 desktop app, private .NET runtimes, native browser host, extension files, FFmpeg, FFprobe, yt-dlp and Node.js. Media tools do not alter system PATH. Microsoft WebView2 is downloaded only if absent; internet is required for that step.\n\nNew downloads using the default root are sorted into Videos, Music, Documents, Programs and Misc. Explicit custom destinations are preserved.\n\nKnown limits: media jobs restart on resume; ordinary-file bandwidth caps do not cover media subprocesses; source support varies; no DRM bypass or live recording. Browser store distribution is not configured. This installer is unsigned and intended for local evaluation, not a certified public release.\n\nThird-party licences are in licenses. Public redistribution remains gated on corresponding-source obligations for the exact GPL media binaries and their dependencies, signing, and clean-machine acceptance. Local SHA-256 checks detect corruption, not publisher authenticity.\n''')
print('Prepared bundled files:',sum(1 for p in stage.rglob('*') if p.is_file()))

notes=stage/'RELEASE-NOTES.txt';notes.write_text(notes.read_text().replace('0.1.0-beta.1',args.version))

(stage/'extensions'/'README.txt').write_text((stage/'BROWSER-SETUP.txt').read_text())

notes=stage/'RELEASE-NOTES.txt';notes.write_text(notes.read_text()+"\nThis all-user edition installs inside Program Files and requests administrator approval. Launch Syphon normally from the Start menu after setup. Download data stays per user. Older per-user native-host overrides are repaired for the account running setup; other Windows accounts with older beta registrations may need to remove their own override.\n")
