import { createPlayerMetadata } from './player-metadata.js';
import { HOST, MENU, downloadRequest, handoff, supportedLink, validReply } from "./handoff.js";
import { createCapture } from "./capture.js";
import { syncDetection, VIDEO_PERMISSION } from "./detection.js";
import { createMediaNetwork } from "./media-network.js";
import { SIGNIN_PERMISSION } from "./request-context.js";

const playerMetadata = createPlayerMetadata(chrome);
const capture = createCapture(chrome);
const network = createMediaNetwork(chrome, tabId => chrome.tabs.sendMessage(tabId, { type: "syphon-media-found" }));
network.listen();
const onDownload = item => { capture.run(item).catch(() => {}); };
function listenForDownloads() {
  const event = chrome.downloads?.onCreated;
  if (event) { event.removeListener(onDownload); event.addListener(onDownload); }
}
listenForDownloads();
async function restoreFeatures() {
  listenForDownloads();
  network.listen();
  await Promise.allSettled([capture.recover(), syncDetection(chrome)]);
}
chrome.runtime.onInstalled.addListener(restoreFeatures);
chrome.runtime.onStartup.addListener(restoreFeatures);
chrome.permissions.onAdded.addListener(restoreFeatures);
chrome.permissions.onRemoved.addListener(() => {
  (async () => {
    const updates = {};
    if (!await chrome.permissions.contains({ permissions: ["downloads"] })) updates.captureDownloads = false;
    if (!await chrome.permissions.contains(VIDEO_PERMISSION)) updates.detectVideos = false;
    if (!await chrome.permissions.contains(SIGNIN_PERMISSION)) updates.useBrowserSignin = false;
    await chrome.storage.local.set(updates);
    await restoreFeatures();
  })().catch(() => {});
});
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== chrome.runtime.id) return;
  if (message?.type === "syphon-settings" && !sender.tab) {
    listenForDownloads();
    network.listen();
    syncDetection(chrome).then(() => respond({ ok: true }), () => respond({ ok: false }));
    return true;
  }
  if (message?.type === "syphon-media-download" && sender.tab && !sender.tab.incognito) {
    (async () => {
      const { detectVideos } = await chrome.storage.local.get("detectVideos");
      if (!detectVideos) return { message: "Video detection is switched off." };
      return await handoff(chrome, downloadRequest(message.url, undefined, { ...message, sourcePage: sender.url }));
    })().then(respond, () => respond({ message: "This source could not be sent to Syphon. Use a direct HTTP or HTTPS file." }));
    return true;
  }
  if (["syphon-media-sources", "syphon-media-inspect", "syphon-player-info"].includes(message?.type) && sender.tab && !sender.tab.incognito) {
    (async () => {
      const { detectVideos } = await chrome.storage.local.get("detectVideos");
      if (!detectVideos || !await chrome.permissions.contains(VIDEO_PERMISSION)) return { message: "Enable video detection and its permissions in the extension." };
      if (message.type === "syphon-player-info") { const media = await playerMetadata(message.url, sender); return media ? { media } : {}; }
      if (message.type === "syphon-media-sources") return { sources: await network.sources(sender.tab.id) };
      if (!supportedLink(message.url)) return { message: "A direct HTTP/HTTPS source is required." };
      const request = { version: 1, action: "media-inspect", requestId: crypto.randomUUID(), url: message.url };
      const reply = await chrome.runtime.sendNativeMessage(HOST, request);
      return validReply(reply, request) ? reply : { message: "Invalid media inspection reply." };
    })().then(respond, () => respond({ message: "Media inspection failed. Open Syphon and check that FFmpeg and FFprobe are installed." }));
    return true;
  }
});
// Service workers also restart between browser sessions.
restoreFeatures().catch(() => {});

async function installMenu() {
  await chrome.contextMenus.removeAll();
  chrome.contextMenus.create({ id: MENU, title: "Syphon this", contexts: ["link", "video", "audio"],
    targetUrlPatterns: ["http://*/*", "https://*/*"] });
}
chrome.runtime.onInstalled.addListener(installMenu);
chrome.runtime.onStartup.addListener(installMenu);
chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId !== MENU) return;
  if (tab?.incognito) return;
  try {
    const url = ["video", "audio"].includes(info.mediaType) ? info.srcUrl : info.linkUrl;
    const response = await handoff(chrome, downloadRequest(url, undefined, { showOptions: true, sourcePage: info.frameUrl || info.pageUrl, ...(/\.(m3u8|mpd)(?:$|[?#])/i.test(url ?? "") ? { format: "mp4" } : {}) }));
    await chrome.notifications.create({ type: "basic", iconUrl: "icon.png", title: "Syphon this", message: response.message });
  } catch {
    await chrome.action.setBadgeText({ text: "!" });
    await chrome.notifications.create({ type: "basic", iconUrl: "icon.png", title: "Syphon this", message: "This link could not be handed off. Use a direct HTTP or HTTPS download link." });
  }
});
