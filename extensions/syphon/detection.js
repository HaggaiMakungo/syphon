export const VIDEO_PERMISSION = { permissions: ["scripting", "webRequest"], origins: ["http://*/*", "https://*/*"] };
const registration = { id: "syphon-video", matches: VIDEO_PERMISSION.origins,
  js: ["media-model.js", "video-button.js"], runAt: "document_idle", allFrames: true, persistAcrossSessions: true };

export async function syncDetection(api) {
  if (!await api.permissions.contains({ permissions: ["scripting"] })) return;
  const { detectVideos } = await api.storage.local.get("detectVideos");
  const allowed = detectVideos && await api.permissions.contains(VIDEO_PERMISSION);
  const registered = await api.scripting.getRegisteredContentScripts({ ids: [registration.id] });
  if (allowed && !registered.length) await api.scripting.registerContentScripts([registration]);
  if (!allowed && registered.length) await api.scripting.unregisterContentScripts({ ids: [registration.id] });
  // Existing pages get the same idempotent controller; restricted pages are skipped.
  const tabs = await api.tabs.query({ url: VIDEO_PERMISSION.origins });
  for (const tab of tabs) {
    if (tab.incognito || !Number.isInteger(tab.id)) continue;
    try {
      if (allowed) await api.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, files: registration.js });
      else await api.tabs.sendMessage(tab.id, { type: "syphon-detection-off" });
    } catch { /* Browser internal pages and frames can reject injection. */ }
  }
}
