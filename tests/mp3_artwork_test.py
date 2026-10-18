"""Offline integration check of the bundled MP3 thumbnail postprocessor."""
import functools
import http.server
import json
from pathlib import Path
import subprocess
import tempfile
import threading

TOOLS = Path(__file__).resolve().parents[1] / '.tools/release-deps/win-x64'


def run(tool, *args):
    result = subprocess.run([str(TOOLS / (tool + '.exe')), *map(str, args)],
                            capture_output=True, text=True, timeout=60)
    if result.returncode:
        raise AssertionError(result.stderr)
    return result.stdout


class QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *_):
        pass


def main():
    with tempfile.TemporaryDirectory(prefix='Syphon-artwork-') as scratch:
        root = Path(scratch)
        run('ffmpeg', '-v', 'error', '-f', 'lavfi', '-i', 'sine=duration=1',
            '-c:a', 'aac', root / 'audio.m4a')
        run('ffmpeg', '-v', 'error', '-f', 'lavfi', '-i', 'color=c=purple:s=64x64',
            '-frames:v', '1', root / 'cover.webp')
        server = http.server.ThreadingHTTPServer(('127.0.0.1', 0),
            functools.partial(QuietHandler, directory=scratch))
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            base = f'http://127.0.0.1:{server.server_port}'
            for case, image in [('cover', 'cover.webp'), ('absent', None), ('unavailable', 'missing.jpg')]:
                output = root / case
                output.mkdir()
                info = {'id': case, 'title': case, 'extractor': 'generic',
                        'webpage_url': base, 'url': base + '/audio.m4a',
                        'ext': 'm4a', 'vcodec': 'none', 'acodec': 'aac'}
                if image:
                    info['thumbnails'] = [{'url': base + '/' + image, 'id': '0'}]
                metadata = root / (case + '.json')
                metadata.write_text(json.dumps(info), encoding='utf-8')
                run('yt-dlp', '--ignore-config', '--no-plugin-dirs', '--no-cache-dir',
                    '--load-info-json', metadata, '--ffmpeg-location', TOOLS,
                    '--extract-audio', '--audio-format', 'mp3', '--audio-quality', '2',
                    '--embed-thumbnail', '--convert-thumbnails', 'jpg',
                    '--output', output / '%(id)s.%(ext)s')
                target = output / (case + '.mp3')
                streams = json.loads(run('ffprobe', '-v', 'error', '-show_streams',
                                         '-of', 'json', target))['streams']
                assert any(s['codec_name'] == 'mp3' for s in streams), streams
                art = [s for s in streams if s.get('disposition', {}).get('attached_pic')]
                assert bool(art) == (case == 'cover'), streams
                if art:
                    assert art[0]['codec_name'] == 'mjpeg', art
                assert list(output.iterdir()) == [target], list(output.iterdir())
                print(f'PASS {case}: playable MP3, expected embedded art, no image sidecar')
        finally:
            server.shutdown()
            server.server_close()
            thread.join()


if __name__ == '__main__':
    main()
