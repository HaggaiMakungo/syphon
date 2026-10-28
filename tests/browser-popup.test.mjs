import { probeDesktop } from "../extensions/syphon/connection.js";
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { createRequire } from "node:module";
import { openPageLinks } from "../extensions/syphon/page-actions.js";
import { handoff, downloadRequest, supportedLink } from "../extensions/syphon/handoff.js";
import { VIDEO_PERMISSION } from "../extensions/syphon/detection.js";
import { SIGNIN_PERMISSION } from "../extensions/syphon/request-context.js";
import { orbitalIcons } from "../extensions/syphon/orbital-icons.js";
const { JSDOM } = createRequire(new URL("../.tools/extension-test/package.json", import.meta.url))("jsdom");
const code = name => fs.readFile(new URL(`../extensions/syphon/${name}`, import.meta.url), "utf8");
const tick = () => new Promise(resolve => setTimeout(resolve, 10));

async function popup(initial = {}, nativeReply, options = {}) {
  const dom = new JSDOM(await code("popup.html"), { url: "https://extension.test/", runScripts: "outside-only" });
  const { window } = dom, state = structuredClone(initial), native = [], permissions = [], badges = [];
  const api = {
    runtime: { id: "fixture", sendMessage: async () => ({ ok: !options.failSettings }), sendNativeMessage: async (_, request) => {
      native.push(request);
      return nativeReply ? nativeReply(request, state) : { version: 1, requestId: request.requestId, status: "accepted", message: "Added to Syphon." };
    } },
    storage: { local: {
      get: async keys => Object.fromEntries((Array.isArray(keys) ? keys : [keys]).map(k => [k, structuredClone(state[k])])),
      set: async values => Object.assign(state, structuredClone(values)),
      remove: async key => { delete state[key]; }
    } },
    permissions: { contains: async () => !options.revoked, request: async value => { permissions.push(value); return !options.declined; } },
    action: { setBadgeText: async v => badges.push(v.text), setBadgeBackgroundColor: async () => {}, setTitle: async () => {} }
  };
  Object.assign(window, { chrome: api, handoff, probeDesktop, VIDEO_PERMISSION, SIGNIN_PERMISSION, orbitalIcons, openPageLinks, supportedLink });
  await window.eval(`(async () => { ${(await code("popup.js")).replace(/^import .*;\r?\n/gm, "")} })()`);
  return { dom, window, state, native, permissions, badges, $: id => window.document.getElementById(id) };
}
const saved = (status = "accepted", message = "Added to Syphon.") => ({ request: { version: 1, action: "status", requestId: "saved-id" }, response: { status, message } });

test('protected download status uses the requested browser message without labelling network errors encrypted',async()=>{
 for(const message of ['Encrypted or protected HLS is not supported.','HTTP 403: access refused.']){
  const p=await popup({lastHandoff:saved('rejected',message)});
  try{assert.equal(p.$('status').textContent,message.startsWith('Encrypted')?"Yeah... I can't download this! Must be encrypted.":message);}
  finally{p.window.close();}
 }
});

test("browser-wide status has explicit scope and old records do not invent a source", async () => {
  const p = await popup({ lastHandoff: saved() });
  try {
    assert.equal(p.$("last-download-label").textContent, "Last browser handoff");
    assert.match(p.window.document.querySelector('.handoff-scope').textContent, /not the current page or video/);
    assert.match(p.$("handoff-context").textContent, /Source unavailable.*Time unavailable/);
  } finally { p.window.close(); }
});

test("source and original handoff time survive status refresh without retaining signed URLs", async () => {
  const p = await popup();
  try {
    const request = downloadRequest("https://media.test/private/video.mp4?token=secret");
    await handoff(p.window.chrome, request);
    const sentAt = p.state.lastHandoff.sentAt;
    assert.equal(p.state.lastHandoff.sourceHost, "media.test");
    assert.equal(JSON.stringify(p.state).includes("secret"), false);
    assert.equal(JSON.stringify(p.state).includes("private/video"), false);
    const reopened = await popup(p.state);
    try {
      assert.match(reopened.$("handoff-context").textContent, /^media\.test · /);
      reopened.$("check").click(); await tick();
      assert.equal(reopened.state.lastHandoff.sentAt, sentAt);
      assert.equal(reopened.state.lastHandoff.sourceHost, "media.test");
      reopened.$("clear").click(); await tick();
      assert.equal(reopened.$("handoff-context").hidden, true);
    } finally { reopened.window.close(); }
  } finally { p.window.close(); }
});

test("Quiet Control renders an actual acknowledgment without claiming a live connection", async () => {
  const p = await popup({ lastHandoff: saved(), captureDownloads: true, detectVideos: true, captureStatus: "The browser transfer was handed to Syphon." });
  try {
    assert.equal(p.$("handoff-title").textContent, "Added to Syphon");
    assert.equal(p.$("status").textContent, "Added to Syphon.");
    assert.equal(p.$("connection-label").textContent, "App replied");
    assert.match(p.$("connection-description").textContent, /last request/);
    assert.equal(p.$("retry").hidden, true);
    assert.equal(p.$("capture-downloads").checked, true);
    assert.equal(p.$("detect-videos").checked, true);
    assert.equal(p.$("browser-signin").checked, false);
    assert.equal(p.$("capture-details").hidden, false);
    assert.equal(p.$("capture-details").open, false);
    assert.equal(p.native.length, 0, "Opening the popup must not launch a native request.");
  } finally { p.window.close(); }
});

