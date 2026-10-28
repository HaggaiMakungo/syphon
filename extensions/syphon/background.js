import { resolveVideoTitle } from './video-title.js';
import { LINKS_MENU, AUDIO_MENU, openPageLinks, audioRequest } from './page-actions.js';
import { handleConnectionInstall } from './connection.js';
import { createPlayerMetadata } from './player-metadata.js';
import { HOST, MENU, downloadRequest, handoff, supportedLink, validReply } from "./handoff.js";
import { createCapture } from "./capture.js";
import { syncDetection, VIDEO_PERMISSION } from "./detection.js";
import { createMediaNetwork } from "./media-network.js";
import { SIGNIN_PERMISSION } from "./request-context.js";

const playerMetadata = createPlayerMetadata(chrome);
const capture = createCapture(chrome);
const network = createMediaNetwork(chrome, tabId => chrome.tabs.sendMessage(tabId, { type: "syphon-media-found" }));
const videoClaims = new Map();
const playerCatalogs = new Map();
const automaticOwners = new Map();
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
chrome.runtime.onInstalled.addListener(details => { restoreFeatures().catch(() => {}); handleConnectionInstall(chrome, details).catch(() => {}); });
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
  if (message?.type === 'syphon-player-catalog' && sender.tab && !sender.tab.incognito) {
    (async()=>{
      const {detectVideos}=await chrome.storage.local.get('detectVideos');if(!detectVideos||!await chrome.permissions.contains(VIDEO_PERMISSION))return {players:[]};
      const now=Date.now();for(const [key,value]of playerCatalogs)if(now-value.time>15000)playerCatalogs.delete(key);
      while(playerCatalogs.size>128)playerCatalogs.delete(playerCatalogs.keys().next().value);
      const rows=Array.isArray(message.players)?message.players.slice(0,60).filter(p=>typeof p.id==='string'&&p.id.length<100).map(p=>({id:p.id,title:String(p.title||'Video').slice(0,300),duration:Number.isFinite(p.duration)&&p.duration>0?p.duration:null,sourceUrls:Array.isArray(p.sourceUrls)?p.sourceUrls.filter(supportedLink).slice(0,12):[],isMain:!!p.isMain,score:Number.isFinite(p.score)?Math.max(0,Math.min(100000000,p.score)):0,pageUrl:sender.url,frameId:sender.frameId||0})):[];
      playerCatalogs.set(sender.tab.id+':'+(sender.frameId||0),{tab:sender.tab.id,topUrl:sender.tab.url,time:now,rows});
      const owner=automaticOwners.get(sender.tab.id);if(owner&&owner.frame===(sender.frameId||0)){const main=rows.find(r=>r.isMain);if(main){owner.time=now;owner.score=main.score;}else automaticOwners.delete(sender.tab.id);}
      const players=[...playerCatalogs.values()].filter(v=>v.tab===sender.tab.id&&v.topUrl===sender.tab.url).flatMap(v=>v.rows).slice(0,120);
      const sources=await network.sources(sender.tab.id);
      const settings=await chrome.storage.local.get('detectVideos');if(!settings.detectVideos||!await chrome.permissions.contains(VIDEO_PERMISSION))return {players:[]};
      const current=await chrome.tabs.get(sender.tab.id);if(current.incognito||current.url!==sender.tab.url)return {players:[]};
      return {players,sources:sources.slice(-24)};
    })().then(respond,()=>respond({players:[]}));return true;
  }
  if(message?.type==='syphon-player-download'&&sender.tab&&!sender.tab.incognito){
    (async()=>{const {detectVideos}=await chrome.storage.local.get('detectVideos');if(!detectVideos||!await chrome.permissions.contains(VIDEO_PERMISSION))return {message:'Video detection is disabled.'};
      const rows=[...playerCatalogs.values()].filter(v=>v.tab===sender.tab.id&&v.topUrl===sender.tab.url&&Date.now()-v.time<15000).flatMap(v=>v.rows);
      const row=rows.find(p=>p.id===message.id&&p.sourceUrls.includes(message.url));if(!row)return {message:'This player changed. Reopen Videos and choose it again.'};
      return await handoff(chrome,downloadRequest(message.url,undefined,{title:row.title,showOptions:true,sourcePage:row.pageUrl,...(/\.(m3u8|mpd)(?:$|[?#])/i.test(message.url)?{format:'mp4'}:{})}));
    })().then(respond,()=>respond({message:'This source could not be sent. Reopen Videos.'}));return true;
  }
  if (message?.type === 'syphon-player-focus' && sender.tab && !sender.tab.incognito) {
    (async()=>{const {detectVideos}=await chrome.storage.local.get('detectVideos');if(!detectVideos||!await chrome.permissions.contains(VIDEO_PERMISSION))return {ok:false};
      const rows=[...playerCatalogs.values()].filter(v=>v.tab===sender.tab.id&&v.topUrl===sender.tab.url&&Date.now()-v.time<15000).flatMap(v=>v.rows);
      const target=rows.find(p=>p.id===message.id);if(!target)return {ok:false};
      return await chrome.tabs.sendMessage(sender.tab.id,{type:'syphon-player-focus-target',id:target.id},{frameId:target.frameId});
    })().then(respond,()=>respond({ok:false}));return true;
  }
  if (message?.type === 'syphon-video-claim' && sender.tab && !sender.tab.incognito) {
    const tabId = sender.tab.id;
    const claim = (videoClaims.get(tabId) || Promise.resolve()).catch(() => {}).then(async () => {
      if (typeof message.claimId !== 'string' || !/^[a-zA-Z0-9-]{16,64}$/.test(message.claimId)) return { ok: false };
      const { detectVideos } = await chrome.storage.local.get('detectVideos');
      if (!detectVideos || !await chrome.permissions.contains(VIDEO_PERMISSION)) return { ok: false };
      if(!message.automatic)automaticOwners.set(tabId,{frame:sender.frameId||0,score:Number.isFinite(message.score)?Math.max(0,Math.min(100000000,message.score)):0,manualUntil:Date.now()+15000,time:Date.now()});
      if(message.automatic){
        const score=Number.isFinite(message.score)?Math.max(0,Math.min(100000000,message.score)):0;
        const previous=automaticOwners.get(tabId);
        if(previous&&previous.frame!==(sender.frameId||0)&&Date.now()-previous.time<5000&&(previous.manualUntil>Date.now()||previous.score>=score))return {ok:false};
        automaticOwners.set(tabId,{frame:sender.frameId||0,score,time:Date.now(),manualUntil:previous?.frame===(sender.frameId||0)?previous.manualUntil:undefined});
      }
      const acknowledgment = await chrome.tabs.sendMessage(tabId, { type: 'syphon-video-owner', claimId: message.claimId });
      return { ok: acknowledgment?.ok === true };
    });
    videoClaims.set(tabId, claim);
    claim.finally(() => { if (videoClaims.get(tabId) === claim) videoClaims.delete(tabId); }).catch(() => {});
    claim.then(respond, () => respond({ ok: false }));
    return true;
  }
  if (['syphon-page-media-sources', 'syphon-page-media-download'].includes(message?.type) && !sender.tab && sender.url === chrome.runtime.getURL('popup.html')) {
    (async () => {
      const { detectVideos } = await chrome.storage.local.get('detectVideos');
      if (!detectVideos || !await chrome.permissions.contains(VIDEO_PERMISSION)) return { message: 'Enable video detection and its permissions in the extension.' };
      if (!Number.isInteger(message.tabId) || message.tabId < 0) return { message: 'This page is unavailable.' };
      const tab = await chrome.tabs.get(message.tabId);
      const [active] = await chrome.tabs.query({ active: true, currentWindow: true });
      if (tab.incognito || active?.id !== tab.id || !supportedLink(tab.url) || message.expectedPageUrl !== tab.url) return { message: 'Media discovery is unavailable on this page or in private browsing.' };
      const sources = await network.sources(tab.id);
      // Network observations wait on a queue; navigation, focus or permission can change while waiting.
      const { detectVideos: stillEnabled } = await chrome.storage.local.get('detectVideos');
      const permitted = await chrome.permissions.contains(VIDEO_PERMISSION);
      const current = await chrome.tabs.get(tab.id);
      const [currentActive] = await chrome.tabs.query({ active: true, currentWindow: true });
      if (!stillEnabled || !permitted || current.incognito || currentActive?.id !== tab.id || current.url !== message.expectedPageUrl) return { message: 'The page or permissions changed. Find media again.' };
      if (message.type === 'syphon-page-media-sources') return { sources };
      if (!sources.some(source => source.url === message.url)) return { message: 'This source is no longer available. Find media again.' };
      return await handoff(chrome, downloadRequest(message.url, undefined, { showOptions: true, sourcePage: tab.url, ...(/\.(m3u8|mpd)(?:$|[?#])/i.test(message.url) ? { format: 'mp4' } : {}) }));
    })().then(respond, () => respond({ message: 'Could not access media on this page. Find media again.' }));
    return true;
  }
  if (message?.type === "syphon-settings" && !sender.tab) {
    listenForDownloads();
    network.listen();
    syncDetection(chrome).then(() => respond({ ok: true }), () => respond({ ok: false }));
    return true;
  }
  if(message?.type==='syphon-video-title'&&sender.tab&&!sender.tab.incognito){
    (async()=>{const {detectVideos}=await chrome.storage.local.get('detectVideos');
      if(!detectVideos||!await chrome.permissions.contains(VIDEO_PERMISSION))return {};
      return {title:await resolveVideoTitle(chrome,sender,Array.isArray(message.candidates)?message.candidates.slice(0,20):[])};
    })().then(respond,()=>respond({}));return true;
  }
  if(message?.type==='syphon-overlay-outside'&&sender.tab&&!sender.tab.incognito){
    chrome.tabs.sendMessage(sender.tab.id,{type:'syphon-close-options'}).then(()=>respond({ok:true}),()=>respond({ok:false}));return true;
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
  chrome.contextMenus.create({ id: LINKS_MENU, title: "Download all links on this page with Syphon", contexts: ["page", "link"], documentUrlPatterns: ["http://*/*", "https://*/*"] });
  chrome.contextMenus.create({ id: AUDIO_MENU, title: "Download audio only", contexts: ["page", "link", "video", "audio"], documentUrlPatterns: ["http://*/*", "https://*/*"] });
  chrome.contextMenus.create({ id: MENU, title: "Syphon this", contexts: ["link", "video", "audio"],
    targetUrlPatterns: ["http://*/*", "https://*/*"] });
}
chrome.runtime.onInstalled.addListener(installMenu);
chrome.runtime.onStartup.addListener(installMenu);
chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (![MENU, LINKS_MENU, AUDIO_MENU].includes(info.menuItemId)) return;
  if (tab?.incognito) return;
  try {
    if (info.menuItemId === LINKS_MENU) { await openPageLinks(chrome, tab); return; }
    if (info.menuItemId === AUDIO_MENU) {
      const response = await handoff(chrome, audioRequest(info));
      await chrome.notifications.create({ type: "basic", iconUrl: "icon.png", title: "Download audio only", message: response.message }); return;
    }
    const url = ["video", "audio"].includes(info.mediaType) ? info.srcUrl : info.linkUrl;
    const response = await handoff(chrome, downloadRequest(url, undefined, { showOptions: true, sourcePage: info.frameUrl || info.pageUrl, ...(/\.(m3u8|mpd)(?:$|[?#])/i.test(url ?? "") ? { format: "mp4" } : {}) }));
    await chrome.notifications.create({ type: "basic", iconUrl: "icon.png", title: "Syphon this", message: response.message });
  } catch (error) {
    await chrome.action.setBadgeText({ text: "!" });
    await chrome.notifications.create({ type: "basic", iconUrl: "icon.png", title: "Syphon this", message: info.menuItemId === LINKS_MENU ? error.message : info.menuItemId === AUDIO_MENU ? "Choose a supported YouTube page or direct media link. DRM and browser-private streams are unsupported." : "This link could not be handed off. Use a direct HTTP or HTTPS download link." });
  }
});
