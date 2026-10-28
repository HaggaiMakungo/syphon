import { downloadRequest, handoff, supportedLink } from "./handoff.js";

export function eligibleDownload(item) {
  return Number.isInteger(item?.id) && item.state === "in_progress" && !item.paused &&
    !item.incognito && !item.byExtensionId && item.danger === "safe" &&
    supportedLink(item.finalUrl || item.url);
}
const safePaused = item => item?.paused && eligibleDownload({ ...item, paused: false });
const sameSource = (item, request) => (item?.finalUrl || item?.url) === request.url;

// Hold each browser transfer immediately, rather than leaving it running behind
// a slow native handoff. Only storage/handoff work is serialized across items.
export function createCapture(api) {
  let queue = Promise.resolve(), storageQueue = Promise.resolve();
  const pending = new Map();
  function updateJob(id, value) {
    const operation = storageQueue.then(async () => {
      const { captureJobs = {} } = await api.storage.local.get("captureJobs");
      const entries = Object.entries(captureJobs).filter(([key, job]) => key !== String(id) && (!job.done && job.pauseIntent || Date.now() - job.time < 86400000));
      // In-flight pause intents must survive a busy batch; trim completed records only.
      const active = entries.filter(([, job]) => !job.done), completedLimit = Math.max(0, 99 - active.length), done = completedLimit ? entries.filter(([, job]) => job.done).slice(-completedLimit) : [];
      await api.storage.local.set({ captureJobs: { ...Object.fromEntries([...done, ...active]), [id]: value } });
    });
    storageQueue = operation.catch(() => {});
    return operation;
  }
  const finish = id => updateJob(id, { done: true, time: Date.now() });
  async function restore(id) {
    try {
      const [current] = await api.downloads.search({ id });
      // This download ID is owned by a persisted pause intent. Redirects may
      // change its URL; resume that same safe browser item, never hand it to an old request.
      if (!current || current.state !== "in_progress" || !current.paused) return true;
      if (!safePaused(current)) return false;
      await api.downloads.resume(id);
      const [after] = await api.downloads.search({ id });
      return !safePaused(after);
    } catch { return false; }
  }
  async function prepare(item) {
    const settings = await api.storage.local.get(["captureDownloads", "captureJobs"]), job = settings.captureJobs?.[item.id];
    if (job?.done || !settings.captureDownloads || !await api.permissions.contains({ permissions: ["downloads"] })) return;
    const [live] = await api.downloads.search({ id: item.id });
    if (!eligibleDownload(live) && !(job?.pauseIntent && safePaused(live))) return;
    const url = live.finalUrl || live.url;
    if (job?.request && !sameSource(live, job.request)) {
      // Never hand a new redirect to the old request or discard an owned pause.
      if (job.pauseIntent && safePaused(live)) {
        const restoreRequest={...job.request,url};
        if (await restore(item.id)) await finish(item.id);
        else await updateJob(item.id,{request:restoreRequest,pauseIntent:true,restoreOnly:true,time:Date.now()});
      } else await finish(item.id);
      return;
    }
    const request = job?.request ?? downloadRequest(url, undefined, { sourcePage: live.referrer, ...(/\.(m3u8|mpd)(?:$|[?#])/i.test(url) ? { format: "mp4" } : {}) });
    // Persist intent BEFORE pausing so worker suspension cannot strand a download.
    await updateJob(item.id, { request, pauseIntent: true, time: Date.now() });
    if (!live.paused) {
      try { await api.downloads.pause(item.id); }
      catch {
        const restored=await restore(item.id);
        if(restored) await finish(item.id);
        else await updateJob(item.id,{request,pauseIntent:true,restoreOnly:true,time:Date.now()});
        await api.storage.local.set({ captureStatus: restored ? "Browser download continues. It could not be paused safely, so Syphon did not capture it." : "The browser pause could not be confirmed. Check its download panel and resume there if needed; Syphon did not capture it." });
        return;
      }
    }
    const [held] = await api.downloads.search({ id: item.id });
    if (!safePaused(held) || !sameSource(held, request)) {
      if (safePaused(held)) {
        const restoreRequest={...request,url:held.finalUrl||held.url};
        if(await restore(item.id)) await finish(item.id);
        else await updateJob(item.id,{request:restoreRequest,pauseIntent:true,restoreOnly:true,time:Date.now()});
      } else await finish(item.id);
      return;
    }
    await api.storage.local.set({captureStatus:"Waiting for Syphon. The browser download is paused until the handoff is confirmed."});
    return { request };
  }
  async function capture(item, held) {
    const { request } = held;
    let accepted = false, keepForRecovery = false;
    try {
      const { captureDownloads } = await api.storage.local.get("captureDownloads");
      const [before] = await api.downloads.search({ id: item.id });
      if (!captureDownloads || !safePaused(before) || !sameSource(before, request)) return;
      const response = await handoff(api, request);
      accepted = response.status === "accepted" && typeof response.taskId === "string" &&
        ["Queued", "Starting", "Downloading", "RetryWaiting", "Assembling", "Publishing", "Completed"].includes(response.taskState);
      if (!accepted) return;
      const [current] = await api.downloads.search({ id: item.id });
      const { captureDownloads: enabled } = await api.storage.local.get("captureDownloads");
      let message;
      if (enabled && safePaused(current) && sameSource(current, request)) {
        try {
          await api.downloads.cancel(item.id);
          const [after] = await api.downloads.search({ id: item.id });
          message = after?.state === "complete" ? "Saved in Syphon, but the browser finished before cancellation. Check both for duplicate files." :
            after?.state === "in_progress" ? "Saved in Syphon, but browser cancellation could not be confirmed. The browser may still be paused; check both before resuming it." :
            "Sent to Syphon. The browser download was paused during handoff and cancelled after Syphon saved the task.";
        } catch { message = "Saved in Syphon, but the browser download could not be cancelled. It was left paused; check Syphon before resuming to avoid duplicate files."; }
      } else message = "Saved in Syphon. The browser download finished, changed or capture was switched off, so it was left untouched. If it is still paused, check Syphon before resuming.";
      await api.storage.local.set({ captureStatus: message });
    } finally {
      if (!accepted) {
        const resumed = await restore(item.id);
        keepForRecovery = !resumed;
        if (keepForRecovery) await updateJob(item.id, { request, pauseIntent: true, restoreOnly: true, time: Date.now() });
        await api.storage.local.set({ captureStatus: resumed ? "Browser download continues. Syphon could not confirm the handoff; check the app before retrying." : "Syphon could not confirm the handoff and the browser could not resume. Resume the paused download in your browser, or reopen the browser to retry recovery." });
      }
      if (!keepForRecovery) await finish(item.id);
    }
  }
  function run(item) {
    if (!Number.isInteger(item?.id)) return Promise.resolve();
    if (pending.has(item.id)) return pending.get(item.id);
    const operation = prepare(item).then(held => {
      if (!held) return;
      const next = queue.then(() => capture(item, held));
      queue = next.catch(() => {});
      return next;
    }).finally(() => pending.delete(item.id));
    pending.set(item.id, operation);
    return operation;
  }
  async function recover() {
    if (!await api.permissions.contains({ permissions: ["downloads"] })) return;
    const { captureJobs = {} } = await api.storage.local.get("captureJobs");
    for (const [id, job] of Object.entries(captureJobs)) {
      if (job.done || !job.request) continue;
      const [item] = await api.downloads.search({ id: Number(id) });
      if (Date.now()-job.time>=86400000) { if(job.pauseIntent){if(await restore(Number(id))) await finish(id);}else await finish(id);continue; }
      if (job.restoreOnly) { if (await restore(Number(id))) await finish(id); continue; }
      const { captureDownloads } = await api.storage.local.get("captureDownloads");
      if (!captureDownloads && job.pauseIntent) { if (await restore(Number(id))) await finish(id); continue; }
      if (item) await run(item);
    }
  }
  return { run, recover };
}