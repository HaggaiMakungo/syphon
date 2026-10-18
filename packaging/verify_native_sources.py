"""Verify the reviewed source companion without extracting or executing its contents."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import zipfile

from dependencies import LOCK, digest, plain_path

def lock_identity(path):
    # Source checkouts may normalize line endings; identify the exact JSON data.
    data = json.loads(Path(path).read_text(encoding='utf-8-sig'))
    canonical = json.dumps(data, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode('utf-8')
    return hashlib.sha256(canonical).hexdigest()


def safe_name(name):
    if (not isinstance(name, str) or not name or '\\' in name or ':' in name
            or name.startswith('/') or any(p in ('', '.', '..') for p in name.split('/'))
            or PurePosixPath(name).as_posix() != name):
        raise ValueError('Unsafe source member name')


def verify_bundle(path, receipt, dependency_lock_sha256):
    path = plain_path(path)
    if path.stat().st_size != receipt['bytes'] or digest(path) != receipt['sha256']:
        raise ValueError('Source companion differs from reviewed archive receipt')
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        if len(entries) > 20000 or len(set(n.casefold() for n in names)) != len(names):
            raise ValueError('Excessive or duplicate source members')
        for entry in entries:
            safe_name(entry.filename)
            if entry.file_size > 256_000_000 or entry.is_dir() or (entry.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError('Unsupported source member')
        if sum(entry.file_size for entry in entries) > 2_000_000_000:
            raise ValueError('Source companion exceeds expanded-size limit')
        info = archive.getinfo('native-source-bundle.json')
        if info.file_size > 4_000_000:
            raise ValueError('Source manifest exceeds limit')
        manifest = json.loads(archive.read(info))
        if manifest.get('schema') != 1 or manifest.get('dependencyLockSha256') != dependency_lock_sha256:
            raise ValueError('Source evidence belongs to a different release input lock')
        files = manifest.get('files')
        if not isinstance(files, dict) or not files or set(names) != set(files) | {'native-source-bundle.json'}:
            raise ValueError('Source manifest does not cover exactly the archive members')
        for name, item in files.items():
            safe_name(name)
            if (type(item.get('bytes')) is not int or item['bytes'] < 0
                    or not re.fullmatch('[0-9a-f]{64}', item.get('sha256', ''))):
                raise ValueError('Invalid source member digest record')
            entry = archive.getinfo(name)
            if entry.file_size != item['bytes']:
                raise ValueError('Source member length mismatch: ' + name)
            fingerprint = hashlib.sha256()
            with archive.open(entry) as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b''):
                    fingerprint.update(block)
            if fingerprint.hexdigest() != item['sha256']:
                raise ValueError('Source member digest mismatch: ' + name)
    return len(files)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--receipt', type=Path, default=Path(__file__).with_name('native-source-bundle.win-x64.json'))
    parser.add_argument('--lock', type=Path, default=LOCK)
    args = parser.parse_args()
    receipt = json.loads(args.receipt.read_text(encoding='utf-8'))
    count = verify_bundle(args.archive, receipt, lock_identity(args.lock))
    print(f'Verified source companion: {count} members; current release input lock matched. No source executed.')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, zipfile.BadZipFile) as error:
        raise SystemExit(str(error))
