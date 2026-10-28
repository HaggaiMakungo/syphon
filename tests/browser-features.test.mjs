import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs/promises";
import vm from "node:vm";
import { createCapture, eligibleDownload } from "../extensions/syphon/capture.js";
import { syncDetection } from "../extensions/syphon/detection.js";

const item = () => ({ id: 12, url: "https://site/movie.mp4", finalUrl: "https://cdn/movie.mp4?token=a%2Fb", state: "in_progress", danger: "safe", paused: false, incognito: false });
function fixture(send) {
  const state = { captureDownloads: true }, cancelled = [], calls = [], paused = [], resumed = [];
  let current = item();
  const api = {
    storage: { local: {
      get: async keys => Object.fromEntries((Array.isArray(keys) ? keys : [keys]).map(k => [k, structuredClone(state[k])])),
      set: async values => Object.assign(state, structuredClone(values))
    } }, permissions: { contains: async () => true },
    runtime: { sendNativeMessage: async (_, request) => {
      calls.push(request); assert.equal(cancelled.length, 0);
      assert.equal(state.captureJobs[12].request.requestId, request.requestId);
      return send ? send(request, state, value => { current = value; }) : accepted(request);
    } },
    downloads: { search: async () => current ? [current] : [], pause: async id => { paused.push(id); current = {...current, paused:true}; }, resume: async id => { resumed.push(id); current = {...current, paused:false}; }, cancel: async id => { cancelled.push(id); current = { ...current, state: "interrupted", error: "USER_CANCELED" }; } },
    action: { setBadgeText: async () => {}, setBadgeBackgroundColor: async () => {}, setTitle: async () => {} }
  };
  return { api, state, calls, cancelled, paused, resumed, setCurrent: value => { current = value; } };
}
function accepted(r) { return { version: 1, requestId: r.requestId, status: "accepted", taskId: "task-id", taskState: "Queued", message: "Saved" }; }

test("capture stays off by default and requires its optional permission", async () => {
  for (const mode of ["off", "denied"]) {
    const f = fixture();
    if (mode === "off") delete f.state.captureDownloads;
    else f.api.permissions.contains = async () => false;
    await createCapture(f.api).run(item()); assert.equal(f.calls.length, 0); assert.deepEqual(f.cancelled, []);
  }
});
test("capture excludes private, unsafe, paused, extension and unsupported downloads", () => {
  for (const patch of [{ incognito: true }, { danger: "uncommon" }, { paused: true }, { byExtensionId: "other" }, { state: "complete" }, { finalUrl: "blob:https://site/id" }])
    assert.equal(eligibleDownload({ ...item(), ...patch }), false);
});
test("capture persists exact redirected URL and cancels only after matching durable acknowledgment", async () => {
  const f = fixture(); await createCapture(f.api).run(item());
  assert.deepEqual(f.cancelled, [12]); assert.equal(f.calls[0].url, item().finalUrl);
  assert.equal(f.state.captureJobs[12].done, true); assert.equal(f.state.captureJobs[12].request, undefined);
  assert.match(f.state.captureStatus, /cancelled after Syphon saved/);
});
test("unavailable or malformed acknowledgments leave browser running", async () => {
  for (const response of [() => { throw Error("offline"); }, r => ({ ...accepted(r), requestId: "wrong" }), r => ({ ...accepted(r), status: "removed" }), r => ({ ...accepted(r), taskState: "Failed" }), r => ({ ...accepted(r), taskId: null })]) {
    const f = fixture(response); await createCapture(f.api).run(item()); assert.deepEqual(f.cancelled, []);
    assert.match(f.state.captureStatus, /Browser download continues/);
  }
});
test("browser completion, manual resume or disabling capture during handoff prevents cancellation", async () => {
  for (const change of ["complete", "resumed", "disabled"]) {
    const f = fixture((r, state, set) => {
      if (change === "disabled") state.captureDownloads = false;
      else set({ ...item(), state: change === "complete" ? "complete" : "in_progress", paused: false });
      return accepted(r);
    });
    await createCapture(f.api).run(item()); assert.deepEqual(f.cancelled, []);
    assert.match(f.state.captureStatus, /left untouched/);
  }
});
test("already finished downloads are not handed off after waiting in the queue", async () => {
  const f = fixture(); f.setCurrent({ ...item(), state: "complete" });
  await createCapture(f.api).run(item()); assert.equal(f.calls.length, 0);
});
test("duplicate events are serialized and completed receipts survive worker recreation", async () => {
  const f = fixture(), capture = createCapture(f.api);
  await Promise.all([capture.run(item()), capture.run(item())]);
  await createCapture(f.api).run(item());
  assert.equal(f.calls.length, 1); assert.deepEqual(f.cancelled, [12]);
});
test("worker recovery reuses the persisted request ID and URL", async () => {
  const f = fixture(); const request = { version: 1, action: "download", requestId: crypto.randomUUID(), url: item().finalUrl };
  f.state.captureJobs = { 12: { request, time: Date.now() } };
  await createCapture(f.api).recover(); assert.deepEqual(f.calls, [request]); assert.deepEqual(f.cancelled, [12]);
});
test("cancellation error explicitly reports possible duplicate files", async () => {
  const f = fixture(); f.api.downloads.cancel = async () => { throw Error("race"); };
  await createCapture(f.api).run(item()); assert.match(f.state.captureStatus, /could not be cancelled/);
  const race = fixture(); race.api.downloads.cancel = async () => { race.setCurrent({ ...item(), state: "complete" }); };
  await createCapture(race.api).run(item()); assert.match(race.state.captureStatus, /browser finished before cancellation/);
});
test("video injection registers only after permission and is idempotent", async () => {
  let registered = [], injected = 0, removed = 0;
  const state = { detectVideos: true };
  const api = { storage: { local: { get: async () => state } }, permissions: { contains: async () => true },
    scripting: { getRegisteredContentScripts: async () => registered,
      registerContentScripts: async values => { registered = values; },
      unregisterContentScripts: async () => { registered = []; removed++; }, executeScript: async () => { injected++; } },
    tabs: { query: async () => [{ id: 1 }, { id: 2, incognito: true }], sendMessage: async () => {} } };
  await syncDetection(api); await syncDetection(api);
  assert.equal(registered.length, 1); assert.deepEqual(registered[0].js, ["media-model.js", "video-button.js"]); assert.equal(injected, 2);
  state.detectVideos = false; await syncDetection(api); assert.equal(removed, 1);
  api.permissions.contains = async () => false;
  api.scripting.getRegisteredContentScripts = async () => { throw Error("must not use ungranted API"); };
  await syncDetection(api);
});

