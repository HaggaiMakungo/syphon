"""Retain PyPI source releases and wheel metadata; never claim complete native provenance."""
import argparse,hashlib,json,shutil,urllib.request,zipfile,email
from pathlib import Path
ROOT=Path(__file__).resolve().parent.parent
DEST=None
OLD=None
INPUTS=None
def sha(p): return hashlib.file_digest(p.open('rb'),'sha256').hexdigest()
def get(url,p,expected=None,limit=100_000_000):
 if not p.exists():
  with urllib.request.urlopen(url,timeout=45) as r:
   data=r.read(limit+1)
  if len(data)>limit: raise ValueError('Download exceeds per-file limit')
  if expected and hashlib.sha256(data).hexdigest()!=expected: raise ValueError('Publisher metadata hash mismatch')
  p.write_bytes(data)
 if expected and sha(p)!=expected: raise ValueError('Retained file hash mismatch')
 return {'file':p.name,'bytes':p.stat().st_size,'sha256':sha(p),'url':url}
def main():
 DEST.mkdir(parents=True,exist_ok=True); lock=json.loads((ROOT/'packaging/yt-dlp-build.win-x64.json').read_text()); rows=[]
 for item in lock['files']:
  if item['kind']!='wheel': continue
  wheel=INPUTS/item['file']
  if sha(wheel)!=item['sha256']: raise ValueError('Locked wheel differs')
  with zipfile.ZipFile(wheel) as z:
   meta=email.message_from_bytes(z.read(next(n for n in z.namelist() if n.endswith('.dist-info/METADATA'))))
   notices=[{'member':n,'sha256':hashlib.sha256(z.read(n)).hexdigest()} for n in z.namelist() if any(w in n.lower().split('/')[-1] for w in ['license','copying','notice'])]
  name,version=meta['Name'],meta['Version']; mp=DEST/f'pypi-{name}-{version}.json'; mu=f'https://pypi.org/pypi/{name}/{version}/json'; mr=get(mu,mp,limit=10_000_000); data=json.loads(mp.read_text(encoding='utf-8'))
  exact=next(x for x in data['urls'] if x['filename']==item['file'])
  pypi_match=exact['digests']['sha256']==item['sha256']
  if not pypi_match and not item['source'].startswith('https://github.com/yt-dlp/Pyinstaller-Builds/releases/download/'): raise ValueError('Wheel no longer matches PyPI metadata')
  sdists=[x for x in data['urls'] if x['packagetype']=='sdist']; sources=[]
  for s in sdists:
   p=DEST/('pypi-'+s['filename'])
   old=OLD/s['filename'] if OLD else None
   if not p.exists() and old and old.exists() and sha(old)==s['digests']['sha256']: shutil.copyfile(old,p)
   sources.append(get(s['url'],p,s['digests']['sha256']))
  row={'name':name,'version':version,'wheel':item['file'],'wheelSha256':item['sha256'],'metadata':mr,'wheelMatchesPyPI':pypi_match,'wheelSource':item['source'],'licenseExpression':meta.get('License-Expression'),'license':meta.get('License'),'licenseFiles':meta.get_all('License-File',[]),'wheelNotices':notices,'nativeMembers':[n for n in z.namelist() if n.lower().endswith(('.pyd','.dll','.exe'))],'sources':sources,'coverage':'Source-release archive verified; wheelMatchesPyPI explicitly distinguishes the custom yt-dlp PyInstaller wheel from PyPI. Checked against retained HTTPS PyPI metadata; not proof of native library provenance, build recipe completeness or reproducibility.'}
  rows.append(row); print(name,version,len(sources),flush=True)
 result={'schema':1,'date':'2026-10-18','complete':False,'buildLockSha256':sha(ROOT/'packaging/yt-dlp-build.win-x64.json'),'authentication':'HTTPS PyPI metadata digest matching. No independent Sigstore/GPG verification claimed.','packages':rows}
 (DEST/'wheel-source-inventory.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
 (ROOT/'packaging/yt-dlp-wheel-source-inventory.win-x64.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--inputs',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);parser.add_argument('--historical',type=Path,default=OLD);args=parser.parse_args();INPUTS=args.inputs;DEST=args.output;OLD=args.historical;main()
