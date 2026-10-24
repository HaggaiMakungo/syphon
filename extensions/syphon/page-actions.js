import { downloadRequest, supportedLink } from './handoff.js';
export const LINKS_MENU = 'syphon-page-links';
export const AUDIO_MENU = 'syphon-audio-only';
export function normalizeLinks(values) {
 const seen = new Set();
 return values.filter(url => supportedLink(url) && !seen.has(url) && seen.add(url)).slice(0, 200).map(url => ({url, requestId: crypto.randomUUID()}));
}
export function filterLinks(rows, filter = '') {
 const query = filter.trim().toLowerCase().replace(/^\./,'');
 return rows.filter(row => !query || new URL(row.url).pathname.toLowerCase().endsWith('.' + query) || row.url.toLowerCase().includes(query));
}
export function audioRequest(info) {
 const direct = info.srcUrl || info.linkUrl;
 const page = info.frameUrl || info.pageUrl;
 const youtube = /^https?:\/\/(?:www\.|m\.|music\.)?(?:youtube\.com\/(?:watch\?|shorts\/|embed\/)|youtu\.be\/)/i.test(page || '');
 const url = direct && (/\.(mp4|webm|mkv|mp3|m4a|ogg|wav|m3u8|mpd)(?:$|[?#])/i.test(direct) || ['audio','video'].includes(info.mediaType)) ? direct : youtube ? page : null;
 return downloadRequest(url, undefined, {format:'mp3',showOptions:true,sourcePage:page});
}
export async function submitLinks(rows, send, cancelled = () => false) {
 let completed = 0;
 for (const row of rows) {
  if (cancelled()) return {completed,stopped:true};
  const response = await send(downloadRequest(row.url,row.requestId,{showOptions:true,sourcePage:row.sourcePage}));
  row.status = response.status;
  if (!['accepted','ready','removed'].includes(response.status)) return {completed,stopped:true,message:response.message};
  completed++;
 }
 return {completed,stopped:false};
}
export async function openPageLinks(api, tab) {
 if (!tab?.id || tab.incognito || !supportedLink(tab.url)) throw new Error('Open an HTTP or HTTPS page first.');
 if (!await api.permissions.request({permissions:['scripting']})) throw new Error('Page link access was cancelled.');
 const results = await api.scripting.executeScript({target:{tabId:tab.id},func:() => Array.from(document.querySelectorAll('a[href]'), a => a.href).slice(0,2000)});
 const rows = normalizeLinks(results.flatMap(r => r.result || [])).map(row => ({...row,sourcePage:tab.url}));
 if (!rows.length) throw new Error('No HTTP or HTTPS links were found on this page.');
 const key = 'page-links-' + crypto.randomUUID();
 // Session-only staging; review consumes it immediately. Never write page links to local history.
 await api.storage.session.set({[key]:rows});
 try { await api.tabs.create({url:api.runtime.getURL('page-links.html')+'#'+key}); }
 catch (error) {await api.storage.session.remove(key);throw error;}
}