test("media discovery preserves signed sources, labels and subtitle languages without inventing qualities", async () => {
  const context = vm.createContext({ URL });
  vm.runInContext(await fs.readFile(new URL("../extensions/syphon/media-model.js", import.meta.url), "utf8"), context);
  const node = values => ({ ...values, getAttribute: key => values[key] ?? null });
  const video = { baseURI: "https://site/watch/", currentSrc: "https://cdn/video.mp4?token=a%2Fb", videoHeight: 1080,
    getAttribute: () => null, querySelectorAll: query => query.startsWith("source") ? [
      node({ src: "https://cdn/video.mp4?token=a%2Fb", label: "1080p" }), node({ src: "../720.mp4", "data-quality": "720p" }),
      node({ src: "manifest.m3u8", label: "Adaptive", type: "application/x-mpegURL" }), node({ src: "javascript:alert(1)" })
    ] : [node({ src: "en.vtt", label: "English", srclang: "en" })] };
  const model = context.SyphonMedia.collect(video);
  assert.equal(model.options.length, 4); assert.equal(model.options[0].label, "1080p");
  assert.equal(model.options[0].url, video.currentSrc); assert.equal(model.options[1].url, "https://site/720.mp4");
  assert.equal(model.options[2].manifest, true); assert.equal(model.options[3].kind, "subtitle");
  video.querySelectorAll = query => query.startsWith("source") ? [node({ src: video.currentSrc })] : [];
  assert.match(context.SyphonMedia.collect(video).options[0].label, /1080p/);
  video.querySelectorAll = () => []; video.currentSrc = "blob:https://site/id";
  assert.equal(context.SyphonMedia.collect(video).streamOnly, true);
  video.currentSrc = "https://site/plain.mp4"; video.videoHeight = 0;
  assert.match(context.SyphonMedia.collect(video).options[0].label, /unspecified/);
  video.mediaKeys = {}; assert.equal(context.SyphonMedia.collect(video).protected, true);
});

test("quality menu uses the requested tiers for cropped dimensions without using width", async () => {
  const context = vm.createContext({ URL });
  vm.runInContext(await fs.readFile(new URL("../extensions/syphon/media-model.js", import.meta.url), "utf8"), context);
  const api = context.SyphonMedia;
  const choices = [1920, 1280, 960, 640, 428, 320, 214, 128].map((height, index) => ({ height, width: 9999, codec: "h264", youtubeFormat: String(index) }));
  assert.deepEqual(Array.from(api.qualityChoices(choices), choice => api.qualityLabel(api.qualityTier(choice))), ["4K", "2K", "1080p", "720p", "480p", "360p", "144p"]);
  assert.equal(api.qualityTier({ height: 1920, width: 1080, qualityHeight: 1080 }), 1080);
  assert.equal(api.qualityTier({ height: 1080, width: 3840 }), 1080);
  assert.equal(api.sizeLabel(null), ""); assert.equal(api.sizeLabel(-1), ""); assert.equal(api.sizeLabel(Infinity), "");
  assert.equal(api.sizeLabel(125000000), "About 125 MB"); assert.equal(api.sizeLabel(2500000000), "About 2.5 GB");
});


