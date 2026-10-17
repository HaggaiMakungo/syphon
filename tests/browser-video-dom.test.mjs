// Optional DOM suite: npm install --prefix .tools/extension-test --no-audit --no-fund jsdom@30.1.3
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { createRequire } from "node:module";
const { JSDOM } = createRequire(new URL("../.tools/extension-test/package.json", import.meta.url))("jsdom");
const code = name => fs.readFile(new URL(`../extensions/syphon/${name}`, import.meta.url), "utf8");
const tick = (ms = 10) => new Promise(resolve => setTimeout(resolve, ms));

test("branded trigger remains visible through opening, selecting, closing and reopening", async () => {
  const dom = new JSDOM('<video src="/video.mp4"></video>', { url: 'https://fixture.test/watch', runScripts: 'outside-only' });
  const { window } = dom; let root;
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { root = attach.call(this, options); return root; };
  window.chrome = { runtime: { getURL: p => 'chrome-extension://fixture/' + p, onMessage: { addListener(){},removeListener(){} }, sendMessage: async () => ({sources:[]}) },
    storage: { local: { get: async () => ({detectVideos:true}) }, onChanged: {addListener(){},removeListener(){}} } };
  window.document.querySelector('video').getBoundingClientRect = () => ({ width:640,height:360,top:50,bottom:410,left:30,right:670 });
  try {
    window.eval(await code('media-model.js')); window.eval(await code('video-button.js')); await tick();
    const trigger=root.querySelector('.trigger'), mark=trigger.querySelector('img'), panel=root.querySelector('.panel');
    assert.equal(mark.src,'chrome-extension://fixture/brand/icon-64.png');
    for(let i=0;i<2;i++) {
      trigger.click(); await tick();
      assert.equal(trigger.hidden,false);
      assert.equal(trigger.getAttribute('aria-expanded'),'true');
      assert.equal(panel.hidden,false);
      root.querySelector('.option').click();
      assert.equal(trigger.querySelector('img'),mark);
      assert.equal(trigger.hidden,false);
      root.querySelector('.dismiss').click();
      assert.equal(panel.hidden,true);
      assert.equal(trigger.getAttribute('aria-expanded'),'false');
    }
    trigger.click(); trigger.click(); assert.equal(panel.hidden,true);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("fullscreen hides video overlays immediately and exiting restores only undismissed buttons", async () => {
  const dom = new JSDOM('<div id="player"><video src="/first.mp4"></video></div><video src="/second.mp4"></video>', { url: "https://fixture.test/watch", runScripts: "outside-only" });
  const { window } = dom, roots = [];
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { const root = attach.call(this, options); roots.push(root); return root; };
  window.chrome = { runtime: { onMessage: { addListener() {}, removeListener() {} }, sendMessage: async () => ({ sources: [] }) },
    storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  const videos = [...window.document.querySelectorAll("video")];
  for (const video of videos) video.getBoundingClientRect = () => ({ width: 640, height: 360, top: 50, bottom: 410, left: 30, right: 670 });
  let fullscreen = null;
  Object.defineProperty(window.document, "fullscreenElement", { get: () => fullscreen });
  const setFullscreen = element => { fullscreen = element; window.document.dispatchEvent(new window.Event("fullscreenchange")); };
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    const hosts = [...window.document.querySelectorAll("[data-syphon-video-button]")];
    assert.equal(hosts.length, 2);
    roots[1].querySelector(".dismiss").click();
    assert.equal(hosts[1].hidden, true);
    for (const target of [videos[0], window.document.getElementById("player"), window.document.documentElement]) {
      roots[0].querySelector(".trigger").click();
      assert.equal(roots[0].querySelector(".panel").hidden, false);
      setFullscreen(target);
      assert(hosts.every(host => host.hidden), "The full toolbar, including dismiss, must disappear immediately.");
      assert.equal(roots[0].querySelector(".panel").hidden, true);
      assert.equal(roots[0].querySelector(".trigger").getAttribute("aria-expanded"), "false");
      window.dispatchEvent(new window.Event("resize"));
      assert.equal(hosts[0].hidden, true);
      setFullscreen(null);
      assert.equal(hosts[0].hidden, false);
      assert.equal(hosts[1].hidden, true, "Leaving fullscreen must preserve an explicit dismissal.");
      assert.equal(roots[0].querySelector(".panel").hidden, true, "Leaving fullscreen must not reopen the download menu.");
    }
    window.syphonVideoController.stop();
    assert.equal(window.document.querySelectorAll("[data-syphon-video-button]").length, 0);
    setFullscreen(videos[0]);
    assert.equal(window.document.querySelectorAll("[data-syphon-video-button]").length, 0);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("players discovered during fullscreen stay hidden until fullscreen ends", async () => {
  const dom = new JSDOM('<video src="/first.mp4"></video>', { url: "https://fixture.test/watch", runScripts: "outside-only" });
  const { window } = dom;
  window.chrome = { runtime: { onMessage: { addListener() {}, removeListener() {} } },
    storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  const video = window.document.querySelector("video");
  video.getBoundingClientRect = () => ({ width: 640, height: 360, top: 50, bottom: 410, left: 30, right: 670 });
  let fullscreen = video;
  Object.defineProperty(window.document, "fullscreenElement", { get: () => fullscreen });
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    const host = window.document.querySelector("[data-syphon-video-button]");
    assert.equal(host.hidden, true);
    fullscreen = null; window.document.dispatchEvent(new window.Event("fullscreenchange"));
    assert.equal(host.hidden, false);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("video button lists actual sources and subtitles, hands off selection and cleans up", async () => {
  const dom = new JSDOM('<video><source src="/720.mp4" label="720p"><source src="/1080.mp4" label="1080p"><track kind="subtitles" src="/en.vtt" label="English"></video>', { url: "https://fixture.test/watch", runScripts: "outside-only" });
  const { window } = dom, roots = [], messages = [], listeners = new Set();
  window.document.title = "A real video title";
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { const root = attach.call(this, options); roots.push(root); return root; };
  const onMessage = { addListener: f => listeners.add(f), removeListener: f => listeners.delete(f) };
  window.chrome = { runtime: { onMessage, sendMessage: async request => { messages.push(request); return { message: "Added to Syphon" }; } },
    storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  window.document.querySelector("video").getBoundingClientRect = () => ({ width: 640, height: 360, top: 50, bottom: 410, left: 30, right: 670 });
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    assert.equal(window.document.querySelectorAll("[data-syphon-video-button]").length, 1);
    window.eval(await code("video-button.js")); assert.equal(roots.length, 1);
    const root = roots[0], trigger = root.querySelector(".trigger");
    const right = window.document.querySelector("[data-syphon-video-button]").style.right;
    assert.equal(right, `${window.innerWidth - 670 + 8}px`);
    trigger.click();
    assert.equal(window.document.querySelector("[data-syphon-video-button]").style.right, right);
    assert.equal(trigger.getAttribute("aria-expanded"), "true");
    const buttons = [...root.querySelectorAll(".option")];
    assert.deepEqual(buttons.map(b => b.textContent), ["720p", "1080p", "English", "Convert audio to MP3"]);
    assert.equal(buttons.at(-1).disabled, false);
    buttons[1].click(); root.querySelector(".confirm").click(); await tick(); assert.equal(messages.filter(m => m.type === "syphon-media-download")[0].url, "https://fixture.test/1080.mp4");
    assert.equal(messages.filter(m => m.type === "syphon-media-download")[0].title, "A real video title");
    assert.equal(messages.filter(m => m.type === "syphon-media-download")[0].showOptions, true);
    assert.equal(messages.filter(m => m.type === "syphon-media-download")[0].quality, "1080p");
    assert.equal(root.querySelector('[role="status"]').textContent, "Added to Syphon");
    [...root.querySelectorAll(".option")][2].click(); root.querySelector(".confirm").click(); await tick(); assert.equal(messages.filter(m => m.type === "syphon-media-download")[1].url, "https://fixture.test/en.vtt");
    [...root.querySelectorAll(".option")].at(-1).click(); root.querySelector(".confirm").click(); await tick(); assert.equal(messages.filter(m => m.type === "syphon-media-download")[2].format, "mp3");
    root.dispatchEvent(new window.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    assert.equal(trigger.getAttribute("aria-expanded"), "false");
    const dynamic = window.document.createElement("video"); dynamic.src = "https://fixture.test/new.mp4"; window.document.body.append(dynamic);
    await tick(200); assert.equal(window.document.querySelectorAll("[data-syphon-video-button]").length, 2);
    dynamic.remove(); await tick(200); assert.equal(window.document.querySelectorAll("[data-syphon-video-button]").length, 1);
    for (const listener of [...listeners]) listener({ type: "syphon-detection-off" });
    assert.equal(window.document.querySelectorAll("[data-syphon-video-button]").length, 0);
    assert.equal(listeners.size, 0); assert.equal(window.syphonVideoController, undefined);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("outside clicks close the menu and dismiss stays hidden until a different video", async () => {
  const dom = new JSDOM('<video src="/first.mp4"></video><button id="outside">Outside</button>', { url: "https://fixture.test/watch", runScripts: "outside-only" });
  const { window } = dom; let root;
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { root = attach.call(this, options); return root; };
  window.chrome = { runtime: { onMessage: { addListener() {}, removeListener() {} }, sendMessage: async () => ({ sources: [], media: { qualities: [{ height: 720, codec: "h264" }] } }) },
    storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  const video = window.document.querySelector("video");
  video.getBoundingClientRect = () => ({ width: 640, height: 360, top: 50, bottom: 410, left: 30, right: 670 });
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    const host = window.document.querySelector("[data-syphon-video-button]"), trigger = root.querySelector(".trigger"), panel = root.querySelector(".panel");
    trigger.click(); await tick(); assert.equal(panel.hidden, false);
    root.querySelector('[role="status"]').dispatchEvent(new window.MouseEvent("click", { bubbles: true, composed: true }));
    assert.equal(panel.hidden, false, "Closed shadow root clicks must count as inside");
    window.document.getElementById("outside").dispatchEvent(new window.Event("pointerdown", { bubbles: true }));
    assert.equal(panel.hidden, true); assert.equal(trigger.getAttribute("aria-expanded"), "false");
    trigger.click(); window.document.getElementById("outside").click(); assert.equal(panel.hidden, true);
    trigger.click(); assert.equal(panel.hidden, false);
    root.querySelector(".dismiss").click(); root.querySelector(".dismiss").click(); assert.equal(host.hidden, true); assert.equal(panel.hidden, true);
    window.dispatchEvent(new window.Event("resize")); await tick(200); assert.equal(host.hidden, true);
    Object.defineProperty(video, "currentSrc", { get: () => video.src, configurable: true });
    video.dispatchEvent(new window.Event("loadedmetadata")); assert.equal(host.hidden, true);
    video.src = "/second.mp4"; video.dispatchEvent(new window.Event("loadedmetadata")); assert.equal(host.hidden, false);
    trigger.click(); assert.equal(panel.hidden, false);
    root.dispatchEvent(new window.KeyboardEvent("keydown", { key: "Escape", bubbles: true })); assert.equal(panel.hidden, true);
    root.querySelector(".dismiss").click();
    const replacement = video.cloneNode(true); replacement.getBoundingClientRect = video.getBoundingClientRect;
    video.replaceWith(replacement); await tick(200);
    assert.equal(window.document.querySelector("[data-syphon-video-button]").hidden, true, "Replacing the player must preserve dismissal for the same video");
    window.syphonVideoController.stop(); window.document.body.click(); assert.equal(host.isConnected, false);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("playback starts detection without hovering, duplicate players share work and expired results stay visible while refreshing", async () => {
  const dom = new JSDOM('<video></video><video></video>', { url: "https://www.youtube.com/watch?v=YE7VzlLtp-4", runScripts: "outside-only" });
  const { window } = dom, roots = [], pending = []; let clock = 100000;
  window.Date.now = () => clock;
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { const root = attach.call(this, options); roots.push(root); return root; };
  window.chrome = { runtime: { onMessage: { addListener() {}, removeListener() {} }, sendMessage: request => {
    if(request.type === "syphon-player-info") return Promise.resolve({}); assert.equal(request.type, "syphon-media-inspect"); return new Promise(resolve => pending.push(resolve));
  } }, storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    assert.equal(pending.length, 0, "Idle players should not start native work");
    window.document.querySelector("video").dispatchEvent(new window.Event("play")); await tick();
    assert.equal(pending.length, 1); assert.equal(roots[0].querySelector(".panel").hidden, true);
    window.document.querySelectorAll("video")[1].dispatchEvent(new window.Event("playing")); await tick(); assert.equal(pending.length, 1);
    roots[1].querySelector(".trigger").click(); await tick(); assert.equal(pending.length, 1);
    pending[0]({ media: { qualities: [{ height: 1080, codec: "h264", estimatedBytes: 125000000 }] } }); await tick();
    assert.equal(roots[1].querySelectorAll(".option")[1].textContent, "1080p · About 125 MB");
    roots[0].querySelector(".trigger").click(); await tick(); assert.equal(pending.length, 1);
    assert.equal(roots[0].querySelectorAll(".option")[1].textContent, "1080p · About 125 MB");
    clock += 60001;
    roots[0].querySelector(".trigger").click(); roots[0].querySelector(".trigger").click(); await tick(); assert.equal(pending.length, 2);
    assert.equal(roots[0].querySelectorAll(".option")[1].textContent, "1080p · About 125 MB");
    roots[0].querySelector(".dismiss").click(); roots[0].querySelector(".dismiss").click();
    pending[1]({ media: { qualities: [{ height: 720, codec: "h264" }] } }); await tick();
    assert.equal(window.document.querySelector("[data-syphon-video-button]").hidden, true, "A late result must not restore a dismissed overlay");
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("already playing videos detect late streams automatically and playback listeners clean up", async () => {
  const dom = new JSDOM('<video src="blob:https://fixture.test/player"></video>', { url: "https://fixture.test/watch", runScripts: "outside-only" });
  const { window } = dom, listeners = new Set(), messages = []; let root, sources = [];
  const video = window.document.querySelector("video");
  Object.defineProperty(video, "paused", { get: () => false });
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { root = attach.call(this, options); return root; };
  window.chrome = { runtime: { onMessage: { addListener: f => listeners.add(f), removeListener: f => listeners.delete(f) }, sendMessage: async request => {
    messages.push(request);
    if (request.type === "syphon-media-sources") return { sources };
    assert.equal(request.type, "syphon-media-inspect");
    return { media: { qualities: [{ height: 1080, codec: "h264", estimatedBytes: 40000000 }, { height: 720, codec: "h264", estimatedBytes: 20000000 }] } };
  } }, storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    assert.equal(messages[0].type, "syphon-media-sources"); assert.equal(root.querySelector(".panel").hidden, true);
    sources = [{ url: "https://cdn.test/master.mpd", manifest: true }];
    for (const listener of listeners) listener({ type: "syphon-media-found" });
    await tick(200);
    assert.equal(messages.filter(m => m.type === "syphon-media-inspect").length, 1);
    assert.match(root.textContent, /1080p · another video on this page · About 40 MB/);
    assert.equal(root.querySelector(".panel").hidden, true);
    root.querySelector(".trigger").click(); await tick();
    assert.match(root.querySelector(".option").textContent, /About 40 MB/);
    assert.equal(messages.filter(m => m.type === "syphon-media-inspect").length, 1);
    root.querySelector(".dismiss").click();
    for (const listener of listeners) listener({ type: "syphon-media-found" });
    await tick(200); assert.equal(root.querySelector(".panel").hidden, true);
    const beforeStop = messages.length; window.syphonVideoController.stop();
    video.dispatchEvent(new window.Event("play")); video.dispatchEvent(new window.Event("playing")); await tick(200);
    assert.equal(messages.length, beforeStop); assert.equal(listeners.size, 0);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("playing labeled direct sources estimates sizes before the menu opens", async () => {
  const dom = new JSDOM('<video><source src="/movie.mp4" label="720p"></video>', { url: "https://fixture.test/watch", runScripts: "outside-only" });
  const { window } = dom, messages = []; let root;
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { root = attach.call(this, options); return root; };
  window.chrome = { runtime: { onMessage: { addListener() {}, removeListener() {} }, sendMessage: async request => {
    messages.push(request);
    return request.type === "syphon-media-sources" ? { sources: [] } : { media: { qualities: [{ height: 720, codec: "h264", estimatedBytes: 15000000 }] } };
  } }, storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    const video = window.document.querySelector("video"); video.dispatchEvent(new window.Event("play")); await tick();
    assert.equal(root.querySelector(".option").textContent, "720p · About 15 MB");
    assert.equal(root.querySelector(".panel").hidden, true);
    video.dispatchEvent(new window.Event("playing")); video.dispatchEvent(new window.Event("play")); await tick();
    assert.equal(messages.filter(m => m.type === "syphon-media-inspect").length, 1);
    assert.equal(messages.some(m => m.type === "syphon-media-download"), false);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("stream inspections run two at a time and render completed sizes without waiting for a slow source", async () => {
  const dom = new JSDOM('<video src="blob:https://fixture.test/player"></video>', { url: "https://fixture.test/watch", runScripts: "outside-only" });
  const { window } = dom; let root; const pending = [];
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { root = attach.call(this, options); return root; };
  window.chrome = { runtime: { onMessage: { addListener() {}, removeListener() {} }, sendMessage: async request => {
    if (request.type === "syphon-media-sources") return { sources: [1, 2, 3, 4].map(n => ({ url: `https://cdn.test/${n}.mpd`, manifest: true })) };
    return await new Promise(resolve => pending.push({ url: request.url, resolve }));
  } }, storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    root.querySelector(".trigger").click(); await tick(); assert.equal(pending.length, 2);
    pending[1].resolve({ media: { qualities: [{ height: 720, codec: "h264", estimatedBytes: 10000000 }] } }); await tick();
    assert.equal(pending.length, 3); assert.match(root.textContent, /720p · another video on this page · About 10 MB/);
    pending[2].resolve({ message: "Unavailable source" }); await tick(); assert.equal(pending.length, 4);
    pending[3].resolve({ media: { qualities: [{ height: 480, codec: "h264" }] } });
    pending[0].resolve({ media: { qualities: [{ height: 1080, codec: "h264" }] } }); await tick();
    assert.match(root.textContent, /1080p/); assert.match(root.textContent, /Unavailable source/);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("preferred quality survives reloads, sizes stay readable and unavailable or failed choices do not silently replace it", async () => {
  const dom = new JSDOM('<video src="blob:https://www.youtube.com/player"></video>', { url: "https://www.youtube.com/watch?v=YE7VzlLtp-4", runScripts: "outside-only" });
  const { window } = dom; let root, accepted = true, unavailable = false;
  const state = { detectVideos: true, preferredVideoQuality: 720 }, requests = [], changes = new Set();
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { root = attach.call(this, options); return root; };
  window.chrome = { runtime: { onMessage: { addListener() {}, removeListener() {} }, sendMessage: async request => {
    requests.push(request);
    if (request.type === "syphon-media-inspect") return { media: { qualities: [
      ...(!unavailable ? [{ height: 1080, codec: "avc1", youtubeFormat: "137", estimatedBytes: 125000000 }] : []),
      { height: 720, codec: "avc1", youtubeFormat: "136", estimatedBytes: null }
    ] } };
    return { status: accepted ? "accepted" : "unavailable", message: accepted ? "Added" : "Retry" };
  } }, storage: { local: { get: async () => ({ ...state }), set: async values => Object.assign(state, values) },
    onChanged: { addListener: f => changes.add(f), removeListener: f => changes.delete(f) } } };
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    root.querySelector(".trigger").click(); await tick();
    assert.deepEqual([...root.querySelectorAll(".option")].map(b => b.textContent), ["Best available", "1080p · About 125 MB", "720p · Preferred", "Convert audio to MP3"]);
    assert(root.textContent.includes("720p · Preferred")); assert.equal(root.querySelector('[aria-pressed="true"]'), null);
    root.querySelectorAll(".option")[1].click(); root.querySelector(".confirm").click(); await tick(); assert.equal(state.preferredVideoQuality, 1080);
    assert.match(root.querySelector('[aria-pressed="true"]').textContent, /1080p · Preferred/);
    accepted = false; root.querySelectorAll(".option")[2].click(); root.querySelector(".confirm").click(); await tick(); assert.equal(state.preferredVideoQuality, 1080);
    accepted = true; [...root.querySelectorAll(".option")].at(-1).click(); root.querySelector(".confirm").click(); await tick(); assert.equal(state.preferredVideoQuality, 1080);
    window.syphonVideoController.stop(); window.eval(await code("video-button.js")); await tick();
    root.querySelector(".trigger").click(); await tick(); assert.match(root.textContent,/1080p · Preferred/); assert.equal(root.querySelector('[aria-pressed="true"]'),null);
    unavailable = true; window.history.pushState({}, "", "/shorts/BaW_jenozKc");
    root.querySelector(".trigger").click(); root.querySelector(".trigger").click(); await tick();
    assert.match(root.textContent, /preferred quality \(1080p\) isn't available/);
    assert.equal(root.querySelector('[aria-pressed="true"]'), null); assert.equal(state.preferredVideoQuality, 1080);
    assert.equal(requests.filter(r => r.type === "syphon-media-download").length, 3);
    state.preferredVideoQuality = 720;
    for (const changed of changes) changed({ preferredVideoQuality: { newValue: 720 } }, "local");
    assert(root.textContent.includes("720p · Preferred")); assert.equal(root.querySelector('[aria-pressed="true"]'), null);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("popup requests optional permissions on selection and persists feature switches", async () => {
  const dom = new JSDOM(await code("popup.html"), { url: "https://extension.test/", runScripts: "outside-only" });
  const { window } = dom, state = {}, requests = [];
  let granted = true;
  window.SIGNIN_PERMISSION = { permissions: ["cookies"], origins: ["http://*/*", "https://*/*"] };
  window.VIDEO_PERMISSION = { permissions: ["scripting"], origins: ["http://*/*", "https://*/*"] };
  window.chrome = { runtime: { id: "fixture", sendMessage: async () => ({ ok: true }) },
    storage: { local: { get: async () => state, set: async values => Object.assign(state, values) } },
    permissions: { contains: async () => true, request: async permission => { requests.push(permission); return granted; } } };
  try {
    const script = (await code("popup.js")).replace(/^import .*;\r?\n/gm, "");
    await window.eval(`(async () => { ${script} })()`);
    const capture = window.document.getElementById("capture-downloads"), video = window.document.getElementById("detect-videos");
    assert.equal(capture.checked, false); assert.equal(video.checked, false);
    capture.click(); await tick(); assert.equal(state.captureDownloads, true); assert.equal(requests[0].permissions[0], "downloads");
    video.click(); await tick(); assert.equal(state.detectVideos, true); assert.equal(requests[1].permissions[0], "scripting");
    const signin = window.document.getElementById("browser-signin");
    assert.equal(signin.checked, false); signin.click(); await tick(); assert.equal(state.useBrowserSignin, true); assert.equal(requests[2].permissions[0], "cookies");
    video.click(); await tick(); assert.equal(state.detectVideos, false);
    granted = false; video.click(); await tick(); assert.equal(video.checked, false); assert.equal(state.detectVideos, false);
    assert.match(window.document.getElementById("feature-status").textContent, /declined/);
  } finally { window.close(); }
});

test("blob players show inspected hidden qualities and protected players disable extraction", async () => {
  const dom = new JSDOM('<video src="blob:https://fixture.test/id"></video>', { url: "https://fixture.test/watch", runScripts: "outside-only" });
  const { window } = dom; let root, selected;
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { root = attach.call(this, options); return root; };
  const video = window.document.querySelector("video");
  window.chrome = { runtime: { onMessage: { addListener() {}, removeListener() {} }, sendMessage: async request => {
    if (request.type === "syphon-media-sources") return { sources: [{ url: "https://cdn.test/master.mpd", manifest: true }] };
    if (request.type === "syphon-media-inspect") return { media: { qualities: [{ videoStream: 2, audioStream: 3, height: 1080, codec: "h264" }, { videoStream: 0, audioStream: 3, height: 720, codec: "h264" }] } };
    selected = request; return { message: "Added" };
  } }, storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    root.querySelector(".trigger").click(); await tick();
    let buttons = [...root.querySelectorAll(".option")];
    assert.equal(buttons[0].textContent, "1080p · another video on this page"); assert.equal(buttons[1].textContent, "720p · another video on this page");
    buttons[1].click(); root.querySelector(".confirm").click(); await tick(); assert.equal(selected.videoStream, 0); assert.equal(selected.audioStream, 3); assert.equal(selected.format, "mp4");
    Object.defineProperty(video, "mediaKeys", { value: {}, configurable: true }); video.dispatchEvent(new window.Event("loadedmetadata"));
    buttons = [...root.querySelectorAll(".option")]; assert.equal(buttons.every(b => b.disabled), true);
    assert.match(root.textContent, /Protected video cannot be downloaded/);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("YouTube button uses page extraction, passes format IDs and follows video navigation", async () => {
  const dom = new JSDOM('<video src="blob:https://www.youtube.com/player"></video>', { url: "https://www.youtube.com/watch?v=YE7VzlLtp-4", runScripts: "outside-only" });
  const { window } = dom; let root; const requests = [];
  const attach = window.Element.prototype.attachShadow;
  window.Element.prototype.attachShadow = function(options) { root = attach.call(this, options); return root; };
  window.chrome = { runtime: { onMessage: { addListener() {}, removeListener() {} }, sendMessage: async request => {
    requests.push(request);
    if (request.type === "syphon-media-inspect") return { media: { qualities: [
      { videoStream: 0, height: 1080, codec: "vp9", youtubeFormat: "248-sr", bitrate: 3000000 },
      { videoStream: 0, height: 1080, codec: "av01.0.08M.08", youtubeFormat: "399", bitrate: 2000000 },
      { videoStream: 0, height: 1080, codec: "avc1.640028", youtubeFormat: "137", bitrate: 4000000 },
      { videoStream: 0, height: 720, codec: "vp9", youtubeFormat: "247" }
    ] } };
    return { message: "Added" };
  } }, storage: { local: { get: async () => ({ detectVideos: true }) }, onChanged: { addListener() {}, removeListener() {} } } };
  try {
    window.eval(await code("media-model.js")); window.eval(await code("video-button.js")); await tick();
    root.querySelector(".trigger").click(); await tick();
    assert.equal(requests.some(r => r.type === "syphon-media-sources"), false);
    assert.equal(requests[0].url, "https://www.youtube.com/watch?v=YE7VzlLtp-4");
    assert.deepEqual([...root.querySelectorAll(".option")].map(button => button.textContent), ["Best available", "1080p", "720p", "Convert audio to MP3"]);
    root.querySelectorAll(".option")[1].click(); root.querySelector(".confirm").click(); await tick();
    const download = requests.find(r => r.type === "syphon-media-download"); assert.equal(download.youtubeFormat, "137"); assert.equal(download.videoStream, undefined);
    [...root.querySelectorAll(".option")].at(-1).click(); root.querySelector(".confirm").click(); await tick();
    assert.equal(requests.at(-1).format, "mp3"); assert.equal(requests.at(-1).youtubeFormat, undefined);
    window.history.pushState({}, "", "/shorts/BaW_jenozKc");
    root.querySelector(".trigger").click(); root.querySelector(".trigger").click(); await tick();
    assert.equal(requests.at(-1).url, "https://www.youtube.com/watch?v=BaW_jenozKc");
    assert.equal(window.SyphonMedia.youtubeUrl("https://youtube.com.evil.test/watch?v=YE7VzlLtp-4"), null);
  } finally { window.syphonVideoController?.stop(); window.close(); }
});

test("browser metadata supplies qualities and captions without native inspection", async()=>{
 const dom=new JSDOM('<video></video>',{url:'https://www.youtube.com/watch?v=YE7VzlLtp-4',runScripts:'outside-only'}),{window}=dom;let root;const requests=[];const attach=window.Element.prototype.attachShadow;window.Element.prototype.attachShadow=function(o){root=attach.call(this,o);return root;};
 window.chrome={runtime:{onMessage:{addListener(){},removeListener(){}},sendMessage:async r=>{requests.push(r);if(r.type==='syphon-player-info')return {media:{qualities:[{height:720,youtubeFormat:'136',codec:'avc1'}],subtitles:[{url:'https://www.youtube.com/api/timedtext?v=YE7VzlLtp-4',label:'English'}]}};return {status:'accepted',message:'Added'};}},storage:{local:{get:async()=>({detectVideos:true})},onChanged:{addListener(){},removeListener(){}}}};
 try{window.eval(await code('media-model.js'));window.eval(await code('video-button.js'));await tick();window.document.querySelector('video').dispatchEvent(new window.Event('play'));await tick();root.querySelector('.trigger').click();await tick();assert(root.textContent.includes('720p'));assert(root.textContent.includes('English'));assert(!requests.some(r=>r.type==='syphon-media-inspect'));const choice=[...root.querySelectorAll('.option')].find(b=>b.textContent==='720p');choice.click();root.querySelector('.confirm').click();await tick();assert.equal(requests.find(r=>r.type==='syphon-media-download').youtubeFormat,'136');}
 finally{window.syphonVideoController?.stop();window.close();}
});
test("automatic quality and MP3 can be queued while native fallback is still pending",async()=>{
 const dom=new JSDOM('<video></video>',{url:'https://www.youtube.com/watch?v=YE7VzlLtp-4',runScripts:'outside-only'}),{window}=dom;let root;const requests=[];const attach=window.Element.prototype.attachShadow;window.Element.prototype.attachShadow=function(o){root=attach.call(this,o);return root;};
 window.chrome={runtime:{onMessage:{addListener(){},removeListener(){}},sendMessage:async r=>{requests.push(r);if(r.type==='syphon-player-info')return {};if(r.type==='syphon-media-inspect')return new Promise(()=>{});return {status:'accepted',message:'Added'};}},storage:{local:{get:async()=>({detectVideos:true})},onChanged:{addListener(){},removeListener(){}}}};
 try{window.eval(await code('media-model.js'));window.eval(await code('video-button.js'));await tick();root.querySelector('.trigger').click();await tick();root.querySelector('.option').click();root.querySelector('.confirm').click();await tick();assert.equal(requests.find(r=>r.type==='syphon-media-download').youtubeFormat,undefined);[...root.querySelectorAll('.option')].at(-1).click();root.querySelector('.confirm').click();await tick();assert.equal(requests.at(-1).format,'mp3');}
 finally{window.syphonVideoController?.stop();window.close();}
});

test('in-band closed captions are detected without pretending a separate file is available',async()=>{
 const dom=new JSDOM('<video src="/video.mp4"></video>',{url:'https://fixture.test/',runScripts:'outside-only'}),{window}=dom;
 try{const video=window.document.querySelector('video');Object.defineProperty(video,'textTracks',{value:[{kind:'captions',label:'English CC',language:'en',mode:'disabled'}]});window.eval(await code('media-model.js'));const cc=window.SyphonMedia.collect(video).options.find(o=>o.label==='English CC');assert(cc);assert.equal(cc.supported,false);assert.match(cc.note,/Embedded/);assert.equal(video.textTracks[0].mode,'disabled');}
 finally{window.close();}
});

test('captions are collapsed by default, preserve expansion while selecting, and reset on reopening',async()=>{
 const dom=new JSDOM('<video><source src="/video.mp4" label="1080p"><track kind="captions" src="/en.vtt" label="English"></video>',{url:'https://fixture.test/watch',runScripts:'outside-only'}),{window}=dom;let root;const attach=window.Element.prototype.attachShadow;window.Element.prototype.attachShadow=function(o){root=attach.call(this,o);return root;};const requests=[];
 window.chrome={runtime:{onMessage:{addListener(){},removeListener(){}},sendMessage:async r=>{requests.push(r);return r.type==='syphon-media-download'?{status:'accepted',message:'Added'}:{sources:[]};}},storage:{local:{get:async()=>({detectVideos:true})},onChanged:{addListener(){},removeListener(){}}}};
 try{window.eval(await code('media-model.js'));window.eval(await code('video-button.js'));await tick();root.querySelector('.trigger').click();await tick();let group=root.querySelector('.caption-group');assert.equal(group.open,false);assert.equal(group.querySelectorAll('.option').length,1);assert.equal(root.querySelector('.option').textContent,'1080p');assert(group.compareDocumentPosition([...root.querySelectorAll('.option')].at(-1))&window.Node.DOCUMENT_POSITION_FOLLOWING);
 group.querySelector('summary').click();assert.equal(group.open,true);group.querySelector('.option').click();assert.equal(root.querySelector('.caption-group').open,true);root.querySelector('.confirm').click();await tick();assert.equal(requests.find(r=>r.type==='syphon-media-download').url,'https://fixture.test/en.vtt');root.querySelector('.trigger').click();root.querySelector('.trigger').click();await tick();assert.equal(root.querySelector('.caption-group').open,false);
 }finally{window.syphonVideoController?.stop();window.close();}
});
