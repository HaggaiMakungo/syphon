// Render-only fixture for an isolated headless browser; never contacts the native host.
const fs = require("node:fs"), path = require("node:path");
const root = path.resolve(__dirname, ".."), folder = path.join(root, "artifacts", "extension");
fs.mkdirSync(folder, { recursive: true });
const scripts = ["media-model.js", "video-button.js"].map(name => fs.readFileSync(path.join(root, "extensions", "syphon", name), "utf8").replaceAll("</script", "<\\/script"));
fs.writeFileSync(path.join(folder, "video-preview.html"), `<!doctype html><meta charset="utf-8"><base href="https://fixture.invalid/"><title>Syphon video fixture</title>
<style>body{margin:0;padding:32px;background:#0b1220;color:#e2e8f0;font:16px 'Segoe UI',sans-serif}h1{font-size:24px}video{display:block;width:960px;height:540px;background:linear-gradient(135deg,#17354b,#192747 55%,#32234a)}p{color:#94a3b8}</style>
<h1>Video discovery fixture</h1><p>Direct sources and subtitle tracks exposed by a player</p>
<video controls preload="none"><source src="./720.mp4" label="720p"><source src="./1080.mp4" label="1080p"><track kind="subtitles" src="./en.vtt" label="English"></video>
<script>const syphonRoots=[];const original=Element.prototype.attachShadow;Element.prototype.attachShadow=function(o){const r=original.call(this,o);syphonRoots.push(r);return r};
window.chrome={runtime:{onMessage:{addListener(){},removeListener(){}},sendMessage:async()=>({message:'Fixture only'})},storage:{local:{get:async()=>({detectVideos:true})},onChanged:{addListener(){},removeListener(){}}}};
</script><script>${scripts[0]}</script><script>${scripts[1]}</script>
<script>setTimeout(()=>syphonRoots[0]?.querySelector('.trigger').click(),250)</script>`);
process.stdout.write(path.join(folder, "video-preview.html") + "\n");
