"""Inventory source-material gaps for the pinned media tools. Not a compliance certificate."""
import argparse
import json
from pathlib import Path
import re
import shutil
from dependencies import DEFAULT_CACHE, LOCK, ROOT, digest, load_lock, plain_path, verify_dependencies, verify_file


def ffmpeg_versions(text):
 marker="git-full external libraries' versions:"
 if text.count(marker)!=1: raise ValueError('Expected one vendor external-library version section')
 records=[]; seen=set()
 for line in text.split(marker,1)[1].strip().splitlines():
  if not line.strip(): continue
  fields=line.strip().split(maxsplit=1)
  if len(fields)!=2 or fields[0].casefold() in seen: raise ValueError('Missing or duplicate vendor library version')
  name,version=fields;seen.add(name.casefold())
  unresolved=bool(re.search(r'\b(latest|master|main|unknown|head)\b',version,re.I))
  records.append({'name':name,'versionReported':version,'identityStatus':'unresolved' if unresolved else 'vendor-reported-only','sourceStatus':'not-collected'})
 if not records: raise ValueError('Vendor dependency version list is empty')
 return records


def third_party_notices(text):
 records=[];seen=set()
 for name,license_name,url in re.findall(r'^([^\n|]+) \| ([^\n]+)\nURL: (https?://\S+)',text.replace('\r\n','\n'),re.M):
  name=name.strip()
  if name.casefold() in seen:raise ValueError('Duplicate third-party notice header')
  seen.add(name.casefold());records.append({'name':name,'licenseReported':license_name.strip(),'homepage':url,'version':None,'identityStatus':'version-not-established','sourceStatus':'not-collected'})
 if not records:raise ValueError('No third-party component notice headers found')
 return records


def verify_materials(root,lock_hash):
 root=plain_path(root);manifest=plain_path(root/'source-materials.json')
 data=json.loads(manifest.read_text(encoding='utf-8-sig'))
 if data.get('schema')!=1 or data.get('dependencyLockSha256')!=lock_hash:raise ValueError('Source evidence is not tied to this dependency lock')
 records=data.get('sources')
 if not isinstance(records,list):raise ValueError('Missing source evidence records')
 result=[];seen=set()
 auxiliary=list(data.get('recipeEvidence',[]))
 for key in ('checksums','binaryInspection'):
  if data.get(key):auxiliary.append(data[key])
 for item in records+auxiliary:
  name=item.get('file','')
  if not name or name in {'.','..'} or '/' in name or '\\' in name or ':' in name or name in seen:raise ValueError('Unsafe or duplicate source evidence filename')
  if type(item.get('bytes')) is not int or item['bytes']<=0 or not re.fullmatch('[0-9a-f]{64}',item.get('sha256','')):raise ValueError('Invalid source evidence checksum')
  verify_file(root/name,item);seen.add(name)
  if item in records:result.append({**item,'status':'present-file-hash-matched','coverage':'partial; exact vendor/bundled dependency build coverage not established'})
 return result


def make_inventory(lock,lock_hash,vendor_text,notices_text,materials,recipes=None):
 vendor=ffmpeg_versions(vendor_text);notices=third_party_notices(notices_text)
 return {
  'schema':1,'scope':'Media-tool source-material triage; not a complete application SBOM or legal approval',
  'dependencyLockSha256':lock_hash,'sourceDistributionComplete':False,
  'binaryInputs':{name:item for name,item in lock['files'].items() if name.endswith('.exe')},
  'ffmpegExternalLibraries':vendor,'ytDlpThirdPartyNotices':notices,'sourceArchives':materials,'upstreamRecipeEvidence':recipes or [],
  'gaps':[
   'Exact FFmpeg vendor build recipe, toolchain, patches and linked-library source archives are not established.',
   'Vendor-reported versions are not verified source revisions; '+str(sum(r['identityStatus']=='unresolved' for r in vendor))+' FFmpeg entries have unresolved identities.',
   'The yt-dlp notices do not establish exact binary component versions. Tagged recipe/requirement evidence is retained when supplied, but exact build inputs and Python/PyInstaller provenance still require reconciliation.',
   'The yt-dlp release source alone does not include the whole bundled executable dependency source set.',
   'Node/.NET/native/frontend/font notice and redistribution review is outside this media-source collection pass.',
   'Syphon publisher licensing and the final public source-distribution location are still decisions to confirm.'
  ],
  'references':[
   'https://www.ffmpeg.org/legal.html',
   'https://www.gyan.dev/ffmpeg/builds/',
   'https://github.com/yt-dlp/yt-dlp/blob/'+lock['files']['yt-dlp.exe']['version']+'/README.md#licensing'
  ]}


def cell(value):return str(value).replace('|','\\|').replace('\r',' ').replace('\n',' ')


