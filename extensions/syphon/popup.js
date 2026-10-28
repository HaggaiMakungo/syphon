import {openPageLinks} from './page-actions.js';
import {orbitalIcons} from './orbital-icons.js';
import { probeDesktop } from './connection.js';
import { handoff, supportedLink } from "./handoff.js";
import { VIDEO_PERMISSION } from "./detection.js";
import { SIGNIN_PERMISSION } from "./request-context.js";

const status = document.getElementById("status");
const retry = document.getElementById("retry");
const check = document.getElementById("check");
const clear = document.getElementById("clear");
const title = document.getElementById("handoff-title");
const card = document.getElementById("handoff-card");
const connection = document.getElementById("connection");
const connectionLabel = document.getElementById("connection-label");
const connectionDescription = document.getElementById("connection-description");
document.getElementById("extension-id").textContent = `Extension ID: ${chrome.runtime.id}`;
const probeButton = document.getElementById('check-connection');
let last, busy = false;

function renderHandoffIcon(attention,received){
 const name=attention?'alert-triangle':received?'circle-check':'download',svg=card.querySelector('.handoff-icon svg');
 const source=new DOMParser().parseFromString(orbitalIcons[name],'image/svg+xml').documentElement;
 svg.setAttribute('viewBox',source.getAttribute('viewBox'));svg.setAttribute('data-icon-family','phosphor');svg.replaceChildren(...[...source.childNodes].map(n=>document.importNode(n,true)));
}

