(() => {

  function qualityLabel(height) {

    return ({ 144: "144p", 360: "360p", 480: "480p", 720: "720p", 1080: "1080p", 1440: "2K", 2160: "4K" })[height] ?? "Original quality";

  }

  function qualityTier(choice) {

    const height = choice.qualityHeight ?? choice.height;

    if (!Number.isInteger(height) || height <= 0) return 0;

    return [144, 240, 360, 480, 720, 1080, 1440, 2160, 4320].reduce((best, tier) => Math.abs(tier - height) < Math.abs(best - height) ? tier : best, 144);

  }

  function qualityChoices(qualities) {

    const byHeight = new Map();

    const compatibility = codec => /^(avc|h264)/i.test(codec ?? "") ? 3 : /^(vp9|vp09)/i.test(codec ?? "") ? 2 : /^(av1|av01)/i.test(codec ?? "") ? 1 : 0;

    for (const choice of qualities) {

      const height = qualityTier(choice);

      if (height === 240 || height === 4320) continue;

      const previous = byHeight.get(height);

      // Prefer broad playback compatibility; compare bitrate only within the same codec family.

      const rank = compatibility(choice.codec), previousRank = compatibility(previous?.codec);

      if (!previous || rank > previousRank || rank === previousRank && (choice.bitrate ?? 0) > (previous.bitrate ?? 0)) byHeight.set(height, choice);

    }

    return [...byHeight.entries()].sort(([a], [b]) => b - a).map(([, choice]) => choice);

  }

  function sizeLabel(bytes) {

    if (!Number.isSafeInteger(bytes) || bytes <= 0) return "";

    const units = ["B", "KB", "MB", "GB", "TB"];

    const index = Math.min(4, Math.floor(Math.log(bytes) / Math.log(1000)));

    return `About ${(bytes / 1000 ** index).toLocaleString(undefined, { maximumFractionDigits: index > 1 ? 1 : 0 })} ${units[index]}`;

  }

  function youtubeUrl(value) {

    try {

      const url = new URL(value), host = url.hostname.toLowerCase();

      if (!["youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be", "www.youtu.be", "www.youtube-nocookie.com", "youtube-nocookie.com"].includes(host) || url.port || url.username || url.password || !["http:", "https:"].includes(url.protocol)) return null;

      const parts = url.pathname.replace(/^\/+|\/+$/g, "").split("/");

      let id = ["youtu.be", "www.youtu.be"].includes(host) && parts.length === 1 ? parts[0] :

        parts.length === 2 && ["shorts", "embed", "live"].includes(parts[0]) ? parts[1] : url.pathname === "/watch" && new Set(url.searchParams.getAll("v")).size === 1 ? url.searchParams.get("v") : null;

      return /^[A-Za-z0-9_-]{11}$/.test(id ?? "") ? `https://www.youtube.com/watch?v=${id}` : null;

    } catch { return null; }

  }

  function resolve(value, base) {

    try {

      if (!value || value.length > 8192) return null;

      const url = new URL(value, base);

      return ["http:", "https:"].includes(url.protocol) && !url.username && !url.password ? url.href : null;

    } catch { return null; }

  }

  function previewHint(video) {

    const flag = video.getAttribute("data-preview");

    return flag !== null && !["false","0"].includes(flag.trim().toLowerCase()) || /preview|trailer|teaser/i.test(`${video.getAttribute("title") || ""} ${video.getAttribute("aria-label") || ""} ${video.className || ""}`);

  }

  function durationLabel(seconds) {

    if (!Number.isFinite(seconds) || seconds <= 0) return "";

    const n = Math.floor(seconds);

    return n >= 3600 ? `${Math.floor(n/3600)}:${String(Math.floor(n/60)%60).padStart(2,"0")}:${String(n%60).padStart(2,"0")}` : `${Math.floor(n/60)}:${String(n%60).padStart(2,"0")}`;

  }

  function playbackGesture(event, video) {

    if (!event.isTrusted || event.repeat || event.defaultPrevented) return false;

    if (event.type === "pointerdown" ? event.button !== 0 : event.type !== "keydown" || ![" ", "Enter", "k", "K"].includes(event.key)) return false;

    const target = event.target;

    if (target === video) return true;

    if (target?.closest?.('input,textarea,select,[contenteditable]:not([contenteditable="false"]),[role="textbox"],[role="slider"],a')) return false;

    const player = video.closest?.('#movie_player');

    const ownsYoutubePlayer = player && youtubeUrl(video.baseURI) && player.querySelectorAll('video').length === 1;

    if (ownsYoutubePlayer) {

      const control = target?.closest?.('.ytp-play-button,.ytp-large-play-button');

      if (control && player.contains(control)) return true;

      if (event.type === 'keydown' && [' ', 'k', 'K'].includes(event.key) && (target === video.ownerDocument.body || target === video.ownerDocument.documentElement || player.contains(target))) return true;

      const surface = target?.closest?.('.html5-video-container,.ytp-cued-thumbnail-overlay');

      if (event.type === 'pointerdown' && surface && player.contains(surface) && !target.closest('button,[role="button"],[role="menuitem"]')) return true;

    }

    const control = target?.closest?.('button,[role="button"]');

    if (!control || !/play|pause|resume/i.test(`${control.getAttribute("aria-label") || ""} ${control.getAttribute("title") || ""} ${control.textContent || ""}`)) return false;

    // Only a local, single-video control group is unambiguous. Never use body/page controls.

    let parent = control.parentElement;

    for (let depth=0; parent && depth<4 && !["BODY","HTML"].includes(parent.tagName); depth++, parent=parent.parentElement) {

      const videos = parent.querySelectorAll("video");

      if (videos.length) return videos.length === 1 && videos[0] === video;

    }

    return false;

  }

  function structuredVideos(doc) {

    const result=[];let remaining=3000;

    function visit(value){if(--remaining<0||!value||typeof value!=='object')return;if(Array.isArray(value)){for(const v of value)visit(v);return;}if(value['@type']==='VideoObject'||Array.isArray(value['@type'])&&value['@type'].includes('VideoObject'))result.push(value);for(const v of Object.values(value))if(typeof v==='object')visit(v);}

    for(const script of [...doc.querySelectorAll('script[type="application/ld+json"]')].slice(0,20)){if(script.textContent.length>1000000)continue;try{visit(JSON.parse(script.textContent));}catch{}}

    return result.slice(0,50);

  }

  function structuredMatch(video) {

    const doc=video.ownerDocument;if(!doc?.querySelectorAll)return null;const poster=resolve(video.poster||video.getAttribute('poster'),video.baseURI);

    const records=structuredVideos(doc);

    const key=url=>{try{return new URL(url).pathname.match(/[a-f0-9]{32}/i)?.[0]?.toLowerCase();}catch{return null;}};

    const matches=records.filter(item=>{

      const thumbs=Array.isArray(item.thumbnailUrl)?item.thumbnailUrl:[item.thumbnailUrl];

      if(poster&&thumbs.some(raw=>{const thumb=resolve(raw,video.baseURI);return thumb&&(thumb===poster||key(thumb)&&key(thumb)===key(poster));}))return true;

      const canonical=doc.querySelector('link[rel="canonical"]')?.href||video.baseURI;

      const page=item['@id']||item.url||item.mainEntityOfPage?.['@id'];

      const isPinterestMain=/(^|\.)pinterest\.[a-z.]+$/i.test(new URL(video.baseURI).hostname)&&video.getAttribute('elementtiming')?.startsWith('closeup-video-main-');

      return (isPinterestMain||doc.querySelectorAll('video').length===1)&&page&&resolve(page,video.baseURI)===resolve(canonical,video.baseURI);

    });

    return matches.length===1?matches[0]:null;

  }

  function mainPlayer(videos) {

    const visible=[...videos].filter(v=>!previewHint(v)).filter(v=>{const r=v.getBoundingClientRect();return v.isConnected&&r.width>=160&&r.height>=90&&r.bottom>0&&r.top<(v.ownerDocument.defaultView.innerHeight||1000)&&r.right>0&&r.left<(v.ownerDocument.defaultView.innerWidth||2000);});

    const known=visible.find(v=>v.closest('#movie_player')&&youtubeUrl(v.baseURI))||visible.find(v=>v.getAttribute('elementtiming')?.startsWith('closeup-video-main-'));if(known)return known;

    const ranked=visible.filter(v=>!v.paused&&!v.ended);return (ranked.length?ranked:visible).sort((a,b)=>{const x=a.getBoundingClientRect(),y=b.getBoundingClientRect();return y.width*y.height-x.width*x.height;})[0]||null;

  }

  function collect(video) {

    const assigned = video.getAttribute("src"), loaded = video.currentSrc;

    const sameUrl = (a,b) => { try { return new URL(a,video.baseURI).href === new URL(b,video.baseURI).href; } catch { return false; } };

    const declared = [...video.querySelectorAll("source[src]")].map(s=>s.getAttribute("src"));

    // Chromium can retain currentSrc from a recycled player until its next load completes.

    // With conflicting identities, neither the old loaded URL nor page metadata is safe.

    if (loaded && (assigned && !sameUrl(assigned,loaded) || !assigned && declared.length && !declared.some(s=>sameUrl(s,loaded))))

      return { options: [], protected: Boolean(video.mediaKeys), streamOnly: true, pendingSource: true };

    const options = [], seen = new Set();

    function add(raw, label, type, kind) {

      const url = resolve(raw, video.baseURI);

      if (!url || seen.has(url)) return;

      seen.add(url);

      const stream = /\.(m3u8|mpd)(?:$|[?#])/i.test(url) || /mpegurl|dash\+xml/i.test(type ?? "");

      const tier = /\b(144|240|360|480|720|1080|1440|2160|4320)p\b/i.exec(label ?? "");

      options.push({ url, label: String(label || (kind === "subtitle" ? "Subtitles" : "Video — quality unspecified")).slice(0, 120), kind, qualityHeight: tier ? Number(tier[1]) : undefined,

        supported: true, manifest: stream, note: stream ? "Streaming source" : "" });

    }

    const structured=structuredMatch(video);

    if(structured){

      const raw=structured.contentUrl;

      if(typeof raw==='string')add(raw,structured.height?`${parseInt(structured.height)}p — current video`:'Current video',structured.encodingFormat,'video');

    }

    // Explicit source labels are more useful than the dimensions of a different active source.

    for (const source of video.querySelectorAll("source[src]")) {

      const height = source.getAttribute("res") || source.getAttribute("data-res");

      add(source.getAttribute("src"), source.getAttribute("label") || source.getAttribute("data-quality") ||

        (height && /^\d{2,5}$/.test(height) ? `${height}p` : "Video — quality unspecified"), source.type, "video");

    }

    const current = video.currentSrc || video.getAttribute("src");

    add(current, video.videoHeight > 0 ? `${video.videoHeight}p — current video` : "Current video — quality unspecified", video.type, "video");

    const active = options.find(option => option.url === resolve(current, video.baseURI));

    if (active && video.videoHeight > 0 && /unspecified/.test(active.label)) { active.label = `${video.videoHeight}p — current video`; active.qualityHeight = qualityTier({ height: video.videoHeight }); }

    for (const track of video.querySelectorAll('track[src][kind="subtitles"],track[src][kind="captions"]'))

      add(track.getAttribute("src"), track.label || track.srclang || "Subtitles", "text/vtt", "subtitle");

    // In-band tracks can exist without a <track src>; detect them without changing playback modes.

    for (const track of Array.from(video.textTracks || [])) if (['subtitles','captions'].includes(track.kind)) {

      const label = track.label || track.language || 'Closed captions';

      if (!options.some(o => o.kind === 'subtitle' && o.label === label)) options.push({kind:'subtitle',label, supported:false,note:'Embedded captions — separate saving is not available yet'});

    }

    const player = video.closest?.("#movie_player");

    const ownsPage = player ? player.querySelectorAll("video").length === 1 : video.ownerDocument?.querySelectorAll("video").length === 1;

    const youtube = ownsPage ? youtubeUrl(video.baseURI) : null;

    if (youtube) {

      const subtitles = options.filter(option => option.kind === "subtitle");

      options.splice(0, options.length, { url: youtube, label: "Best available", kind: "video", format: "mp4", youtube: true, supported: true, note: "" }, ...subtitles);

    }

    const page=videoPageUrl(video.baseURI);
    const ownsFallbackPage=ownsPage||mainPlayer([...(video.ownerDocument?.querySelectorAll?.('video')||[])])===video;
    if(!youtube&&page&&ownsFallbackPage&&!previewHint(video)&&!video.mediaKeys&&(knownVideoPage(page)||!options.some(o=>o.kind==='video'))){
      const subtitles=options.filter(o=>o.kind==='subtitle');options.splice(0,options.length,{url:page,label:'Best available',kind:'video',format:'mp4',page:true,supported:true,note:'Resolved from this video page'},...subtitles);
    }
    const durationMatch=typeof structured?.duration==='string'?/^PT(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)S)?$/.exec(structured.duration):null;

    const duration=Number.isFinite(video.duration)&&video.duration>0?video.duration:durationMatch?Number(durationMatch[1]||0)*3600+Number(durationMatch[2]||0)*60+Number(durationMatch[3]||0):null;

    return { options, title:typeof structured?.name==='string'?structured.name:null, duration, protected: Boolean(video.mediaKeys),

      streamOnly: Boolean(current) && !resolve(current, video.baseURI) };

  }

  function videoPageUrl(value){
    const url=resolve(value,value);if(!url)return null;const host=new URL(url).hostname;
    if(/(^|\.)(youtube\.com|youtube-nocookie\.com|youtu\.be)$/i.test(host))return null;
    return /\.(mp4|webm|mkv|mov|m4v|ts|mp3|m4a|wav|ogg|m3u8|mpd|vtt|srt|jpg|jpeg|png|gif|webp)$/i.test(new URL(url).pathname)?null:url;
  }
  function knownVideoPage(value){try{const u=new URL(value),host=u.hostname;return u.pathname.length>1&&['facebook.com','fb.watch','vimeo.com','instagram.com','tiktok.com','twitter.com','x.com','dailymotion.com','dai.ly','reddit.com','redd.it','twitch.tv','pinterest.com','pin.it','bilibili.com','b23.tv','rumble.com','streamable.com'].some(site=>host===site||host.endsWith('.'+site));}catch{return false;}}
  function titleCandidates(video) {

    const values=[];const add=v=>{if(typeof v==='string'&&v.trim())values.push(v.trim().slice(0,512));};

    add(video?.getAttribute('aria-label'));add(video?.getAttribute('title'));

    try{add(navigator.mediaSession?.metadata?.title);}catch{}

    for(const selector of ['meta[property="og:video:title"]','meta[property="og:title"]','meta[name="twitter:title"]'])add(document.querySelector(selector)?.content);

    const walk=(v,d=0)=>{if(!v||typeof v!=='object'||d>4)return;if([].concat(v['@type']||[]).includes('VideoObject'))add(v.name);for(const k of ['@graph','mainEntity','video','itemListElement','item'])for(const i of [].concat(v[k]||[]).slice(0,20))walk(i,d+1);};

    for(const node of [...document.querySelectorAll('script[type="application/ld+json"]')].slice(0,8))try{if(node.textContent.length<65536)for(const v of [].concat(JSON.parse(node.textContent)))walk(v);}catch{}

    add(document.querySelector('h1')?.textContent);add(document.title);return values.slice(0,20);

  }

  function readableTitle(values) {return values.filter(v=>typeof v==='string').map(v=>v.replace(/\s+/g,' ').trim().slice(0,512)).find(v=>v.length>2&&!/^(?:\d+|[a-f0-9]{16,}|video|player|embed|watch|untitled|detected video)(?:\s*[-|:]\s*\d+)?$/i.test(v))||'';}

  globalThis.SyphonMedia = { videoPageUrl,knownVideoPage,titleCandidates, readableTitle, collect, resolve, youtubeUrl, qualityLabel, qualityChoices, qualityTier, sizeLabel, durationLabel, playbackGesture, previewHint, mainPlayer, structuredVideos };

})();

