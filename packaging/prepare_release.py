"""Prepare and verify an unpublished release preview from an existing signed build."""
import argparse
from datetime import date
import hashlib
import html
from html.parser import HTMLParser
import json
import os
from pathlib import Path
import re
import shutil
from urllib.parse import unquote, urlsplit
import zipfile

ROOT = Path(__file__).resolve().parents[1]
TEMPLATES = ROOT / 'packaging/release-page'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def require_file(path, entry):
    if path.is_symlink() or not path.is_file() or path.stat().st_size != entry['Length'] or digest(path) != entry['Sha256'].lower():
        raise ValueError(f'File identity mismatch: {path.name}')


def prepare(installer, record_path, extracted, manifest_path, output, release_date, sources=None):
    installer, extracted, output = installer.resolve(), extracted.resolve(), output.resolve()
    if output.exists() or output.is_relative_to(extracted):
        raise ValueError('Use a new output directory outside the extracted payload.')
    date.fromisoformat(release_date)
    record, manifest = read_json(record_path), read_json(manifest_path)
    require_file(installer, {'Length': record['bytes'], 'Sha256': record['sha256']})
    if not all(record.get(key) is True for key in ('signed', 'cryptographicSignatureValid', 'fileDigestValid')):
        raise ValueError('A verified self-signed installer receipt is required.')
    require_file(extracted / 'build-inputs.json', manifest['Files']['build-inputs.json'])
    build = read_json(extracted / 'build-inputs.json')
    version = build['version']
    if not re.fullmatch(r'0\.1\.0-beta\.\d+', version) or manifest['Version'] != version:
        raise ValueError('Release versions do not match.')
    if build['sourceDirty'] is not False or build['architecture'] != 'win-x64' or not re.fullmatch(r'[0-9a-f]{40}', build['sourceRevision']):
        raise ValueError('Expected clean Windows x64 build provenance.')
    if installer.name != f'Syphon-Setup-{version}-win-x64.exe':
        raise ValueError('Installer filename/version mismatch.')
    extension = extracted / 'extensions'
    members = sorted(extension.rglob('*'))
    if any(p.is_symlink() for p in members):
        raise ValueError('Extension links are not supported.')
    members = [p for p in members if p.is_file()]
    actual = {p.relative_to(extracted).as_posix() for p in members}
    expected = {p for p in manifest['Files'] if p.startswith('extensions/')}
    if not actual or actual != expected:
        raise ValueError('Extension file inventory differs from the installer.')
    for path in members:
        require_file(path, manifest['Files'][path.relative_to(extracted).as_posix()])
    extension_version = read_json(extension / 'manifest.json')['version']
    if not re.fullmatch(r'\d+(?:\.\d+){1,3}', extension_version):
        raise ValueError('Invalid extension version.')
    beta = version.rsplit('.', 1)[1]
    extension_name = f'Syphon-Extension-{extension_version}-beta{beta}.zip'
    if sources is None:
        sources = [read_json(ROOT / 'packaging' / name) for name in
                   ('native-source-bundle.win-x64.json', 'ffmpeg-source-evidence.win-x64.json')]
    if len(sources) != 2 or len({item['file'] for item in sources}) != 2:
        raise ValueError('Both source-material archives are required.')
    for item in sources:
        if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._-]*\.zip', item['file']) or item['file'] in (installer.name, extension_name):
            raise ValueError('Invalid source archive filename.')
        require_file(installer.parent / item['file'], {'Length': item['bytes'], 'Sha256': item['sha256']})
    output.mkdir(parents=True)
    # Hardlink immutable release artifacts on the same volume; copy across volumes.
    for source in [installer, *(installer.parent / item['file'] for item in sources)]:
        try:
            os.link(source, output / source.name)
        except OSError:
            shutil.copyfile(source, output / source.name)
    with zipfile.ZipFile(output / extension_name, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for path in members:
            info = zipfile.ZipInfo(path.relative_to(extension).as_posix(), date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, path.read_bytes())
    extension_hash = digest(output / extension_name)
    metadata = {
        'version': version, 'date': release_date, 'architecture': 'win-x64',
        'sourceRevision': build['sourceRevision'], 'status': 'local-preview', 'publiclyPublished': False,
        'installer': {'file': installer.name, 'bytes': record['bytes'], 'sha256': record['sha256']},
        'extension': {'file': extension_name, 'version': extension_version,
                      'bytes': (output / extension_name).stat().st_size, 'sha256': extension_hash},
        'signing': {'type': 'local self-signed Authenticode', 'publiclyTrusted': False,
                    'certificateThumbprint': record['certificateThumbprint'], 'timestamped': False},
        'acceptance': {'ownerReportedFunctionalAndInstallerPassOnThisPC': False, 'cleanMachineTested': False},
        'sources': [{key: item[key] for key in ('file', 'bytes', 'sha256')} for item in sources],
        'pending': [f'Owner Beta{beta} upgrade acceptance', 'Public release authorization',
                    'Browser store accounts, submission and store-installed acceptance'],
    }
    values = {
        'VERSION': version, 'BETA': beta, 'DATE': release_date, 'INSTALLER': installer.name,
        'MB': f"{record['bytes'] / 1e6:.1f}", 'SHA256': record['sha256'],
        'EXTENSION': extension_name, 'EXTENSION_VERSION': extension_version,
        'EXTENSION_SHA256': extension_hash, 'REVISION': build['sourceRevision'],
    }
    for index, item in enumerate(sources, 1):
        values.update({f'SOURCE{index}': item['file'], f'SOURCE{index}_MB': f"{item['bytes'] / 1e6:.1f}",
                       f'SOURCE{index}_SHA256': item['sha256']})
    for source, destination in [('index.html', 'index.html'), ('release-notes.md', 'RELEASE-NOTES.md'),
                                 ('browser-setup.md', 'BROWSER-SETUP.md'), ('readiness.md', 'RELEASE-READINESS.md')]:
        rendered = (TEMPLATES / source).read_text(encoding='utf-8')
        for key, value in values.items():
            rendered = rendered.replace('@@' + key + '@@', html.escape(value) if source.endswith('.html') else value)
        if '@@' in rendered:
            raise ValueError('Unresolved template value.')
        (output / destination).write_text(rendered, encoding='utf-8')
    assets = output / 'assets'
    assets.mkdir()
    shutil.copyfile(TEMPLATES / 'site.css', assets / 'site.css')
    for name in ('inter-400.woff2', 'inter-500.woff2', 'inter-600.woff2', 'Inter-LICENSE.txt'):
        shutil.copyfile(ROOT / 'extensions/syphon/fonts' / name, assets / name)
    for name in ('space-500-600.woff2', 'Space-Grotesk-OFL.txt'):
        shutil.copyfile(ROOT / 'ui/desktop/fonts' / name, assets / name)
    shutil.copyfile(ROOT / 'extensions/syphon/brand/emblem.png', assets / 'emblem.png')
    shutil.copyfile(ROOT / 'LICENSE', output / 'LICENSE.txt')
    shutil.copyfile(ROOT / 'THIRD-PARTY-NOTICES.md', output / 'THIRD-PARTY-NOTICES.md')
    (output / 'release.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    checksums = '\n'.join(f'{digest(p)}  {p.relative_to(output).as_posix()}'
                          for p in sorted(output.rglob('*')) if p.is_file())
    (output / 'SHA256SUMS.txt').write_text(checksums + '\n', encoding='utf-8')
    verify(output)
    return metadata


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        self.links.extend(v for k, v in attrs if k in ('href', 'src') and v)


def verify(output):
    output = output.resolve()
    seen = set()
    for line in (output / 'SHA256SUMS.txt').read_text(encoding='utf-8').splitlines():
        fingerprint, name = line.split('  ', 1)
        logical_path = output / name
        path = logical_path.resolve()
        if not path.is_relative_to(output) or logical_path.is_symlink() or not path.is_file() or digest(path) != fingerprint:
            raise ValueError(f'Checksum verification failed: {name}')
        seen.add(name)
    actual = {p.relative_to(output).as_posix() for p in output.rglob('*') if p.is_file()} - {'SHA256SUMS.txt'}
    if seen != actual:
        raise ValueError('Release inventory and checksums differ.')
    parser = Links()
    parser.feed((output / 'index.html').read_text(encoding='utf-8'))
    for link in parser.links:
        parsed = urlsplit(link)
        if parsed.scheme or not parsed.path:
            continue
        path = (output / unquote(parsed.path)).resolve()
        if not path.is_relative_to(output) or not path.is_file():
            raise ValueError(f'Broken local page link: {link}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--installer', type=Path)
    parser.add_argument('--receipt', type=Path)
    parser.add_argument('--extracted', type=Path)
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--date')
    parser.add_argument('--verify-only', action='store_true')
    args = parser.parse_args()
    if args.verify_only:
        verify(args.output)
        print('PASS release inventory, SHA-256 checksums and local page links')
    else:
        if not all((args.installer, args.receipt, args.extracted, args.manifest, args.date)):
            parser.error('Provide installer, receipt, extracted payload, manifest and date.')
        print(json.dumps(prepare(args.installer, args.receipt, args.extracted, args.manifest, args.output, args.date), indent=2))
