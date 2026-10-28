"""Keep the public home page product-focused while identifying its download."""
import re


def product_page(page, version):
    release = 'https://github.com/HaggaiMakungo/syphon/releases/tag/v' + version
    beta = version.split('-beta.')[-1]
    page = page.replace('Syphon Beta 20: a Windows download manager', 'Syphon: a Windows download manager')
    page = re.sub(r'<div class="preview-bar">.*?</div>',
                  '<div class="preview-bar">READY WHEN YOU ARE <span>Made for Windows</span></div>', page)
    page = page.replace('href="#changes">What’s new', 'href="#features">Features')
    page = page.replace('SYPHON / BETA 20', 'SYPHON / FOR WINDOWS')
    for label in ['Get Beta 20', 'Get Syphon Beta 20', 'Download for Windows']:
        page = page.replace(label, 'Download Syphon')
    page = re.sub(r'<p class="fine">Windows x64.*?</p>',
                  '<p class="fine">Windows x64 <span>·</span> Beta ' + beta +
                  ' <span>·</span> <a href="' + release + '">What’s new ↗</a></p>', page)
    page = page.replace('id="changes"', 'id="features"')
    page = page.replace('02 / NEW IN BETA 20', '02 / MADE FOR YOUR DOWNLOADS')
    page = page.replace('A few useful upgrades.', 'Keep things moving.')
    page = page.replace('Current Syphon Beta 20 Downloads window', 'Syphon Downloads window')
    page = re.sub(r'<section class="wrap" style="padding-bottom:64px">.*?</section>',
        '<section class="wrap" style="padding-bottom:64px"><h2>Make yourself at home.</h2>'
        '<p>The Windows installer includes the app runtime and media tools. '
        'Connect your browser to send links straight to Syphon.</p>'
        '<p><a href="BROWSER-SETUP.md">Browser setup ↗</a> · '
        '<a href="' + release + '">Release notes &amp; download details ↗</a></p>'
        '<p class="fine">Windows may show a publisher warning. '
        'Signing information and installation notes are included with the release.</p></section>', page, flags=re.S)
    return page