test("browser download is paused before native handoff and resumed on failure",async()=>{
 const f=fixture((r)=>{assert.deepEqual(f.paused,[12]);return {...accepted(r),status:'unavailable'};});
 await createCapture(f.api).run(item());assert.deepEqual(f.resumed,[12]);assert.deepEqual(f.cancelled,[]);
});
test("capture pauses before a slow handoff and does not transfer an unpausable download",async()=>{
 const f=fixture();f.api.downloads.pause=async()=>{throw Error('Cannot pause');};await createCapture(f.api).run(item());assert.equal(f.calls.length,0);assert.deepEqual(f.cancelled,[]);
});
test("worker recovery recognizes its own paused download and retains request identity",async()=>{
 const f=fixture(),request={version:1,action:'download',requestId:crypto.randomUUID(),url:item().finalUrl};
 f.state.captureJobs={12:{request,pauseIntent:true,time:Date.now()}};f.setCurrent({...item(),paused:true});
 await createCapture(f.api).recover();assert.deepEqual(f.calls,[request]);assert.deepEqual(f.cancelled,[12]);
});
test("failed browser resume remains recoverable without a second native handoff",async()=>{
 const f=fixture(r=>({...accepted(r),status:'unavailable'}));f.api.downloads.resume=async()=>{throw Error('Unavailable');};
 await createCapture(f.api).run(item());assert.equal(f.state.captureJobs[12].restoreOnly,true);
 f.api.downloads.resume=async()=>{f.setCurrent({...item(),paused:false});};await createCapture(f.api).recover();assert.equal(f.calls.length,1);assert.equal(f.state.captureJobs[12].done,true);
});

test('concurrent browser items pause before a slow native handoff completes',async()=>{
 const f=fixture(),items=new Map([[12,item()],[13,{...item(),id:13,url:'https://site/second.zip',finalUrl:'https://cdn/second.zip'}]]);
 let release,started,secondPaused;const gate=new Promise(r=>release=r),firstStarted=new Promise(r=>started=r),heldSecond=new Promise(r=>secondPaused=r);const calls=[];
 f.api.downloads.search=async({id})=>items.has(id)?[items.get(id)]:[];
 f.api.downloads.pause=async id=>{items.set(id,{...items.get(id),paused:true});if(id===13)secondPaused();};
 f.api.downloads.resume=async id=>items.set(id,{...items.get(id),paused:false});
 f.api.downloads.cancel=async id=>items.set(id,{...items.get(id),state:'interrupted'});
 f.api.runtime.sendNativeMessage=async(_,r)=>{calls.push(r);if(calls.length===1){started();await gate;}return accepted(r);};
 const capture=createCapture(f.api),first=capture.run(item());await firstStarted;const second=capture.run(items.get(13));await heldSecond;
 assert.equal(items.get(13).paused,true);assert.equal(calls.length,1);release();await Promise.all([first,second]);
 assert.equal(calls.length,2);assert.equal(f.state.captureJobs[12].done,true);assert.equal(f.state.captureJobs[13].done,true);
});
test('changed redirect on recovery is not sent under a stale request or left paused',async()=>{
 const f=fixture(),request={version:1,action:'download',requestId:crypto.randomUUID(),url:'https://old.test/file.zip'};
 f.state.captureJobs={12:{request,pauseIntent:true,time:Date.now()}};f.setCurrent({...item(),paused:true});
 await createCapture(f.api).recover();assert.equal(f.calls.length,0);assert.deepEqual(f.resumed,[12]);assert.equal(f.state.captureJobs[12].done,true);
});
test('old owned browser pauses are restored even after the capture history expires',async()=>{
 const f=fixture(),request={version:1,action:'download',requestId:crypto.randomUUID(),url:item().finalUrl};
 f.state.captureJobs={12:{request,pauseIntent:true,time:Date.now()-172800000}};f.setCurrent({...item(),paused:true});
 await createCapture(f.api).recover();assert.equal(f.calls.length,0);assert.deepEqual(f.resumed,[12]);assert.equal(f.state.captureJobs[12].done,true);
});
test('redirect changing as pause completes restores the browser instead of handing off a stale URL',async()=>{
 const f=fixture();f.api.downloads.pause=async()=>f.setCurrent({...item(),finalUrl:'https://cdn/redirected.zip',paused:true});
 await createCapture(f.api).run(item());assert.equal(f.calls.length,0);assert.deepEqual(f.resumed,[12]);assert.equal(f.state.captureJobs[12].done,true);
});

test('redirect changing while waiting for the native queue resumes the owned browser item',async()=>{
 const f=fixture(),originalSearch=f.api.downloads.search;let searches=0;
 f.api.downloads.search=async query=>{if(++searches===3)f.setCurrent({...item(),finalUrl:'https://cdn/late-redirect.zip',paused:true});return originalSearch(query);};
 await createCapture(f.api).run(item());assert.equal(f.calls.length,0);assert.deepEqual(f.resumed,[12]);assert.equal(f.state.captureJobs[12].done,true);
});
test('source change during a failed handoff resumes the owned item instead of discarding the hold',async()=>{
 const f=fixture((r,state,set)=>{set({...item(),finalUrl:'https://cdn/late-redirect.zip',paused:true});return{...accepted(r),status:'unavailable'};});
 await createCapture(f.api).run(item());assert.deepEqual(f.resumed,[12]);assert.equal(f.state.captureJobs[12].done,true);
});