async function refresh() {
  ({ lastHandoff: last } = await chrome.storage.local.get("lastHandoff"));
  const outcome = last?.response?.status;
  const attention = ["unavailable", "not_found", "rejected"].includes(outcome);
  const titles = { accepted: "Added to Syphon", ready: "Download status", removed: "Removed from Syphon", cancelled: "Download cancelled", not_found: "Request not found", rejected: "Needs attention", unavailable: "Could not confirm handoff" };
  title.textContent = !last ? "No recent handoff" : titles[outcome] ?? "Download status";
  const rawMessage = last?.response?.message ?? "Your next download will appear here. Connect the desktop app once to get started.";
  const message = /\b(?:encrypted|drm|protected (?:hls|dash|playback|media|video))\b/i.test(rawMessage) ? "Yeah... I can't download this! Must be encrypted." : rawMessage;
  const repeatedTitle = title.textContent + ". ";
  status.textContent = message.startsWith(repeatedTitle) ? message.slice(repeatedTitle.length) : message;
  const context = document.getElementById("handoff-context");
  let source = last?.sourceHost;
  if (!source && last?.request?.url) { try { source = new URL(last.request.url).hostname; } catch {} }
  const timestamp = last?.sentAt ?? last?.time;
  const when = Number.isFinite(timestamp) && timestamp > 0 ? new Date(timestamp).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" }) : "Time unavailable";
  context.textContent = last ? `${source || "Source unavailable for this older request"} · ${when}` : "";
  context.hidden = !last;
  card.dataset.state = !last ? "empty" : attention ? "attention" : "received";
  renderHandoffIcon(attention,!!last);
  // A saved acknowledgment proves a past reply, not that the app is still running.
  const replied = ["accepted", "ready", "removed", "not_found", "rejected", "cancelled"].includes(outcome);
  connection.dataset.state = replied ? "replied" : outcome === "unavailable" ? "unavailable" : "unknown";
  connectionLabel.textContent = replied ? "App replied" : outcome === "unavailable" ? "Check setup" : "Not checked";
  connectionDescription.textContent = replied ? "Syphon replied to the last request. Refresh status to contact the app again." : outcome === "unavailable" ? "The last handoff could not be confirmed. Open Syphon or check the setup instructions below." : "Send a link to check the desktop connection. Setup instructions are below.";
  const { desktopConnection: probe } = await chrome.storage.local.get('desktopConnection');
  if (['ready', 'unavailable'].includes(probe?.status) && Number.isFinite(probe.checkedAt) && probe.checkedAt <= Date.now() + 1000 && probe.checkedAt >= (last?.time || 0)) {
    connection.dataset.state = probe.status === 'ready' ? 'replied' : 'unavailable';
    connectionLabel.textContent = probe.status === 'ready' ? 'App replied' : 'Check setup';
    const when = new Date(probe.checkedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    connectionDescription.textContent = probe.status === 'ready' ? `Syphon answered the connection check at ${when}. Check again to contact the app now.` : `Connection check at ${when} did not reach Syphon. Run Setup with Browser integration enabled, then try again.`;
  }
  retry.hidden = last?.request?.action !== "download";
  check.hidden = !last?.request?.requestId;
  clear.hidden = !last;
}

async function runAction(action) {
  if (busy) return;
  busy = true;
  retry.disabled = check.disabled = clear.disabled = probeButton.disabled = true;
  card.setAttribute("aria-busy", "true");
  try { await action(); await refresh(); }
  catch { status.textContent = "Could not update the handoff. Reopen this menu and try again."; }
  finally {
    busy = false;
    retry.disabled = check.disabled = clear.disabled = probeButton.disabled = false;
    card.setAttribute("aria-busy", "false");
  }
}
probeButton.addEventListener('click', () => runAction(async () => {
  connectionLabel.textContent = 'Checking…';
  connectionDescription.textContent = 'Contacting the desktop app. Syphon may open to complete this check.';
  await probeDesktop(chrome);
}));
retry.addEventListener("click", () => {
  if (last?.request?.action === "download") runAction(() => handoff(chrome, last.request));
});
check.addEventListener("click", () => runAction(async () => {
  if (!last?.request?.requestId) return;
  const original = last;
  const response = await handoff(chrome, { version: 1, action: "status", requestId: last.request.requestId });
  // A status miss must not erase the link needed to retry an uncertain original request.
  if (["not_found", "unavailable"].includes(response.status) && original.request.action === "download") {
    const { lastHandoff: current } = await chrome.storage.local.get("lastHandoff");
    if (current?.request?.requestId === original.request.requestId)
      await chrome.storage.local.set({ lastHandoff: { ...original, response } });
  }
}));
clear.addEventListener("click", () => runAction(async () => {
  await chrome.storage.local.remove("lastHandoff");
  await chrome.action.setBadgeText({ text: "" });
}));
try { await refresh(); }
catch {
  title.textContent = "History unavailable";
  status.textContent = "Could not read your handoff history. Reopen this menu to try again.";
}

const captureToggle = document.getElementById("capture-downloads");
const videoToggle = document.getElementById("detect-videos");
const signinToggle = document.getElementById("browser-signin");
const featureStatus = document.getElementById("feature-status");
try {
  const settings = await chrome.storage.local.get(["captureDownloads", "detectVideos", "captureStatus", "useBrowserSignin"]);
  captureToggle.checked = Boolean(settings.captureDownloads) && await chrome.permissions.contains({ permissions: ["downloads"] });
  videoToggle.checked = Boolean(settings.detectVideos) && await chrome.permissions.contains(VIDEO_PERMISSION);
  signinToggle.checked = Boolean(settings.useBrowserSignin) && await chrome.permissions.contains(SIGNIN_PERMISSION);
  document.getElementById("capture-status").textContent = settings.captureStatus ?? "";
  document.getElementById("capture-details").hidden = !settings.captureStatus;
  captureToggle.disabled = videoToggle.disabled = signinToggle.disabled = false;
} catch { featureStatus.textContent = "Could not load your feature settings. Reopen this menu to try again."; }

for (const [toggle, key, permission] of [[captureToggle, "captureDownloads", { permissions: ["downloads"] }], [videoToggle, "detectVideos", VIDEO_PERMISSION], [signinToggle, "useBrowserSignin", SIGNIN_PERMISSION]]) {
  toggle.addEventListener("change", async () => {
    const enabled = toggle.checked;
    toggle.disabled = true;
    try {
      // Request inside the checkbox's user gesture, before any other awaited work.
      if (enabled && !await chrome.permissions.request(permission)) {
        toggle.checked = false;
        featureStatus.textContent = "Permission was declined. This feature remains off.";
        return;
      }
      await chrome.storage.local.set({ [key]: enabled });
      const result = await chrome.runtime.sendMessage({ type: "syphon-settings" });
      if (!result?.ok) throw new Error("Settings could not be applied");
      featureStatus.textContent = !enabled ? "Switched off." : key === "detectVideos" ? "Video button enabled. Reload the video page if its button does not appear." : key === "captureDownloads" ? "Browser download capture enabled." : "Browser sign-in enabled for supported file links.";
    } catch {
      toggle.checked = false;
      featureStatus.textContent = "Could not apply this feature. Reload the extension and try again.";
      try { await chrome.storage.local.set({ [key]: false }); } catch { /* Keep the failure visible if storage is unavailable. */ }
    } finally { toggle.disabled = false; }
  });
}

chrome.storage.onChanged?.addListener((changes, area) => { if (area === 'local' && changes.desktopConnection) refresh().catch(() => {}); });

const findMedia = document.getElementById('find-media');
const mediaStatus = document.getElementById('page-media-status');
const mediaResults = document.getElementById('page-media-results');
let findingMedia = false;
findMedia.addEventListener('click', async () => {
  if (findingMedia) return;
  findingMedia = true; findMedia.disabled = true;
  mediaResults.replaceChildren();
  mediaStatus.textContent = 'Looking for observed media sources…';
  try {
    const { detectVideos } = await chrome.storage.local.get('detectVideos');
    if (!detectVideos || !await chrome.permissions.contains(VIDEO_PERMISSION)) throw new Error('Enable video detection and its permissions below to find media.');
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab || tab.incognito || !Number.isInteger(tab.id)) throw new Error('Media discovery is unavailable on this page or in private browsing.');
    const expectedPageUrl = tab.url;
    const reply = await chrome.runtime.sendMessage({ type: 'syphon-page-media-sources', tabId: tab.id, expectedPageUrl });
    const [currentTab] = await chrome.tabs.query({ active: true, currentWindow: true });
    const currentSettings = await chrome.storage.local.get('detectVideos');
    if (currentTab?.id !== tab.id || currentTab.url !== expectedPageUrl || currentTab.incognito || !currentSettings.detectVideos || !await chrome.permissions.contains(VIDEO_PERMISSION)) throw new Error('The page or permissions changed. Find media again.');
    if (!Array.isArray(reply?.sources)) throw new Error(reply?.message || 'Could not find media on this page.');
    const seen = new Set();
    const sources = reply.sources.filter(source => supportedLink(source?.url) && !seen.has(source.url) && seen.add(source.url));
    mediaStatus.textContent = sources.length ? 'Observed sources; these may not be the complete video. Choose one to review desktop options.' : 'No observed media sources. Play media on this page, then try again.';
    for (const source of sources) {
      const row = document.createElement('div'); row.className = 'feature';
      const label = document.createElement('p'); label.className = 'note';
      const duration = Number.isFinite(source.duration) && source.duration > 0 ? ` · ${Math.floor(source.duration / 60)}:${String(Math.floor(source.duration % 60)).padStart(2, '0')}` : '';
      label.textContent = `${typeof source.title === 'string' && source.title.trim() ? source.title : 'Unnamed media'} · ${new URL(source.url).hostname}${duration}`;
      const address = document.createElement('p'); address.className = 'handoff-context'; address.textContent = source.url;
      const choose = document.createElement('button'); choose.type = 'button'; choose.textContent = 'Choose source in Syphon';
      choose.addEventListener('click', async () => {
        if (choose.disabled) return;
        choose.disabled = true;
        try {
          const result = await chrome.runtime.sendMessage({ type: 'syphon-page-media-download', tabId: tab.id, expectedPageUrl, url: source.url });
          mediaStatus.textContent = result?.message || 'Could not open source options in Syphon.';
          await refresh();
        } catch { mediaStatus.textContent = 'Could not open source options in Syphon. Try again.'; }
        finally { choose.disabled = false; }
      });
      row.append(label, address, choose); mediaResults.append(row);
    }
  } catch (error) { mediaStatus.textContent = error.message || 'Could not find media on this page.'; }
  finally { findingMedia = false; findMedia.disabled = false; }
});

const pageLinks=document.getElementById('page-links');
pageLinks.addEventListener('click',async()=>{
 if(pageLinks.disabled)return;pageLinks.disabled=true;
 try{const [tab]=await chrome.tabs.query({active:true,currentWindow:true});await openPageLinks(chrome,tab);}
 catch(error){mediaStatus.textContent=error.message||'Could not read links on this page.';}
 finally{pageLinks.disabled=false;}
});