test("empty and cleared handoff states leave feature preferences intact", async () => {
  const p = await popup({ lastHandoff: saved(), captureDownloads: true });
  try {
    p.$("clear").click(); await tick();
    assert.equal(p.state.lastHandoff, undefined);
    assert.equal(p.state.captureDownloads, true);
    assert.equal(p.$("handoff-title").textContent, "No recent handoff");
    assert.equal(p.$("connection-label").textContent, "Not checked");
    for (const id of ["retry", "check", "clear"]) assert.equal(p.$(id).hidden, true);
    assert.equal(p.badges.at(-1), "");
    assert.equal(p.native.length, 0);
  } finally { p.window.close(); }
});

test("uncertain status retains the original signed URL and retry ID", async () => {
  const request = downloadRequest("https://site.test/file?token=retry-me");
  const p = await popup({ lastHandoff: { request, response: { status: "unavailable", message: "Waiting for Syphon." } } }, async r => ({ version: 1, requestId: r.requestId, status: r.action === "status" ? "not_found" : "accepted", message: "Native reply" }));
  try {
    assert.equal(p.$("connection-label").textContent, "Check setup");
    p.$("check").click(); await tick();
    assert.equal(p.state.lastHandoff.request.url, request.url);
    assert.equal(p.$("retry").hidden, false);
    p.$("retry").click(); await tick();
    assert.equal(p.native[1].requestId, request.requestId);
    assert.equal(p.native[1].url, request.url);
    assert.equal(p.state.lastHandoff.request.action, "status");
    assert.equal(JSON.stringify(p.state).includes("retry-me"), false);
    assert.equal(p.$("retry").hidden, true);
  } finally { p.window.close(); }
});

test("pending refresh blocks repeat actions and a stale reply cannot overwrite a newer handoff", async () => {
  const request = downloadRequest("https://site.test/old");
  let release;
  const p = await popup({ lastHandoff: { request, response: { status: "unavailable", message: "Waiting" } } }, r => new Promise(resolve => { release = () => resolve({ version: 1, requestId: r.requestId, status: "unavailable", message: "Not confirmed" }); }));
  try {
    p.$("check").click(); await tick();
    for (const id of ["retry", "check", "clear"]) assert.equal(p.$(id).disabled, true);
    p.$("check").click(); p.$("clear").click();
    assert.equal(p.native.length, 1);
    p.state.lastHandoff = { request: { action: "status", requestId: "newer" }, response: { status: "accepted", message: "Newer request" } };
    release(); await tick();
    assert.equal(p.state.lastHandoff.request.requestId, "newer");
    assert.equal(p.$("status").textContent, "Newer request");
    assert.equal(p.$("handoff-card").getAttribute("aria-busy"), "false");
  } finally { p.window.close(); }
});

test("revoked or declined optional permissions never show a feature as enabled", async () => {
  const p = await popup({ captureDownloads: true, detectVideos: true, useBrowserSignin: true }, undefined, { revoked: true, declined: true });
  try {
    for (const id of ["capture-downloads", "detect-videos", "browser-signin"]) assert.equal(p.$(id).checked, false);
    p.$("browser-signin").click(); await tick();
    assert.equal(p.permissions[0].permissions.includes("cookies"), true);
    assert.equal(p.$("browser-signin").checked, false);
    assert.match(p.$("feature-status").textContent, /declined/);
    assert.equal(p.native.length, 0);
  } finally { p.window.close(); }
});

test("failed settings application rolls the feature back and keeps feedback visible", async () => {
  const p = await popup({}, undefined, { failSettings: true });
  try {
    p.$("detect-videos").click(); await tick();
    assert.equal(p.state.detectVideos, false);
    assert.equal(p.$("detect-videos").checked, false);
    assert.equal(p.$("detect-videos").disabled, false);
    assert.match(p.$("feature-status").textContent, /Could not apply/);
  } finally { p.window.close(); }
});

test("native messages render as text and all feature help remains available", async () => {
  const p = await popup({ lastHandoff: saved("rejected", '<img src=x onerror="alert(1)">') });
  try {
    assert.equal(p.$("status").querySelector("img"), null);
    assert.match(p.$("status").textContent, /<img/);
    assert.equal(p.$("handoff-card").dataset.state, "attention");
    for (const id of ["capture-help", "signin-help", "video-help"]) assert.equal(p.$(id).closest("details").open, false);
    assert.match(p.$("signin-help").textContent, /partitioned cookies/);
    assert.match(p.$("video-help").textContent, /Protected playback/);
  } finally { p.window.close(); }
});


test("connection check reaches the native app without replacing the download receipt", async () => {
  const original = saved();
  const p = await popup({ lastHandoff: original }, request => ({ version: 1, requestId: request.requestId, status: 'ready', message: 'Syphon is ready.' }));
  try {
    p.$('connection-details').open = true;
    p.$('check-connection').click(); await tick();
    assert.equal(p.native.length, 1); assert.equal(p.native[0].action, 'ping');
    assert.deepEqual(p.state.lastHandoff, original);
    assert.equal(p.$('connection-label').textContent, 'App replied');
    assert.match(p.$('connection-description').textContent, /connection check at/);
    assert.equal(p.$('check-connection').disabled, false);
    assert.equal(p.permissions.length, 0);
  } finally { p.window.close(); }
});
