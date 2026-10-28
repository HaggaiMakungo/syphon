import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import { downloadRequest, handoff, supportedLink } from "../extensions/syphon/handoff.js";

function api(send) {
  const state = {}, badges = [];
  return { state, badges, runtime: { sendNativeMessage: send }, storage: { local: {
    get: async key => ({ [key]: structuredClone(state[key]) }),
    set: async values => Object.assign(state, structuredClone(values))
  } }, action: { setBadgeText: async value => badges.push(value.text), setBadgeBackgroundColor: async () => {}, setTitle: async () => {} } };
}
const reply = (request, status = "accepted") => ({ version: 1, requestId: request.requestId, status, taskId: "task", message: status === "accepted" ? "Added to Syphon" : "Not found" });

test("only explicit HTTP links without credentials are eligible", () => {
  for (const url of ["file:///C:/private.txt", "javascript:alert(1)", "blob:https://site/a", "data:text/plain,hi", "https://user:secret@site/a", "x"]) assert.equal(supportedLink(url), false);
  assert.equal(supportedLink("https://site/file?signature=a%2Fb&x=1"), true);
  assert.throws(() => downloadRequest("file:///private"));
});
test("signed URL is passed unchanged and separate clicks get separate IDs", () => {
  const url = "https://site/file?token=a%2Fb&signature=AbC%2B";
  const a = downloadRequest(url), b = downloadRequest(url);
  assert.equal(a.url, url); assert.notEqual(a.requestId, b.requestId);
});
test("pending request is persisted before contacting native host", async () => {
  const request = downloadRequest("https://site/file");
  const mock = api(async () => { assert.deepEqual(mock.state.lastHandoff.request, request); return reply(request); });
  await handoff(mock, request);
});
test("confirmed handoff drops signed URL from extension history", async () => {
  const request = downloadRequest("https://site/file?secret=one"); const mock = api(async () => reply(request));
  const response = await handoff(mock, request);
  assert.equal(response.status, "accepted"); assert.equal(mock.state.lastHandoff.request.action, "status");
  assert.equal(JSON.stringify(mock.state).includes("secret=one"), false); assert.equal(mock.badges.at(-1), "");
});
test("cancelling download options clears the pending URL without reporting a queued task", async () => {
  const request = downloadRequest("https://site/file?secret=one", undefined, { showOptions: true });
  const mock = api(async () => ({ version: 1, requestId: request.requestId, status: "cancelled", message: "Download cancelled." }));
  const response = await handoff(mock, request);
  assert.equal(response.status, "cancelled"); assert.equal(response.taskId, undefined);
  assert.equal(mock.state.lastHandoff.request.action, "status"); assert.equal(JSON.stringify(mock.state).includes("secret=one"), false);
  assert.equal(mock.badges.at(-1), "");
});
test("missing native host retains exact request for safe retry", async () => {
  const calls = []; const request = downloadRequest("https://site/file");
  const mock = api(async (_, value) => { calls.push(value); throw new Error("host missing"); });
  const response = await handoff(mock, request);
  assert.equal(response.status, "unavailable"); assert.equal(mock.badges.at(-1), "!");
  await handoff(mock, mock.state.lastHandoff.request);
  assert.deepEqual(calls, [request, request]);
});
test("mismatched acknowledgment cannot erase pending URL or report success", async () => {
  const request = downloadRequest("https://site/file"); const mock = api(async () => ({ ...reply(request), requestId: crypto.randomUUID() }));
  const response = await handoff(mock, request);
  assert.equal(response.status, "unavailable"); assert.equal(mock.state.lastHandoff.request.url, request.url);
});
test("status miss preserves uncertain original download request", async () => {
  const request = downloadRequest("https://site/file"); let missing = true;
  const mock = api(async (_, value) => { if (missing) throw new Error("offline"); return reply(value, "not_found"); });
  await handoff(mock, request); missing = false;
  await handoff(mock, { version: 1, action: "status", requestId: request.requestId });
  assert.deepEqual(mock.state.lastHandoff.request, request);
});
test("late failure from another click cannot replace newer successful handoff", async () => {
  const old = downloadRequest("https://site/old"), newer = downloadRequest("https://site/new"); let release;
  const mock = api((_, r) => r.requestId === old.requestId ? new Promise(resolve => { release = resolve; }) : Promise.resolve(reply(r)));
  const pending = handoff(mock, old);
  while (!release) await new Promise(resolve => setTimeout(resolve, 1));
  await handoff(mock, newer); release({ ...reply(old), status: "unavailable" }); await pending;
  assert.equal(mock.state.lastHandoff.request.requestId, newer.requestId); assert.equal(mock.badges.at(-1), "");
});
test("removed task receipt reports removal without creating a new request", async () => {
  const request = downloadRequest("https://site/file"); const mock = api(async () => reply(request, "removed"));
  await handoff(mock, request); assert.equal(mock.state.lastHandoff.response.status, "removed"); assert.equal(mock.state.lastHandoff.request.action, "status");
});
test("manifest has bounded explicit capabilities and no interception permissions", async () => {
  const manifest = JSON.parse(await fs.readFile(new URL("../extensions/syphon/manifest.json", import.meta.url), "utf8"));
  assert.equal(manifest.manifest_version, 3); assert.equal(manifest.name, "Syphon this"); assert.ok(manifest.key);
  assert.deepEqual(manifest.permissions.toSorted(), ["activeTab", "contextMenus", "nativeMessaging", "storage", "notifications"].toSorted());
  assert.equal(manifest.host_permissions, undefined); assert.equal(manifest.content_scripts, undefined);
  assert.deepEqual(manifest.optional_permissions, ["cookies", "downloads", "scripting", "webRequest"]);
  assert.deepEqual(manifest.optional_host_permissions, ["http://*/*", "https://*/*"]);
});
test("service worker hands links and direct media sources to Syphon", async () => {
  let installed, clicked, menu, messageHandler;
  const requests = [], notifications = [];
  const mock = api(async (_, request) => { requests.push(request); return reply(request); });
  globalThis.chrome = { ...mock, permissions: { contains: async () => false, onRemoved: { addListener() {} }, onAdded: { addListener() {} } },
    scripting: { getRegisteredContentScripts: async () => [] }, tabs: { query: async () => [] },
    runtime: { ...mock.runtime, id: "fixture", onMessage: { addListener(f) { messageHandler = f; } }, onInstalled: { addListener: f => { installed = f; } }, onStartup: { addListener() {} } },
    notifications: { create: async value => notifications.push(value) },
    contextMenus: { removeAll: async () => {}, create: value => { menu = value; }, onClicked: { addListener: f => { clicked = f; } } } };
  try {
  await import("../extensions/syphon/background.js"); await installed();
  assert.equal(menu.title, "Syphon this"); assert.deepEqual(menu.contexts, ["link", "video", "audio"]);
  assert.deepEqual(menu.targetUrlPatterns, ["http://*/*", "https://*/*"]); assert.equal(typeof clicked, "function");
  const video = "https://dn720409.ca.archive.org/0/items/example/Video%20%281080p%29.mp4?token=a%2Fb";
  await clicked({ menuItemId: menu.id, mediaType: "video", srcUrl: video, linkUrl: "https://site/wrapper" });
  await clicked({ menuItemId: menu.id, mediaType: "audio", srcUrl: "https://site/audio.mp3" });
  await clicked({ menuItemId: menu.id, linkUrl: "https://site/download.zip" });
  assert.deepEqual(requests.map(r => r.url), [video, "https://site/audio.mp3", "https://site/download.zip"]);
  await clicked({ menuItemId: menu.id, mediaType: "video", srcUrl: "blob:https://site/id", linkUrl: "https://site/wrapper" });
  assert.equal(requests.length, 3); assert.match(notifications.at(-1).message, /direct HTTP or HTTPS/);
  await clicked({ menuItemId: "other", linkUrl: "https://site/ignored" });
  assert.equal(requests.length, 3);
  // Downloads API can become available only after the optional permission is granted.
  let downloadListener;
  chrome.downloads = { onCreated: { removeListener() {}, addListener(f) { downloadListener = f; } } };
  await new Promise(resolve => messageHandler({ type: "syphon-settings" }, { id: "fixture" }, resolve));
  assert.equal(typeof downloadListener, "function");
  } finally { delete globalThis.chrome; }
});
