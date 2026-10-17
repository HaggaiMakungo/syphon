import { downloadRequest, handoff, supportedLink } from "./handoff.js";

export function eligibleDownload(item) {
  return Number.isInteger(item?.id) && item.state === "in_progress" && !item.paused &&
    !item.incognito && !item.byExtensionId && item.danger === "safe" &&
    supportedLink(item.finalUrl || item.url);
}

// Serialize storage updates and keep a stable request ID across worker restarts.
export function createCapture(api) {
  let queue = Promise.resolve();
  const run = item => {
    const next = queue.then(() => capture(item));
    queue = next.catch(() => {});
    return next;
  };
  async function capture(item) {
    const settings = await api.storage.local.get(["captureDownloads", "captureJobs"]);
    if (!settings.captureDownloads || !await api.permissions.contains({ permissions: ["downloads"] }) || !eligibleDownload(item)) return;
    const jobs = settings.captureJobs ?? {};
    const key = String(item.id);
    if (jobs[key]?.done) return;
    const [live] = await api.downloads.search({ id: item.id });
    if (!eligibleDownload(live)) return;
    const url = live.finalUrl || live.url;
    const request = jobs[key]?.request ?? downloadRequest(url, undefined, { sourcePage: live.referrer, ...(/\.(m3u8|mpd)(?:$|[?#])/i.test(url) ? { format: "mp4" } : {}) });
    // Keep at most 100 recent records, without evicting this in-flight operation.
    const retained = Object.entries(jobs).filter(([id, value]) => id !== key && Date.now() - value.time < 86400000).slice(-99);
    const updated = Object.fromEntries(retained);
    updated[key] = { request, time: Date.now() };
    await api.storage.local.set({ captureJobs: updated });
    const response = await handoff(api, request);
    let message = "Browser download continues. Syphon could not confirm the handoff; check the app before retrying.";
    const accepted = response.status === "accepted" && typeof response.taskId === "string" &&
      ["Queued", "Starting", "Downloading", "RetryWaiting", "Assembling", "Publishing", "Completed"].includes(response.taskState);
    if (accepted) {
      const [current] = await api.downloads.search({ id: item.id });
      const { captureDownloads } = await api.storage.local.get("captureDownloads");
      if (captureDownloads && eligibleDownload(current)) {
        try {
          await api.downloads.cancel(item.id);
          const [after] = await api.downloads.search({ id: item.id });
          message = after?.state === "complete" ? "Saved in Syphon, but the browser finished before cancellation. Check both for duplicate files." :
            after?.state === "in_progress" ? "Saved in Syphon, but browser cancellation could not be confirmed. Check both for duplicate files." :
            "Sent to Syphon. The browser download was cancelled after Syphon saved the task. Progress and errors appear in the app.";
        } catch { message = "Saved in Syphon, but the browser download could not be cancelled. Check both to avoid duplicate files."; }
      } else message = "Saved in Syphon. The browser download finished, changed or capture was switched off, so it was left untouched.";
    }
    // No background retries after a known outcome. Uncertain manual retries use lastHandoff's ID.
    updated[key] = { done: true, time: Date.now() };
    await api.storage.local.set({ captureJobs: updated, captureStatus: message });
  }
  async function recover() {
    if (!await api.permissions.contains({ permissions: ["downloads"] })) return;
    const { captureJobs = {} } = await api.storage.local.get("captureJobs");
    for (const [id, job] of Object.entries(captureJobs)) {
      if (job.done || Date.now() - job.time >= 86400000) continue;
      const [item] = await api.downloads.search({ id: Number(id) });
      if (item) await run(item);
    }
  }
  return { run, recover };
}
