import { supportedLink } from "./handoff.js";
import { VIDEO_PERMISSION } from "./detection.js";
export function mediaResponse(details) {
  if (details.incognito || details.tabId < 0 || !supportedLink(details.url) || details.statusCode < 200 || details.statusCode >= 300) return null;
  const mime = details.responseHeaders?.find(h => h.name.toLowerCase() === "content-type")?.value ?? "";
  const manifest = /\.(m3u8|mpd)(?:$|[?#])/i.test(details.url) || /mpegurl|dash\+xml/i.test(mime);
  const direct = /\.(mp4|webm|mkv|mp3|m4a)(?:$|[?#])/i.test(details.url) || /^(video\/(mp4|webm)|audio\/(mpeg|mp4))/i.test(mime);
  return manifest || direct ? { url: details.url, manifest, frameId: details.frameId, time: Date.now() } : null;
}
export function createMediaNetwork(api, onSource = () => {}) {
  let queue = Promise.resolve();
  function observe(details) {
    if (details.type !== "main_frame" && !mediaResponse(details)) return Promise.resolve();
    const next = queue.then(async () => {
      const { detectVideos } = await api.storage.local.get("detectVideos");
      if (!detectVideos || !await api.permissions.contains(VIDEO_PERMISSION) || details.incognito || details.tabId < 0) return;
      const { mediaTabs = {} } = await api.storage.session.get("mediaTabs");
      const now = Date.now();
      for (const id of Object.keys(mediaTabs)) if (now - mediaTabs[id].time > 600000) delete mediaTabs[id];
      if (details.type === "main_frame") delete mediaTabs[details.tabId];
      const source = mediaResponse(details);
      let added = false;
      if (source) {
        const tab = mediaTabs[details.tabId] ?? { sources: [] };
        added = !tab.sources.some(s => s.url === source.url);
        tab.time = now; tab.sources = [...tab.sources.filter(s => s.url !== source.url), source].slice(-24); mediaTabs[details.tabId] = tab;
      }
      while (Object.keys(mediaTabs).length > 32) delete mediaTabs[Object.keys(mediaTabs).sort((a, b) => mediaTabs[a].time - mediaTabs[b].time)[0]];
      await api.storage.session.set({ mediaTabs });
      if (added) {
        try { await onSource(details.tabId); } catch { /* A tab can close before its players receive the update. */ }
      }
    });
    queue = next.catch(() => {}); return next;
  }
  async function sources(tabId) {
    await queue;
    const { mediaTabs = {} } = await api.storage.session.get("mediaTabs");
    const tab = mediaTabs[tabId]; return tab && Date.now() - tab.time < 600000 ? tab.sources : [];
  }
  const listener = details => { observe(details).catch(() => {}); };
  function listen() {
    const event = api.webRequest?.onHeadersReceived;
    if (event) {
      try { event.removeListener(listener); event.addListener(listener, { urls: ["http://*/*", "https://*/*"] }, ["responseHeaders"]); }
      catch { /* Optional webRequest permission may not have been granted yet. */ }
    }
  }
  return { observe, sources, listen };
}
