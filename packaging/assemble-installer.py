from pathlib import Path
import hashlib,json,zipfile,struct,shutil,argparse
parser=argparse.ArgumentParser();parser.add_argument('--output',required=True);parser.add_argument('--version',default='0.1.0-beta.1');args=parser.parse_args()
root=Path(__file__).resolve().parents[1];stage=root/'artifacts/setup-stage';out=Path(args.output).resolve();out.mkdir(parents=True,exist_ok=True)
files=sorted(p for p in stage.rglob('*') if p.is_file() and p.name!='release-manifest.json')
for required in ['app/Syphon.exe','host/Syphon.NativeHost.exe','Uninstall.exe','extensions/manifest.json','app/.tools/media/ffmpeg.exe','app/.tools/media/ffprobe.exe','app/.tools/media/yt-dlp.exe','app/.tools/media/node.exe','prerequisites/MicrosoftEdgeWebview2Setup.exe']:
 assert (stage/required).is_file(),required
manifest={'Version':args.version,'Files':{p.relative_to(stage).as_posix():{'Length':p.stat().st_size,'Sha256':hashlib.file_digest(p.open('rb'),'sha256').hexdigest()} for p in files}}
manifestPath=out/'release-manifest.json';manifestPath.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
payload=out/'payload.zip'
with zipfile.ZipFile(payload,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9,allowZip64=True) as z:
 for p in files:z.write(p,p.relative_to(stage).as_posix())
 z.write(manifestPath,'release-manifest.json')
core=root/'artifacts/setup-core/Syphon.Setup.exe';setup=out/('Syphon-Setup-'+args.version+'-win-x64.exe')
with setup.open('wb') as target,core.open('rb') as source:shutil.copyfileobj(source,target,1048576)
with setup.open('ab') as target,payload.open('rb') as source:
 shutil.copyfileobj(source,target,1048576)
 target.write(struct.pack('<16sqq32s',b'SYPHON-PAYLOAD01',core.stat().st_size,payload.stat().st_size,hashlib.file_digest(payload.open('rb'),'sha256').digest()))
sha=hashlib.file_digest(setup.open('rb'),'sha256').hexdigest();(out/'SHA256SUMS.txt').write_text(sha+'  '+setup.name+'\n')
summary={'installer':str(setup),'bytes':setup.stat().st_size,'MB':round(setup.stat().st_size/1e6,1),'MiB':round(setup.stat().st_size/1048576,1),'installedBytes':sum(p.stat().st_size for p in files),'sha256':sha,'files':len(files),'signed':False,'status':'local evaluation candidate; public release gates documented'}
(out/'build-summary.json').write_text(json.dumps(summary,indent=2));print(json.dumps(summary,indent=2))
