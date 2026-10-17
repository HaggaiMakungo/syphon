import { withBrowserContext } from "./request-context.js";
export const HOST = "com.syphon.downloads";
export const MENU = "syphon-this";

export function supportedLink(value) {
  try {
    if (typeof value !== "string" || value.length > 8192) return false;
    const url = new URL(value);
    return ["http:", "https:"].includes(url.protocol) && !url.username && !url.password;
  } catch { return false; }
}

export function downloadRequest(url, requestId = crypto.randomUUID(), options = {}) {
  if (!supportedLink(url)) throw new Error("Syphon this supports HTTP and HTTPS links without embedded credentials.");
  const request = { version: 1, action: "download", requestId, url };
  if (supportedLink(options.sourcePage)) request.sourcePage = options.sourcePage;
  if (options.showOptions !== undefined) {
    if (typeof options.showOptions !== "boolean") throw new Error("Invalid download options.");
    request.showOptions = options.showOptions;
  }
  if (options.quality !== undefined) {
    if (typeof options.quality !== "string" || options.quality.length > 120) throw new Error("Invalid quality label.");
    request.quality = options.quality;
  }
  if (options.estimatedBytes !== undefined && options.estimatedBytes !== null) {
    if (!Number.isSafeInteger(options.estimatedBytes) || options.estimatedBytes <= 0) throw new Error("Invalid size estimate.");
    request.estimatedBytes = options.estimatedBytes;
  }
  if (options.title !== undefined) {
    if (typeof options.title !== "string" || options.title.length > 512) throw new Error("Invalid video title.");
    request.title = options.title;
  }
  if (options.format !== undefined) {
    if (!["mp3", "mp4"].includes(options.format)) throw new Error("Unsupported media output.");
    request.format = options.format;
    if (options.qualityHeight != null) { if (options.format !== "mp4" || ![144,240,360,480,720,1080,1440,2160,4320].includes(options.qualityHeight) || options.videoStream != null || options.youtubeFormat != null) throw new Error("Invalid advertised quality."); request.qualityHeight = options.qualityHeight; }
    if (options.youtubeFormat !== undefined && options.youtubeFormat !== null) {
      if (typeof options.youtubeFormat !== "string" || !/^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$/.test(options.youtubeFormat)) throw new Error("Invalid YouTube format.");
      request.youtubeFormat = options.youtubeFormat;
    }
    for (const key of ["videoStream", "audioStream"]) if (options[key] !== undefined && options[key] !== null) {
      if (!Number.isInteger(options[key]) || options[key] < 0 || options[key] > 255) throw new Error("Invalid stream selection.");
      request[key] = options[key];
    }
  }
  return request;
}

export function validReply(reply, request) {
  return reply?.version === 1 && reply.requestId === request.requestId &&
    ["accepted", "ready", "removed", "not_found", "rejected", "unavailable", "cancelled"].includes(reply.status) &&
    typeof reply.message === "string" && reply.message.length <= 2048;
}

export async function handoff(api, request) {
  request = { ...request }; delete request.context;
  const { lastHandoff: previous } = await api.storage.local.get("lastHandoff");
  const original = request.action === "status" && previous?.request?.requestId === request.requestId ? previous.request : request;
  const same = previous?.request?.requestId === request.requestId;
  // Keep only a hostname as display context; acknowledged signed URLs remain discarded.
  let sourceHost = same ? previous.sourceHost : undefined;
  if (!sourceHost && original.url) { try { sourceHost = new URL(original.url).hostname; } catch {} }
  const sentAt = same ? previous.sentAt ?? previous.time ?? Date.now() : Date.now();
  // Save before contacting the host: a worker/browser shutdown must not lose the retry ID.
  await api.storage.local.set({ lastHandoff: { request: original, sourceHost, sentAt, time: Date.now(), response: {
    version: 1, requestId: request.requestId, status: "unavailable", message: request.showOptions ? "Choose your download options in Syphon. Nothing is added until you confirm." : "Waiting for Syphon. If no confirmation arrives, retry this same request." } } });
  let response, prepared = false;
  try {
    const wire = await withBrowserContext(api, request); prepared = true;
    response = await api.runtime.sendNativeMessage(HOST, wire);
    if (!validReply(response, request)) throw new Error("Invalid acknowledgment");
  } catch {
    response = { version: 1, requestId: request.requestId, status: "unavailable", taskId: null,
      message: prepared ? "Syphon could not confirm this handoff. Open the app and use Retry in this extension. Your browser download was not cancelled." : "Browser sign-in data could not be read or is too large. Check sign-in permissions in the extension, or turn browser sign-in off. Your browser download was not cancelled." };
  }
  const accepted = ["accepted", "ready", "removed", "cancelled"].includes(response.status);
  // Once acknowledged, retain only the request ID for status checks, not its possibly signed URL.
  const savedRequest = accepted ? { version: 1, action: "status", requestId: request.requestId } : original;
  const { lastHandoff: current } = await api.storage.local.get("lastHandoff");
  if (current?.request?.requestId === request.requestId) {
    await api.storage.local.set({ lastHandoff: { request: savedRequest, sourceHost, sentAt, response, time: Date.now() } });
    await api.action.setBadgeText({ text: accepted ? "" : "!" });
    if (!accepted) await api.action.setBadgeBackgroundColor({ color: "#B42318" });
    await api.action.setTitle({ title: accepted ? "Syphon this" : "Syphon this — handoff needs attention" });
  }
  return response;
}
