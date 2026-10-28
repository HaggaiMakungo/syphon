"""Read-only validation of exported dimensions, alpha, hashes and ICO frame directories."""
from pathlib import Path
from PIL import Image
import hashlib,json,struct
def pixels(image):
 return image.get_flattened_data() if hasattr(image,'get_flattened_data') else image.getdata()
root=Path(__file__).resolve().parent
manifest=json.loads((root/'manifest.json').read_text(encoding='utf-8'))
for item in manifest['files']:
 p=root/item['path']
 assert hashlib.sha256(p.read_bytes()).hexdigest()==item['sha256'],p
 with Image.open(p) as image:
  assert image.size==(item['size'],item['size']),p
  assert image.mode=='RGBA',p
  assert image.getpixel((0,0))[3]==0,p
  if '/transparent/' in item['path'] and item['size'] in [16,20,24,32,64]:
   assert set(pixels(image.getchannel('A'))).issubset({0,255}),p
for p in (root/'windows').glob('*.ico'):
 data=p.read_bytes();reserved,kind,count=struct.unpack_from('<HHH',data)
 assert (reserved,kind,count)==(0,1,8)
 for i in range(count):
  entry=struct.unpack_from('<BBBBHHII',data,6+i*16)
  width,height=entry[0] or 256,entry[1] or 256
  size,offset=entry[-2:];png=data[offset:offset+size]
  assert png[:8]==b'\x89PNG\r\n\x1a\n'
  assert struct.unpack_from('>II',png,16)==(width,height)
palette=set(manifest['palette'].values());counts=[]
for progress in [0,25,50,75,100]:
 with Image.open(root/f'png/states/orbital-s-progress-{progress}.png') as image:
  colors=[tuple(bytes.fromhex(manifest['palette'][c][1:])) for c in ['deep','violet','lilac','highlight']]
  count=sum(1 for px in pixels(image) if px[:3] in colors and px[3])
  counts.append(count)
assert counts[0]==0 and all(b>a for a,b in zip(counts,counts[1:])),counts
for size in [16,32,48,128]:
 assert (root/f'browser-extension/icon{size}.png').read_bytes()==(root/f'png/transparent/orbital-s-{size}.png').read_bytes()
print(json.dumps({'pngs':len(manifest['files']),'hashes':True,'dimensions':True,'transparentExterior':True,'crispSmallGridAlpha':True,'icoFiles':2,'icoFrames':16,'progressFillIncreasing':counts,'extensionCopies':4}))