def markdown(report):
 lines=['# Pinned media source-material audit','',report['scope'],'',
  '**Status: incomplete. Collected core sources do not close the external/bundled dependency gaps.**','',
  'Dependency lock SHA-256: `'+report['dependencyLockSha256']+'`','',
  '## Retained source archives','', '| Component | File | SHA-256 |','|---|---|---|']
 for item in report['sourceArchives']:lines.append('| '+cell(item.get('component','source evidence'))+' | '+cell(item['file'])+' | '+item['sha256']+' |')
 if not report['sourceArchives']:lines.append('| None verified locally | — | — |')
 lines+=['','A matching file hash establishes retained bytes, not completeness of source/build inputs or publisher authenticity. The source evidence manifest retains the retrieval and verification notes.','',
  '## FFmpeg vendor-reported external libraries','', '| Library | Reported version | Identity status |','|---|---|---|']
 lines += ['| '+cell(r['name'])+' | '+cell(r['versionReported'])+' | '+r['identityStatus']+' |' for r in report['ffmpegExternalLibraries']]
 lines+=['','## yt-dlp bundled notice inventory','', 'These are notice entries, not proof of exact versions inside the executable. License identifiers are transcribed from the packaged notices; no licensing conclusion for Syphon is inferred.','', '| Component | Reported license | Exact version |','|---|---|---|']
 lines+=['| '+cell(r['name'])+' | '+cell(r['licenseReported'])+' | Not established |' for r in report['ytDlpThirdPartyNotices']]
 lines+=['','## Remaining gaps','']+['- '+gap for gap in report['gaps']]
 if report['upstreamRecipeEvidence']:
  lines+=['','## Retained upstream recipe evidence','', 'These files come from the resolved yt-dlp source commit. Requirement pins and workflow matrices narrow the investigation; they are not treated as proof of every component inside the compiled EXE.','']
  lines+=['- '+cell(item['file'])+' — `'+item['sha256']+'`' for item in report['upstreamRecipeEvidence']]
 lines+=['','## Upstream references','']+['- '+url for url in report['references']]
 return '\n'.join(lines)+'\n'


def main():
 parser=argparse.ArgumentParser(description=__doc__)
 parser.add_argument('--dependency-root',type=Path,default=DEFAULT_CACHE)
 parser.add_argument('--source-materials',type=Path)
 parser.add_argument('--output',type=Path,required=True)
 args=parser.parse_args();lock=load_lock();cache=verify_dependencies(args.dependency_root,lock);lock_hash=digest(LOCK)
 materials=verify_materials(args.source_materials,lock_hash) if args.source_materials else []
 details=json.loads((args.source_materials/'source-materials.json').read_text(encoding='utf-8-sig')) if args.source_materials else {}
 out=plain_path(args.output);dep=plain_path(args.dependency_root)
 if out==dep or out in dep.parents or dep in out.parents:raise ValueError('Audit output must be separate from the dependency cache')
 if args.source_materials and (out==plain_path(args.source_materials) or out in plain_path(args.source_materials).parents):raise ValueError('Audit output must not replace the source-materials directory')
 report=make_inventory(lock,lock_hash,cache['FFmpeg-build-and-source.txt'].read_text(encoding='utf-8'),(ROOT/'packaging/licenses/yt-dlp-THIRD-PARTY.txt').read_text(encoding='utf-8'),materials,details.get('recipeEvidence',[]))
 marker=plain_path(out/'.syphon-source-audit')
 if out.exists() and any(out.iterdir()) and (not marker.is_file() or marker.read_text(encoding='utf-8')!='Syphon source audit schema 1\n'):raise ValueError('Use a new dedicated audit output directory')
 out.mkdir(parents=True,exist_ok=True)
 marker.write_bytes(b'Syphon source audit schema 1\n')
 plain_path(out/'source-audit.json').write_bytes((json.dumps(report,indent=2)+'\n').encode('utf-8'))
 plain_path(out/'source-audit.md').write_bytes(markdown(report).encode('utf-8'))
 evidence=out/'notices';plain_path(evidence);evidence.mkdir(exist_ok=True)
 inputs=[cache['FFmpeg-GPLv3.txt'],cache['FFmpeg-build-and-source.txt'],LOCK,ROOT/'packaging/licenses/yt-dlp-LICENSE.txt',ROOT/'packaging/licenses/yt-dlp-THIRD-PARTY.txt']
 if args.source_materials:
  inputs.append(args.source_materials/'source-materials.json')
  inputs.extend(args.source_materials/item['file'] for item in details.get('recipeEvidence',[]))
  inputs.extend(args.source_materials/details[key]['file'] for key in ('checksums','binaryInspection') if details.get(key))
 for source in inputs:
  target=plain_path(evidence/source.name)
  if target.exists() and target.samefile(source):raise ValueError('Audit evidence cannot overwrite its source')
  shutil.copyfile(source,target)
 sums=[f'{digest(plain_path(file))}  {file.relative_to(out).as_posix()}' for file in sorted(out.rglob('*')) if file.is_file() and file.name!='SHA256SUMS.txt']
 plain_path(out/'SHA256SUMS.txt').write_bytes(('\n'.join(sums)+'\n').encode('utf-8'))
 print(json.dumps({'ffmpegVersionRecords':len(report['ffmpegExternalLibraries']),'ytDlpNoticeRecords':len(report['ytDlpThirdPartyNotices']),'retainedSourceArchives':len(materials),'sourceDistributionComplete':False,'output':str(out)},indent=2))


if __name__=='__main__':
 try:main()
 except (ValueError,OSError,KeyError) as error:raise SystemExit(str(error))
