import { test } from "node:test";
import assert from "node:assert/strict";
import { createMediaNetwork, mediaResponse } from "../extensions/syphon/media-network.js";
import { downloadRequest } from "../extensions/syphon/handoff.js";

const response = (patch = {}) => ({ url: "https://cdn.test/master.m3u8?token=a%2Fb", statusCode: 200, tabId: 7, frameId: 0, type: "xmlhttprequest", responseHeaders: [], ...patch });
test("hidden manifest discovery recognizes MIME types without reading response bodies", () => {
  assert.equal(mediaResponse(response()).manifest, true);
  assert.equal(mediaResponse(response({ url: "https://cdn.test/endpoint", responseHeaders: [{ name: "Content-Type", value: "application/dash+xml" }] })).manifest, true);
  for (const patch of [{ incognito: true }, { tabId: -1 }, { statusCode: 403 }, { url: "https://cdn.test/chunk.m4s" }, { url: "https://cdn.test/license" }, { url: "file:///secret.mpd" }])
    assert.equal(mediaResponse(response(patch)), null);
});
test("manifest inventory is tab-scoped, deduplicated and cleared on navigation", async () => {
  const state = {}, api = { permissions: { contains: async () => true }, storage: {
    local: { get: async () => ({ detectVideos: true }) }, session: { get: async () => structuredClone(state), set: async values => Object.assign(state, structuredClone(values)) }
  } };
  const notifications = [];
  const network = createMediaNetwork(api, tabId => { assert.equal(state.mediaTabs[tabId].sources.length > 0, true); notifications.push(tabId); });
  await Promise.all([network.observe(response()), network.observe(response())]);
  assert.equal((await network.sources(7)).length, 1); assert.equal((await network.sources(8)).length, 0);
  assert.deepEqual(notifications, [7], "Only newly recorded sources should notify players");
  assert.equal((await network.sources(7))[0].url, response().url);
  for (let i = 0; i < 30; i++) await network.observe(response({ url: `https://cdn.test/${i}.mpd` }));
  assert.equal((await network.sources(7)).length, 24);
  await network.observe(response({ type: "main_frame", url: "https://site.test/new-page" }));
  assert.equal((await network.sources(7)).length, 0);
  api.permissions.contains = async () => false; await network.observe(response());
  assert.equal((await network.sources(7)).length, 0);
});
test("media request options preserve format and selected stream IDs on retry", () => {
  const id = crypto.randomUUID(), url = "https://cdn.test/master.mpd?token=a%2Fb";
  const request = downloadRequest(url, id, { format: "mp4", videoStream: 3, audioStream: 4 });
  assert.deepEqual(request, { version: 1, action: "download", requestId: id, url, format: "mp4", videoStream: 3, audioStream: 4 });
  assert.throws(() => downloadRequest(url, id, { format: "exe" }));
  assert.throws(() => downloadRequest(url, id, { format: "mp4", videoStream: -1 }));
  assert.equal(downloadRequest("https://www.youtube.com/watch?v=YE7VzlLtp-4", id, { format: "mp4", youtubeFormat: "248-sr" }).youtubeFormat, "248-sr");
  assert.throws(() => downloadRequest(url, id, { format: "mp4", youtubeFormat: "137+--exec" }));
  assert.equal(downloadRequest(url, id, { title: "A video: title?" }).title, "A video: title?");
  assert.throws(() => downloadRequest(url, id, { title: "a".repeat(513) }));
  assert.throws(() => downloadRequest(url, id, { title: {} }));
  const options = downloadRequest(url, id, { showOptions: true, quality: "1080p", estimatedBytes: 125000000 });
  assert.equal(options.showOptions, true); assert.equal(options.quality, "1080p"); assert.equal(options.estimatedBytes, 125000000);
  assert.throws(() => downloadRequest(url, id, { showOptions: "true" }));
  assert.throws(() => downloadRequest(url, id, { estimatedBytes: -1 }));
  assert.throws(() => downloadRequest(url, id, { quality: "a".repeat(121) }));
});
