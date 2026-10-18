"""Build a lightweight Pages site linked to a verified, published GitHub prerelease."""
import argparse
import json
from pathlib import Path
import shutil
from urllib.parse import quote

REPOSITORY = 'https://github.com/HaggaiMakungo/syphon'


def prepare(preview, release_files, release, output):
    if output.exists():
        raise ValueError('Use a new site output directory.')
    metadata = json.loads((preview / 'release.json').read_text(encoding='utf-8'))
    tag = 'v' + metadata['version']
    base = REPOSITORY + '/releases/download/' + tag + '/'
    if release.get('draft') is not False or release.get('prerelease') is not True or release.get('tag_name') != tag or release.get('html_url') != REPOSITORY + '/releases/tag/' + tag:
        raise ValueError('Expected the published matching prerelease.')
    assets = {a['name']: a for a in release['assets']}
    for item in [metadata['installer'], metadata['extension'], *metadata['sources']]:
        asset = assets.get(item['file'], {})
        if asset.get('size') != item['bytes'] or asset.get('digest') != 'sha256:' + item['sha256'] or asset.get('state') != 'uploaded':
            raise ValueError('Release asset identity mismatch: ' + item['file'])
    for name, asset in assets.items():
        if Path(name).name != name or asset.get('browser_download_url') != base + quote(name):
            raise ValueError('Unexpected release asset location.')
    output.mkdir(parents=True)
    shutil.copytree(preview / 'assets', output / 'assets')
    page = (preview / 'index.html').read_text(encoding='utf-8')
    page = page.replace('<meta name="robots" content="noindex,nofollow">', '<meta name="robots" content="index,follow">')
    page = page.replace('A local preview of the Syphon Windows download manager release.', 'Download Syphon for Windows: a free, open-source download manager for files, video and audio.')
    page = page.replace('Local release preview', 'Public beta').replace('Not published', 'Beta ' + metadata['version'].rsplit('.', 1)[1])
    for name, asset in assets.items():
        page = page.replace('href="' + name + '"', 'href="' + asset['browser_download_url'] + '"')
    source_name = 'Syphon-' + metadata['version'] + '-source.zip'
    if source_name in assets:
        page = page.replace('<dt>Native dependencies</dt>', '<dt>Application source</dt><dd><a href="' + assets[source_name]['browser_download_url'] + '">Exact build source ZIP ↗</a></dd><dt>Native dependencies</dt>')
        metadata['applicationSource'] = {'file': source_name, 'url': assets[source_name]['browser_download_url'], 'sha256': assets[source_name]['digest'].removeprefix('sha256:'), 'bytes': assets[source_name]['size']}
    for name in ('RELEASE-NOTES.md','BROWSER-SETUP.md','RELEASE-READINESS.md','LICENSE.txt','THIRD-PARTY-NOTICES.md','SHA256SUMS.txt'):
        source = release_files / name
        if source.is_file():
            text = source.read_text(encoding='utf-8')
            for asset_name, asset in assets.items():
                text = text.replace('](' + asset_name + ')', '](' + asset['browser_download_url'] + ')')
            (output / name).write_text(text, encoding='utf-8')
    metadata.update(status='public-beta', publiclyPublished=True, releaseUrl=release['html_url'])
    metadata['pending'] = [p for p in metadata.get('pending', []) if p != 'Public release authorization']
    for item in [metadata['installer'], metadata['extension'], *metadata['sources']]:
        item['url'] = assets[item['file']]['browser_download_url']
    (output / 'index.html').write_text(page, encoding='utf-8')
    (output / 'release.json').write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    (output / '.nojekyll').touch()
    return metadata


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('preview','release-files','release-record','output'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    prepare(args.preview, args.release_files, json.loads(args.release_record.read_text(encoding='utf-8')), args.output)
    print('PASS public site prepared; large downloads remain release assets.')
