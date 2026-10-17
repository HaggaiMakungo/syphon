import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'packaging'))
from media_runtime import validate_runtime
class RuntimeTests(unittest.TestCase):
 def test_old_python_rejected(self):
  with self.assertRaisesRegex(ValueError,'Python'):validate_runtime('[debug] Python 3.10.11 (CPython AMD64 64bit) - Windows (OpenSSL 1.1.1t)')
 def test_missing_runtime_rejected(self):
  with self.assertRaises(ValueError):validate_runtime('yt-dlp 2026.08.19')
 def test_old_ssl_rejected(self):
  with self.assertRaisesRegex(ValueError,'OpenSSL'):validate_runtime('[debug] Python 3.13.16 (CPython AMD64 64bit) - Windows (OpenSSL 1.1.1t)')
 def test_modern_runtime_accepted(self):
  self.assertEqual(validate_runtime('[debug] Python 3.13.16 (CPython AMD64 64bit) - Windows (OpenSSL 3.5.9 29 Sep 2026)'),{'python':'3.13.16','openssl':'3.5.9'})
if __name__=='__main__':unittest.main()
