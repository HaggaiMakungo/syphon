"""Turn the approved Beta 20 review into a release site with verified asset links."""
from pathlib import Path
import argparse
import hashlib
import json
import re
import shutil
from prepare_release import verify
from product_page import product_page

ROOT = Path(__file__).resolve().parents[1]


def render(bundle):
    bundle = bundle.resolve()
    metadata = json.loads((bundle / 'release.json').read_text())
    if metadata['version'] != '0.1.0-beta.20':
        raise ValueError('This approved layout is for Beta 20.')
    base = 'https://github.com/HaggaiMakungo/syphon/releases/download/v0.1.0-beta.20/'
    source = ROOT / 'design/beta20/html'
    for name in ['release.css', 'release-refinement.css', 'release-review.css',
                 'release-icons.js', 'release-activity.js', 'release-review.js']:
        shutil.copyfile(source / name, bundle / name)
    for name in ['brand', 'fonts', 'release-captures']:
        shutil.copytree(source / name, bundle / name, dirs_exist_ok=True)
    page = (source / 'release-review.html').read_text(encoding='utf-8')
    page = page.replace('<meta name="robots" content="noindex,nofollow">',
                        '<meta name="description" content="Syphon Beta 20: a Windows download manager for files, video and audio.">')
    page = page.replace('BETA 20 IS ON ITS WAY', 'BETA 20 / AVAILABLE NOW')
    page = page.replace('COMING IN BETA 20', 'NEW IN BETA 20')
    page = page.replace('Beta 20 coming soon', 'Beta 20 · 28 October 2026')
    page = page.replace('href="companion.html?view=connect"', 'href="BROWSER-SETUP.md"')
    installer = base + metadata['installer']['file']
    extension = base + metadata['extension']['file']
    page = re.sub(r'<button([^>]*?) data-download([^>]*)>(.*?)</button>',
                  lambda m: '<a' + m[1] + m[2] + ' href="' + installer + '">' + m[3] + '</a>', page, flags=re.S)
    page = re.sub(r'<dialog id="download-dialog".*?</dialog>', '', page, flags=re.S)
    page = page.replace('</main>', '<section class="wrap" style="padding-bottom:64px"><h2>Before you load in</h2>'
        '<p>Windows x64. The installer includes the app runtime and media tools. It is locally self-signed, '
        'not publicly trusted or timestamped; Windows warnings may remain.</p>'
        '<p><a href="RELEASE-NOTES.md">Release notes</a> · <a href="BROWSER-SETUP.md">Browser setup</a> · '
        '<a href="' + extension + '">Companion ZIP</a> · <a href="' + base + 'SHA256SUMS.txt">Checksums</a></p>'
        '<p>Clean-machine certification is not claimed. Source support varies; protected or inaccessible streams may be unavailable.</p>'
        '<p>Optional source downloads: ' + ' · '.join('<a href="' + base + item['file'] + '">' +
            ('FFmpeg source and build materials' if 'FFmpeg' in item['file'] else 'Native runtime source and build materials') + '</a>'
            for item in metadata['sources']) + '.</p></section></main>')
    (bundle / 'index.html').write_text(product_page(page, metadata['version']), encoding='utf-8')
    script = (bundle / 'release-review.js').read_text(encoding='utf-8')
    script = script[:script.index('const dialog=')]
    (bundle / 'release-review.js').write_text(script, encoding='utf-8')
    shutil.copyfile(ROOT / 'RELEASE-NOTES.md', bundle / 'RELEASE-NOTES.md')
    metadata.update(status='release-ready', publiclyPublished=False)
    metadata['pending'] = ['Clean-machine acceptance', 'Browser-store distribution']
    (bundle / 'release.json').write_text(json.dumps(metadata, indent=2) + '\n')
    (bundle / 'SHA256SUMS.txt').write_text('\n'.join(
        hashlib.file_digest(p.open('rb'), 'sha256').hexdigest() + '  ' + p.relative_to(bundle).as_posix()
        for p in sorted(bundle.rglob('*')) if p.is_file() and p.name != 'SHA256SUMS.txt') + '\n')
    verify(bundle)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bundle', type=Path, required=True)
    render(parser.parse_args().bundle)
