import { handoff } from "./handoff.js";
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
let last, busy = false;

async function refresh() {
  ({ lastHandoff: last } = await chrome.storage.local.get("lastHandoff"));
  const outcome = last?.response?.status;
  const attention = ["unavailable", "not_found", "rejected"].includes(outcome);
  const titles = { accepted: "Added to Syphon", ready: "Download status", removed: "Removed from Syphon", cancelled: "Download cancelled", not_found: "Request not found", rejected: "Needs attention", unavailable: "Could not confirm handoff" };
  title.textContent = !last ? "No recent handoff" : titles[outcome] ?? "Download status";
  const message = last?.response?.message ?? "Your next download will appear here. Connect the desktop app once to get started.";
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
  card.querySelector(".handoff-icon path").setAttribute("d", attention ? "M12 8v4M12 16h.01M10.3 3.9 2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0" : last ? "M5 12l4 4L19 6" : "M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2M7 11l5 5 5-5M12 4v12");
  // A saved acknowledgment proves a past reply, not that the app is still running.
  const replied = ["accepted", "ready", "removed", "not_found", "rejected", "cancelled"].includes(outcome);
  connection.dataset.state = replied ? "replied" : outcome === "unavailable" ? "unavailable" : "unknown";
  connectionLabel.textContent = replied ? "App replied" : outcome === "unavailable" ? "Check setup" : "Not checked";
  connectionDescription.textContent = replied ? "Syphon replied to the last request. Refresh status to contact the app again." : outcome === "unavailable" ? "The last handoff could not be confirmed. Open Syphon or check the setup instructions below." : "Send a link to check the desktop connection. Setup instructions are below.";
  retry.hidden = last?.request?.action !== "download";
  check.hidden = !last?.request?.requestId;
  clear.hidden = !last;
}

async function runAction(action) {
  if (busy) return;
  busy = true;
  retry.disabled = check.disabled = clear.disabled = true;
  card.setAttribute("aria-busy", "true");
  try { await action(); await refresh(); }
  catch { status.textContent = "Could not update the handoff. Reopen this menu and try again."; }
  finally {
    busy = false;
    retry.disabled = check.disabled = clear.disabled = false;
    card.setAttribute("aria-busy", "false");
  }
}
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
