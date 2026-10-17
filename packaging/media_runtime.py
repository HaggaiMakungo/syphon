"""Release gate for Syphon's reviewed private yt-dlp runtime."""
import argparse,json,re,subprocess
from dependencies import load_lock,verify_file

def validate_runtime(text):
 python=re.search(r'\[debug\] Python (\d+\.\d+\.\d+).*CPython AMD64 64bit',text)
 if not python or tuple(map(int,python[1].split('.'))) < (3,13,16):raise ValueError('yt-dlp must report reviewed Python 3.13.16 or newer on Windows x64')
 ssl=re.search(r'OpenSSL (\d+\.\d+\.\d+)',text)
 if not ssl or tuple(map(int,ssl[1].split('.'))) < (3,5,9):raise ValueError('yt-dlp must report reviewed OpenSSL 3.5.9 or newer')
 return {'python':python[1],'openssl':ssl[1]}

def inspect(executable):
 # Verify the pinned binary before execution, ignore user config/plugins/cache,
 # and use an unsupported scheme so no remote media is requested.
 verify_file(executable,load_lock()['files']['yt-dlp.exe'])
 result=subprocess.run([str(executable),'--ignore-config','--no-plugin-dirs','--no-cache-dir','--verbose','--simulate','--','syphon-runtime-check:'],capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=45)
 if result.returncode!=1 or 'Unsupported url scheme' not in result.stderr:raise ValueError('yt-dlp runtime probe did not end with its expected unsupported scheme')
 report=validate_runtime(result.stderr)
 if 'syphon_win_x64_py313' not in result.stderr:raise ValueError('Expected the reviewed Syphon-local yt-dlp variant')
 return report

if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('executable',type=__import__('pathlib').Path)
 try:print(json.dumps(inspect(p.parse_args().executable),indent=2))
 except (ValueError,OSError,subprocess.TimeoutExpired) as e:raise SystemExit(str(e))
