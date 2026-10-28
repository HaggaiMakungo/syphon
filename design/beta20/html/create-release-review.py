from pathlib import Path
import re
import shutil
import json

root = Path(__file__).parent
page = (root / 'release.html').read_text(encoding='utf-8')
replacements = {
    '<title>Syphon · Beta 20</title>': '<title>Syphon · Your loading program</title>',
    'release-refinement.css?v=2': 'release-refinement.css?v=2',
    '</head>': '<link rel="stylesheet" href="release-review.css?v=1"></head>',
    'NEXT UP / BETA 20': 'SYPHON / BETA 20',
    '<h1>Good things<br>come in <em class="pixel-word" aria-label="bytes."></em></h1>': '<h1>Your loading<br><em>program.</em></h1>',
    'Big files. Long queues. That video you meant to save. Give them a home, then get on with your day.': 'This is Syphon. We can load videos, music, images, software, whole archives. Whatever you need for what comes next.',
    'Your queue. Under control.': 'You’re in control.',
    'Pause it. Pick it back up. Keep moving.': 'Start it now. Queue it for later. Pick up where you left off.',
    'Take a look': 'See what’s inside',
    '01 / THE EXPERIENCE': '01 / FOLLOW THE SIGNAL',
    'Close by.<br>Out of your way.': 'Keep an eye on it.<br>Keep doing your thing.',
    'A compact home for your downloads, with just enough presence to keep you in the loop.': 'The main window, a floating monitor, or a glance at the taskbar. You choose how much you see.',
    'Your queue, at a glance': 'See the whole picture',
    'A little window, a lot of info': 'Keep a window open',
    'A one-second charging pulse in your taskbar.': 'A quick pulse means it’s still downloading.',
    'A bit more Syphon.': 'A few useful upgrades.',
    'From the first pasted link to the last byte, keep your downloads within reach.': 'Less digging through folders. Fewer windows to babysit. More time for what you downloaded in the first place.',
    'From link to your library': 'Everything in its place',
    'Paste one link or add a batch. Choose where your files go, start them now, or leave them queued for later. Your next download starts on your terms.': 'One link or a whole batch. Send images to Images, archives to Compressed, and keep videos, music, documents and programs in their own folders. Find the finished file from your history.',
    'Small windows. Clear signals.': 'Still there. Still downloading.',
    'A little breathing room': 'Leave room for the rest',
    'Set a speed limit when the rest of your connection needs some room. Find finished files in your history, check their details, and open their folder when you need them.': 'A download shouldn’t take the whole connection hostage. Set a speed limit, pause what can wait, and resume supported transfers when you’re ready.',
    '03 / BRING YOUR BROWSER': '03 / THE CONNECTION',
    'Found it?<br><em>Syphon it.</em>': 'There’s more<br><em>behind the link.</em>',
    'Keep the browser companion close for links, video and audio. The current beta setup uses an unpacked extension for Chrome or Edge.': 'Found a video worth keeping? Open “Syphon this”, choose an available format, and send it to the app. Or hand over a file link without leaving the page.',
    'Explore browser setup ↗': 'See browser setup ↗',
    'Open your browser’s extensions page, enable Developer mode, then load the extension folder.': 'In Chrome, Edge or Vivaldi, enable Developer mode on the extensions page and load the included extension folder.',
    'Open “Syphon this” and check the connection. You’re ready for the next link.': 'Open “Syphon this” and check that the app is connected. The next link is yours.',
    'YOUR NEXT DOWNLOAD HAS A NEW HOME': 'THE NEXT MOVE IS YOURS',
    'Make room for<br><em>the good stuff.</em>': 'What do you<br><em>need to load?</em>',
    'Every byte deserves a good home.': 'Ready when you are.',
    '<script src="release.js?v=2"></script>': '<script src="release-review.js?v=1"></script>',
}
for before, after in replacements.items():
    if before not in page:
        raise ValueError(f'Missing copy: {before}')
    page = page.replace(before, after)

page = re.sub(r'orbital-s/png/transparent/orbital-s-\d+\.png', 'brand/syphon-release-logo.png', page)
page = page.replace('<div class="product-stage"', '<div class="signal-field" aria-hidden="true"><i></i><i></i><i></i><i></i></div><div class="product-stage"', 1)
page = page.replace('</div></ol></section>', '</div></ol></section>')
page = page.replace('</ol></section>', '</ol><p class="browser-note">Video availability depends on the source. Protected or inaccessible streams can’t be downloaded.</p></section>')
page = re.sub(r'<div class="mini-app">.*?(?=<div class="stage-caption")', '<img class="current-main-capture" src="release-captures/main.png?v=2" width="760" height="580" alt="Current Syphon Beta 20 Downloads window with Phosphor icons, two active downloads and one paused download.">\n ', page, count=1, flags=re.S)
page = re.sub(r'<div class="queue-focus">.*?</div><p class="display-caption">', '<img class="current-transfer-capture" src="release-captures/transfer.png?v=2" width="628" height="102" alt="Current download card showing Coastal landscapes.mp4 at 34 percent."><p class="display-caption">', page, count=1, flags=re.S)
page = re.sub(r'<div class="floating-sample">.*?</div><p class="display-caption">', '<img class="current-floating-capture" src="release-captures/floating.png?v=2" width="330" height="458" alt="Current expanded floating download monitor with speed, remaining time, graph and pause controls."><p class="display-caption">', page, count=1, flags=re.S)
page = page.replace('syphon-orbital-icon', 'syphon-activity-icon')
page = page.replace('src="orbital-s/orbital-icon.js"', 'src="release-activity.js"')
page = page.replace('<script src="pixel-icons.js"></script>', '<script src="release-icons.js"></script>')
page = page.replace('href="index.html?v=18&focus=1">Try the queue ↗', 'href="release-captures/main.png?v=2" target="_blank" rel="noopener">View the window ↗')
page = page.replace('href="floating.html?state=single-expanded">Try the floating window ↗', 'href="release-captures/floating.png?v=2" target="_blank" rel="noopener">View the monitor ↗')
page = page.replace('A final shimmer means they’re all done.', 'A final shimmer means the active downloads are done.')
(root / 'brand').mkdir(exist_ok=True)
shutil.copyfile(root.parents[2] / 'src/Syphon.Desktop/Assets/Brand/syphon_emblem_transparent.png', root / 'brand/syphon-release-logo.png')
shutil.copyfile(root.parents[2] / 'src/Syphon.Desktop/Assets/Brand/syphon_window_icon.png', root / 'brand/syphon-taskbar-logo.png')
icons = json.loads((root.parents[2] / 'ui/desktop/icons.json').read_text(encoding='utf-8'))
aliases = {'spark':'plus', 'gear':'settings', 'pause':'player-pause', 'play':'player-play', 'zip':'file-zip'}
names = set(re.findall(r'data-icon="([^"]+)"', page))
page_icons = {name:icons[aliases.get(name,name)] for name in names}
(root / 'release-icons.js').write_text('const releaseIcons='+json.dumps(page_icons)+';\nfunction icon(name){return releaseIcons[name]||releaseIcons.file;}\n', encoding='utf-8')
(root / 'release-review.html').write_text(page, encoding='utf-8')
script = (root / 'release.js').read_text(encoding='utf-8')
script = script[:script.index('const glyphs=')]
script = script.replace(script.splitlines()[0]+'\n', '')
script = script.replace('syphon-orbital-icon', 'syphon-activity-icon')
(root / 'release-review.js').write_text(script, encoding='utf-8')
