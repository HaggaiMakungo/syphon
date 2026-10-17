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
  function collect(video) {
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
    const youtube = youtubeUrl(video.baseURI);
    if (youtube) {
      const subtitles = options.filter(option => option.kind === "subtitle");
      options.splice(0, options.length, { url: youtube, label: "Best available", kind: "video", format: "mp4", youtube: true, supported: true, note: "" }, ...subtitles);
    }
    return { options, protected: Boolean(video.mediaKeys),
      streamOnly: Boolean(current) && !resolve(current, video.baseURI) };
  }
  globalThis.SyphonMedia = { collect, resolve, youtubeUrl, qualityLabel, qualityChoices, qualityTier, sizeLabel };
})();
