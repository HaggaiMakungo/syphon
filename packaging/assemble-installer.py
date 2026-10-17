from pathlib import Path
import argparse,json
from assemble_installer import assemble
from dependencies import load_lock

def main():
 root=Path(__file__).resolve().parents[1]
 parser=argparse.ArgumentParser()
 parser.add_argument('--output',required=True,type=Path)
 parser.add_argument('--version',default='0.1.0-beta.9')
 parser.add_argument('--stage',type=Path,default=root/'artifacts/setup-stage')
 parser.add_argument('--core',type=Path,default=root/'artifacts/setup-core/Syphon.Setup.exe')
 args=parser.parse_args()
 print(json.dumps(assemble(args.stage,args.core,args.output,args.version,load_lock()),indent=2))

if __name__=='__main__':
 try: main()
 except (ValueError,OSError) as error:raise SystemExit(str(error))
