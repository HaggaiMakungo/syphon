// Runs in the page world. Return only bounded metadata, never player code, cookies or media URLs.
export function readYoutubePlayer() {
  let response;
  try { response = document.querySelector('#movie_player')?.getPlayerResponse?.(); } catch {}
  response ||= globalThis.ytInitialPlayerResponse;
  if (!response || typeof response !== 'object') return null;
  const details = response.videoDetails || {}, data = response.streamingData || {};
  const formats = [...(Array.isArray(data.formats) ? data.formats : []), ...(Array.isArray(data.adaptiveFormats) ? data.adaptiveFormats : [])].slice(0, 256);
  const captions = response.captions?.playerCaptionsTracklistRenderer?.captionTracks;
  return { videoId: String(details.videoId || '').slice(0, 32), title: String(details.title || '').slice(0, 512),
    duration: Number(details.lengthSeconds), live: !!details.isLiveContent, playable: response.playabilityStatus?.status === 'OK',
    formats: formats.map(f => ({ id: String(f.itag || '').slice(0, 64), height: Number(f.height), width: Number(f.width),
      codec: String(f.mimeType || '').slice(0, 160), qualityLabel: String(f.qualityLabel || '').slice(0, 40),
      bitrate: Number(f.bitrate), size: Number(f.contentLength), audio: !!f.audioQuality,
      drm: !!f.drmFamilies?.length || !!f.drmTrackType })),
    subtitles: (Array.isArray(captions) ? captions : []).slice(0, 32).map(t => {
      const name=String(t.name?.simpleText || t.name?.runs?.map(r=>r.text).join('') || t.languageCode || 'Closed captions');
      return {url:String(t.baseUrl || '').slice(0,8192),label:(name+(t.kind==='asr'&&!/auto[- ]generated/i.test(name)?' (auto-generated)':'')).slice(0,120)};
    }) };
}

export function youtubeKey(value) {
  try {
    const u = new URL(value);
    if (!['youtube.com','www.youtube.com','m.youtube.com','music.youtube.com','youtube-nocookie.com','www.youtube-nocookie.com','youtu.be','www.youtu.be'].includes(u.hostname) || !['https:','http:'].includes(u.protocol) || u.username || u.password || u.port) return null;
    const id = u.hostname.endsWith('youtu.be') ? u.pathname.slice(1) : /^\/(?:shorts|embed|live)\/([\w-]{11})\/?$/.exec(u.pathname)?.[1] || (u.pathname === '/watch' ? u.searchParams.get('v') : null);
    return /^[\w-]{11}$/.test(id || '') ? id : null;
  } catch { return null; }
}

export function sanitizePlayer(raw, id) {
  if (!raw || raw.videoId !== id || !raw.playable || raw.live || !Array.isArray(raw.formats)) return null;
  const duration = Number.isFinite(raw.duration) && raw.duration > 0 ? raw.duration : null;
  const qualities = raw.formats.filter(f => !f.drm && /^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$/.test(f.id || '') &&
    Number.isInteger(f.height) && f.height > 0 && f.height <= 32768).slice(0, 128).map(f => {
      const label = /^(\d{2,4})p/.exec(f.qualityLabel || '');
      return { videoStream: 0, width: Number.isInteger(f.width) ? f.width : 0, height: f.height,
        qualityHeight: label ? Number(label[1]) : f.height, youtubeFormat: f.id,
        codec: /codecs="([^"]+)/.exec(f.codec || '')?.[1]?.split(',')[0]?.slice(0, 40) || '',
        bitrate: Number.isFinite(f.bitrate) && f.bitrate > 0 ? f.bitrate : null,
        // Separate audio is not included in video-only contentLength; don't label it as the final size.
        estimatedBytes: f.audio && Number.isSafeInteger(f.size) && f.size > 0 ? f.size : null };
    });
  if (!qualities.length) return null;
  const subtitles = (Array.isArray(raw.subtitles) ? raw.subtitles : []).filter(t => {
    try { const u = new URL(t.url); return u.protocol === 'https:' && ['www.youtube.com','youtube.com'].includes(u.hostname) && u.pathname === '/api/timedtext' && !u.username && !u.password && !u.port && u.href.length <= 8192; } catch { return false; }
  }).slice(0,32).map(t => {const u=new URL(t.url);u.searchParams.set('fmt','vtt');return {url:u.href,label:String(t.label || 'Subtitles').slice(0,120)};}).filter(t=>t.url.length<=8192);
  return { qualities, hasAudio: raw.formats.some(f => !f.drm && f.audio), duration, title: String(raw.title || '').slice(0,512), subtitles,
    message: 'Player qualities found. Availability is checked when downloading.' };
}

export function createPlayerMetadata(api) {
  const pending = new Map();
  return async (url, sender) => {
    const id = youtubeKey(url);
    // Only inspect the requesting YouTube frame, never an arbitrary tab supplied by the page.
    if (!id || youtubeKey(sender.url) !== id || !Number.isInteger(sender.tab?.id)) return null;
    const key = `${sender.tab.id}:${sender.frameId || 0}:${id}`;
    if (pending.has(key)) return pending.get(key);
    const work = (async () => {
      const store = api.storage.session;
      const cache = (await store?.get('playerQualities'))?.playerQualities || {};
      const saved = cache[id];
      const cached = saved?.expires > Date.now() ? saved.media : null;
      const read = (async () => {
        const result = await api.scripting.executeScript({ target: { tabId: sender.tab.id, frameIds: [sender.frameId || 0] }, world: 'MAIN', func: readYoutubePlayer });
        const media = sanitizePlayer(result?.[0]?.result, id);
        if (media && store) {
          // Cache only qualities/title. Caption URLs can expire and stay in this page's memory.
          const latest = (await store.get('playerQualities')).playerQualities || {};
          const entries = Object.entries(latest).filter(([,v]) => v?.expires > Date.now()).slice(-63);
          await store.set({ playerQualities: { ...Object.fromEntries(entries), [id]: { expires: Date.now()+600000, media: {...media,subtitles:[]} } } });
        }
        return media;
      })().catch(() => null);
      if (cached) { void read; return cached; }
      let timer;
      try { return await Promise.race([read,new Promise(resolve => {timer=setTimeout(()=>resolve(null),1200);})]); }
      finally { clearTimeout(timer); }
    })().finally(() => pending.delete(key));
    pending.set(key,work);return work;
  };
}
