import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'packaging'))
from media_runtime import validate_runtime
MODERN='[debug] Python 3.13.16 (CPython AMD64 64bit) - Windows (OpenSSL 3.5.9 29 Sep 2026)\n[debug] Optional libraries: requests-2.34.2, urllib3-2.8.0, websockets-17.0.1'
class RuntimeTests(unittest.TestCase):
 def test_old_python_rejected(self):
  with self.assertRaisesRegex(ValueError,'Python'):validate_runtime('[debug] Python 3.10.11 (CPython AMD64 64bit) - Windows (OpenSSL 1.1.1t)')
 def test_missing_runtime_rejected(self):
  with self.assertRaises(ValueError):validate_runtime('yt-dlp 2026.08.19')
 def test_old_ssl_rejected(self):
  with self.assertRaisesRegex(ValueError,'OpenSSL'):validate_runtime('[debug] Python 3.13.16 (CPython AMD64 64bit) - Windows (OpenSSL 1.1.1t)')
 def test_vulnerable_urllib3_rejected(self):
  with self.assertRaisesRegex(ValueError,'urllib3'):validate_runtime(MODERN.replace('urllib3-2.8.0','urllib3-2.7.0'))
 def test_missing_urllib3_rejected(self):
  with self.assertRaisesRegex(ValueError,'urllib3'):validate_runtime(MODERN.split('\n')[0])
 def test_modern_runtime_accepted(self):
  self.assertEqual(validate_runtime(MODERN),{'python':'3.13.16','openssl':'3.5.9','urllib3':'2.8.0'})
if __name__=='__main__':unittest.main()
