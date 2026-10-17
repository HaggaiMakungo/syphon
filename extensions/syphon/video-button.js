(() => {
  if (chrome.extension?.inIncognitoContext) return;
  if (globalThis.syphonVideoController) return;
  const controllers = new Map();
  const dismissedVideos = new Set();
  const inspections = new Map(), inspectionQueue = [];
  let activeInspections = 0;
  let stopped = false, scanTimer, preferredQuality;
  const validPreference = value => [144, 360, 480, 720, 1080, 1440, 2160].includes(value);
  function inspectionFor(url) {
    const entry = inspections.get(url);
    if (entry && !entry.pending && entry.expires <= Date.now()) { inspections.delete(url); return; }
    return entry;
  }
  function pumpInspections() {
    while (!stopped && activeInspections < 2 && inspectionQueue.length) {
      activeInspections++;
      inspectionQueue.shift()();
    }
  }
  function inspect(url) {
    const previous = inspectionFor(url);
    if (previous) return previous.promise;
    for (const key of inspections.keys()) inspectionFor(key);
    if (inspections.size >= 32) {
      const oldest = [...inspections].find(([, entry]) => !entry.pending);
      if (oldest) inspections.delete(oldest[0]);
      else return Promise.resolve({ message: "Media detection is busy. Try again shortly." });
    }
    const entry = { pending: true };
    entry.promise = new Promise(resolve => {
      inspectionQueue.push(async () => {
        try {
          if (SyphonMedia.youtubeUrl(url)) entry.reply = await chrome.runtime.sendMessage({ type: "syphon-player-info", url });
          if (!entry.reply?.media) entry.reply = await chrome.runtime.sendMessage({ type: "syphon-media-inspect", url }) ?? { message: "No inspection reply." };
        }
        catch { entry.reply = { message: "Could not inspect this source. Try again shortly." }; }
        finally {
          entry.pending = false;
          entry.expires = Date.now() + (entry.reply?.media ? 60000 : 3000);
          resolve(entry.reply); activeInspections--; pumpInspections();
        }
      });
    });
    inspections.set(url, entry); pumpInspections();
    return entry.promise;
  }
  const themeTokens = /* OV_TOKENS_START */"/* Stealth Hybrid: approved Obsidian Glass lab, 50% glow / 0% brightness / 0% border.\n   Canonical palette; build.mjs emits desktop, native and extension copies.\n   Quiet structural separators remain neutral; violet is light beneath the face. */\n:host {\n --ov-glow-strength:0.5;\n --ov-panel-brightness:0;\n --ov-border-intensity:0;\n --ov-canvas:#070709;\n --ov-shell:#08080b;\n --ov-panel:#050508;\n --ov-panel-raised:#101014;\n --ov-control:#14111d;\n --ov-control-hover:#1e1929;\n --ov-inset:#08080b;\n --ov-border:#1b1920;\n --ov-border-subtle:#121216;\n --ov-border-hover:#3a3347;\n --ov-border-focus:#baaaff;\n --ov-text-primary:#eeeef4;\n --ov-text-secondary:#b2afbf;\n --ov-text-muted:#9c95ad;\n --ov-text-disabled:#777181;\n --ov-accent:#a791ff;\n --ov-accent-hover:#baa2f0;\n --ov-accent-pressed:#7250ba;\n /* Deeper primary fills keep small off-white button labels above 4.5:1. */\n --ov-primary-top:#744be0;\n --ov-primary-bottom:#4b24ac;\n --ov-primary-hover:#7043d8;\n --ov-primary-pressed:#45228e;\n --ov-glow-soft:rgba(117,68,229,0.08);\n --ov-glow-medium:rgba(116,69,237,0.20);\n --ov-glow-strong:rgba(166,127,255,0.275);\n --ov-corner-light:rgba(125,77,255,0.21);\n --ov-corner-light-secondary:rgba(109,66,232,0.08);\n --ov-edge-light:rgba(255,255,255,0.0225);\n --ov-shadow:rgba(0,0,0,0.32);\n --ov-shadow-deep:rgba(0,0,0,0.65);\n --ov-selection:rgba(140,103,255,0.094);\n --ov-progress-track:#19151f;\n --ov-chart-line:#aa8eff;\n --ov-chart-grid:rgba(255,255,255,0.04);\n --ov-chart-fill:rgba(161,129,237,0.18);\n --ov-chart-clear:rgba(161,129,237,0);\n --ov-success:#4a9e6a;\n --ov-warning:#b87a2a;\n --ov-danger:#cf7979;\n --ov-cancelled:#a99bb9;\n --ov-success-surface:rgba(74,158,106,0.12);\n --ov-warning-surface:rgba(184,122,42,0.12);\n --ov-danger-surface:rgba(207,121,121,0.12);\n}\n"/* OV_TOKENS_END */;
  const css = themeTokens + `:host{all:initial;position:fixed;z-index:2147483647;font:14px/1.4 "Segoe UI",sans-serif;color:var(--ov-text-primary);color-scheme:dark}
    :host([hidden]){display:none!important}*{box-sizing:border-box}button{font:inherit;cursor:pointer;border:1px solid var(--ov-border-hover);border-radius:7px;background:var(--ov-control);color:var(--ov-text-primary);padding:8px 12px;text-align:left}
    button{box-shadow:0 3px 9px #0009,inset 0 1px 0 #794b9633}button:hover{background:var(--ov-control-hover)}button:focus-visible{outline:3px solid var(--ov-accent-hover);outline-offset:2px}button:disabled{cursor:default;opacity:.6}
    .toolbar{display:flex;justify-content:flex-end;gap:6px;align-items:center}
    .trigger{display:flex;align-items:center;justify-content:center;gap:8px;min-height:38px;padding:8px 11px;border:1px solid var(--ov-border-hover);border-radius:11px;background:radial-gradient(ellipse at 85% 130%,var(--ov-corner-light),transparent 65%),var(--ov-shell);color:var(--ov-text-primary);font-size:12px;line-height:1.4;font-weight:500;white-space:nowrap;box-shadow:0 3px 12px var(--ov-shadow-deep),inset 0 1px var(--ov-edge-light),inset 0 -1px var(--ov-glow-strong)}
    .trigger:after{content:"";width:6px;height:6px;margin-left:2px;margin-top:-3px;border-right:1.5px solid var(--ov-text-muted);border-bottom:1.5px solid var(--ov-text-muted);transform:rotate(45deg)}
    .trigger:hover{background:radial-gradient(ellipse at 85% 130%,var(--ov-glow-medium),transparent 75%),var(--ov-panel-raised);border-color:var(--ov-accent-pressed);box-shadow:0 4px 16px var(--ov-shadow-deep),inset 0 -1px var(--ov-glow-strong)}
    .trigger:active{background:var(--ov-inset);box-shadow:inset 0 2px 5px var(--ov-shadow-deep),inset 0 -1px var(--ov-glow-strong)}
    .trigger:focus-visible{outline:2px solid var(--ov-border-focus);outline-offset:3px}
    .trigger[aria-expanded="true"]:after{transform:rotate(225deg);margin-top:3px}
    .trigger-mark{display:block;object-fit:contain;width:22px;height:22px;flex-shrink:0}
    .dismiss{display:grid;place-items:center;width:30px;min-height:30px;padding:0;border-radius:9px;border-color:var(--ov-border-hover);font-size:18px;line-height:1;background:var(--ov-shell);color:var(--ov-text-muted);box-shadow:0 2px 8px var(--ov-shadow-deep)}.dismiss:hover{background:var(--ov-panel-raised);color:var(--ov-text-primary)}
    @media(pointer:coarse){.trigger{min-height:44px}.dismiss{width:44px;min-height:44px}}
    .panel{position:absolute;right:0;top:100%;margin-top:6px;padding:12px;background:var(--ov-panel);border:1px solid var(--ov-border-hover);border-radius:10px;width:290px;max-width:calc(100vw - 24px);max-height:min(640px,75vh);overflow:auto;box-shadow:0 4px 24px #0008}
    .caption-group{margin:10px 0;border:1px solid var(--ov-border-subtle);border-radius:8px;background:var(--ov-inset);overflow:hidden}
    .caption-group>summary{list-style:none;display:flex;align-items:center;gap:8px;padding:10px;cursor:pointer;font-size:13px;font-weight:600;color:var(--ov-text-primary);user-select:none}
    .caption-group>summary::-webkit-details-marker{display:none}.caption-group>summary:after{content:"";width:6px;height:6px;margin-left:auto;margin-right:3px;border-right:1.5px solid var(--ov-text-muted);border-bottom:1.5px solid var(--ov-text-muted);transform:rotate(45deg);flex-shrink:0}
    .caption-group[open]>summary:after{transform:rotate(225deg)}.caption-group>summary:hover{background:var(--ov-control-hover)}.caption-group>summary:focus-visible{outline:2px solid var(--ov-border-focus);outline-offset:-3px;border-radius:7px}
    .caption-count{font-size:11px;font-weight:400;color:var(--ov-text-muted)}.caption-options{max-height:200px;overflow:auto;padding:0 8px 6px}.caption-selection{margin:0 10px 8px;font-size:12px;color:var(--ov-accent);overflow-wrap:anywhere}
    .panel,.caption-options{scrollbar-gutter:stable;scrollbar-width:thin;scrollbar-color:var(--ov-accent-pressed) var(--ov-inset)}
    @supports selector(::-webkit-scrollbar){.panel,.caption-options{scrollbar-width:auto;scrollbar-color:auto}.panel::-webkit-scrollbar,.caption-options::-webkit-scrollbar{width:8px;height:8px}.panel::-webkit-scrollbar-track,.caption-options::-webkit-scrollbar-track{background:var(--ov-inset);border-radius:10px}.panel::-webkit-scrollbar-thumb,.caption-options::-webkit-scrollbar-thumb{background:linear-gradient(180deg,var(--ov-accent-pressed),var(--ov-border-hover));border:2px solid var(--ov-inset);border-radius:10px;min-height:32px}.panel::-webkit-scrollbar-thumb:hover,.caption-options::-webkit-scrollbar-thumb:hover{background:var(--ov-accent)}.panel::-webkit-scrollbar-button,.caption-options::-webkit-scrollbar-button{display:none;width:0;height:0}.panel::-webkit-scrollbar-corner,.caption-options::-webkit-scrollbar-corner{background:transparent}}
    .option{display:block;width:100%;margin:4px 0;padding:7px 10px;overflow-wrap:anywhere}.option[aria-pressed="true"]{border-color:var(--ov-accent-hover);background:var(--ov-control-hover)}.option.edge{color:var(--ov-text-secondary);font-size:12px}.confirm{display:block;width:100%;margin-top:12px;text-align:center;background:var(--ov-accent);border-color:var(--ov-border-focus);font-weight:600;position:sticky;bottom:0}button{transition:background-color 160ms ease,border-color 160ms ease,box-shadow 180ms ease}.panel{background:radial-gradient(ellipse at 0 0,var(--ov-corner-light),transparent 60%),var(--ov-panel);border-color:var(--ov-border-subtle)}.option[aria-pressed="true"]{background:var(--ov-selection);border-color:var(--ov-border-hover)}button:focus-visible{outline-color:var(--ov-border-focus)}.confirm:hover:not(:disabled){background:var(--ov-primary-hover)}.confirm{background:linear-gradient(165deg,var(--ov-primary-top),var(--ov-primary-bottom));box-shadow:0 3px 12px var(--ov-glow-soft)}@media(prefers-reduced-motion:reduce){*{transition:none!important;animation:none!important}}.confirm:disabled{background:var(--ov-panel);color:var(--ov-text-disabled);box-shadow:none;opacity:1}h2{font:600 14px "Segoe UI",sans-serif;margin:10px 0 5px}p{margin:8px 0;font-size:12px;overflow-wrap:anywhere}[hidden]{display:none!important}`;
  function add(video) {
    const host = document.createElement("div");
    host.setAttribute("data-syphon-video-button", "");
    const root = host.attachShadow({ mode: "closed" });
    const style = document.createElement("style"); style.textContent = css; root.append(style);
    const trigger = document.createElement("button"); trigger.className = "trigger"; trigger.type = "button";
    const triggerMark = document.createElement("img"); triggerMark.className = "trigger-mark"; triggerMark.alt = ""; triggerMark.setAttribute("aria-hidden", "true"); triggerMark.draggable = false;
    if (chrome.runtime.getURL) triggerMark.src = chrome.runtime.getURL("brand/icon-64.png");
    const triggerLabel = document.createElement("span"); triggerLabel.textContent = "Syphon this"; trigger.append(triggerMark, triggerLabel);
    const dismiss = document.createElement("button"); dismiss.className = "dismiss"; dismiss.textContent = "×";
    dismiss.setAttribute("aria-label", "Hide Syphon for this video"); dismiss.title = "Hide for this video";
    const toolbar = document.createElement("div"); toolbar.className = "toolbar"; toolbar.append(trigger, dismiss);
    trigger.setAttribute("aria-expanded", "false"); trigger.setAttribute("aria-controls", "syphon-options");
    const panel = document.createElement("section"); panel.className = "panel"; panel.id = "syphon-options"; panel.hidden = true;
    panel.setAttribute("aria-label", "Syphon video downloads");
    const options = document.createElement("div"), status = document.createElement("p");
    status.setAttribute("role", "status"); status.setAttribute("aria-live", "polite");
    const confirm=document.createElement("button");confirm.className="confirm";confirm.textContent="Syphon this";confirm.disabled=true;let selection=null;confirm.onclick=()=>{if(selection)send(selection.source,selection.format);};panel.append(options, status,confirm); root.append(toolbar, panel); document.documentElement.append(host);
    let subtitlesExpanded = false;
    let busy = false, inspectionRun, discovered = [], interaction = 0, dismissed = false, automaticIdentity, sourceTimer, sourcesPending = false;
    const knownMedia = new Map();
    const sourceIdentity = value => SyphonMedia.resolve(value, video.baseURI) || value || "";
    const identityNow = () => SyphonMedia.youtubeUrl(video.baseURI) || `${video.baseURI}\n${sourceIdentity(video.currentSrc || video.getAttribute("src"))}\n${[...video.querySelectorAll("source[src]")].map(s => sourceIdentity(s.getAttribute("src"))).join("\n")}`;
    let identity = identityNow();
    dismissed = dismissedVideos.has(identity);
    function syncIdentity() {
      const next = identityNow();
      if (next === identity) return;
      identity = next; dismissed = dismissedVideos.has(identity); discovered = []; inspectionRun = null; automaticIdentity = undefined;
      selection=null;knownMedia.clear();
      sourcesPending = false; clearTimeout(sourceTimer); interaction++; closePanel(); status.textContent = "";
    }
    function closePanel() { subtitlesExpanded = false;const captions=options.querySelector(".caption-group");if(captions)captions.open=false;panel.hidden = true; trigger.hidden = false; trigger.setAttribute("aria-expanded", "false");dismiss.setAttribute("aria-label", "Hide Syphon for this video");dismiss.title = "Hide for this video"; }
    function modelNow() {
      const model = SyphonMedia.collect(video);
      for (const source of discovered) if (!model.options.some(o => o.url === source.url))
        model.options.push({ ...source, kind: "video", label: "Page stream", supported: true, note: "Detected in page requests" });
      model.options = model.options.flatMap(source => {
        const fresh = inspectionFor(source.url)?.reply;
        if (fresh?.media) {
          knownMedia.set(source.url, fresh);
          if (knownMedia.size > 16) knownMedia.delete(knownMedia.keys().next().value);
        }
        // Keep detected choices visible while expired inspection data is refreshed.
        const result = fresh?.media ? fresh : knownMedia.get(source.url) || fresh;
        if (result?.media?.qualities?.length) return [...(source.youtube ? [{...source,label:'Best available',youtubeFormat:undefined,estimatedBytes:undefined}] : []), ...SyphonMedia.qualityChoices(result.media.qualities).map(choice => ({ ...source,
          label: SyphonMedia.qualityLabel(SyphonMedia.qualityTier(choice)) + (source.label === "Page stream" ? " · another video on this page" : ""),
          format: "mp4", videoStream: choice.youtubeFormat || choice.resolveAtDownload ? undefined : choice.videoStream, requestedHeight: choice.resolveAtDownload ? SyphonMedia.qualityTier(choice) : undefined, audioStream: choice.youtubeFormat ? undefined : choice.audioStream,
          youtubeFormat: choice.youtubeFormat, qualityHeight: SyphonMedia.qualityTier(choice), estimatedBytes: choice.estimatedBytes, title: result.media.title, note: "" }))];
        if (result && !result.media && (source.manifest || source.youtube)) return [{ ...source, supported: !!source.youtube, note: source.youtube ? "Qualities unavailable — automatic selection remains available" : result.message || "Could not inspect stream" }];
        return [source];
      });
      for (const result of knownMedia.values()) for (const track of result.media?.subtitles || []) if (!model.options.some(o => o.url === track.url) && SyphonMedia.resolve(track.url, video.baseURI)) model.options.push({url:track.url,label:track.label,kind:'subtitle',supported:true});
      for (const result of knownMedia.values()) for (const track of result.media?.captions || []) if (!model.options.some(o => o.kind === 'subtitle' && o.label === track.label)) model.options.push({kind:'subtitle',label:track.label,supported:false,note:track.note || 'Embedded captions — separate saving is not available yet'});
      return model;
    }
    async function send(source, format) {
      if (busy) return;
      interaction++;
      busy = true; render(); status.textContent = "Choose your download options in Syphon…";
      try {
        const response = await chrome.runtime.sendMessage({ type: "syphon-media-download", url: source.url,
          title: source.kind === "video" && source.label.indexOf("another video on this page") < 0 ? (source.title || video.getAttribute("title") || document.title || "").slice(0, 512) || undefined : undefined,
          showOptions: true, quality: format === "mp3" ? "MP3 audio" : source.label.slice(0, 120), estimatedBytes: format === "mp3" ? undefined : source.estimatedBytes,
          format: format || source.format || (source.manifest ? "mp4" : undefined),
          videoStream: format === "mp3" ? undefined : source.videoStream, audioStream: source.audioStream,
          youtubeFormat: format === "mp3" ? undefined : source.youtubeFormat, qualityHeight: format === "mp3" ? undefined : source.requestedHeight });
        if (response?.status === "accepted" && !format && source.kind === "video" && validPreference(source.qualityHeight)) {
          preferredQuality = source.qualityHeight;
          try { await chrome.storage.local.set({ preferredVideoQuality: preferredQuality }); }
          catch { /* The confirmed download stays successful if a preference cannot be saved. */ }
        }
        status.textContent = response?.message || "No confirmation. Open the extension to check or retry.";
      } catch { status.textContent = "Connection lost. Reload this page, or check the extension's last handoff."; }
      finally { busy = false; render(); }
    }
    async function discover(includeDirect = false) {
      syncIdentity();
      if (stopped || dismissed || !video.isConnected || SyphonMedia.collect(video).protected) return;
      if (inspectionRun?.identity === identity) return;
      const run = { identity }; inspectionRun = run;
      const current = () => !stopped && !dismissed && video.isConnected && identityNow() === run.identity && inspectionRun === run;
      const startedAt = interaction;
      status.textContent = "Detecting media qualities…";
      try {
        const youtube = SyphonMedia.youtubeUrl(video.baseURI);
        const result = youtube ? { sources: [] } : await chrome.runtime.sendMessage({ type: "syphon-media-sources" });
        if (!current()) return;
        discovered = (result?.sources ?? []).filter(s => SyphonMedia.resolve(s.url, video.baseURI)).slice(-12);
        render();
        if (!SyphonMedia.collect(video).protected) {
          const sources = modelNow().options.filter(s => s.kind === "video" && !inspectionFor(s.url)?.reply?.media && (includeDirect === true || s.youtube || s.manifest || /unspecified|Page stream/.test(s.label)));
          const seen = new Set();
          for (const source of sources) {
            if (seen.has(source.url)) continue;
            if (seen.size >= 4) break;
            seen.add(source.url);
          }
          await Promise.all([...seen].map(async url => { await inspect(url); if (current()) render(); }));
        }
        if (current() && interaction === startedAt) status.textContent = "";
      } catch { if (current()) status.textContent = "Could not inspect streams. Reload the extension and enable video detection permissions."; }
      finally { if (inspectionRun === run) { inspectionRun = null; if (sourcesPending) onSources(); } }
    }
    function onPlayback() {
      syncIdentity();
      if (stopped || dismissed || !video.isConnected || automaticIdentity === identity) return;
      automaticIdentity = identity;
      discover(true);
    }
    function onSources() {
      syncIdentity();
      if (stopped || dismissed || !video.isConnected || SyphonMedia.youtubeUrl(video.baseURI) || automaticIdentity !== identity && video.paused) return;
      sourcesPending = true;
      if (inspectionRun) return;
      clearTimeout(sourceTimer);
      sourceTimer = setTimeout(() => { sourcesPending = false; discover(true); }, 150);
    }
    function render() {
      subtitlesExpanded = options.querySelector(".caption-group")?.open ?? subtitlesExpanded;
      options.replaceChildren();
      const model = modelNow();
      for (const [kind, title] of [["video", "Video quality"], ["subtitle", "Subtitles & closed captions"]]) {
        const sources = model.options.filter(option => option.kind === kind);
        let container = options;
        if (kind === "subtitle") {
          const group = document.createElement("details"); group.className = "caption-group"; group.open = subtitlesExpanded;
          const summary = document.createElement("summary");summary.textContent = title;
          const count = document.createElement("span");count.className = "caption-count";count.textContent = String(sources.length);summary.append(count);group.append(summary);
          const tracks = document.createElement("div");tracks.className = "caption-options";tracks.setAttribute("aria-label", "Subtitle and closed-caption tracks");group.append(tracks);
          if(selection?.source.kind === "subtitle"){const selected=document.createElement("p");selected.className="caption-selection";selected.textContent=selection.source.label;group.insertBefore(selected,tracks);}
          options.append(group);container = tracks;
        } else { const heading = document.createElement("h2"); heading.textContent = title; options.append(heading); }
        if (!sources.length) { const note = document.createElement("p"); note.textContent = kind === "video" ? "No direct video file exposed by this player." : "No subtitle or closed-caption tracks detected."; container.append(note); }
        for (const source of sources) {
          const button = document.createElement("button"); button.className = "option";
          const preferred = kind === "video" && source.qualityHeight === preferredQuality && validPreference(preferredQuality);
          button.textContent = source.label + (preferred ? " · Preferred" : "") + (SyphonMedia.sizeLabel(source.estimatedBytes) ? ` · ${SyphonMedia.sizeLabel(source.estimatedBytes)}` : "") + (source.note ? ` — ${source.note}` : "");
          button.setAttribute("aria-pressed", String(selection ? selection.source.url===source.url && selection.source.label===source.label && !selection.format : false));
          button.disabled = busy || !source.supported || (kind === "video" && model.protected);
          if(source.qualityHeight>=2160||source.qualityHeight<=144)button.classList.add("edge");button.addEventListener("click", () => {selection={source};status.textContent="";render();}); container.append(button);
        }
      }
      if (validPreference(preferredQuality) && model.options.some(s => s.qualityHeight) && !model.options.some(s => s.qualityHeight === preferredQuality)) options.append(Object.assign(document.createElement('p'),{textContent:`Your preferred quality (${SyphonMedia.qualityLabel(preferredQuality)}) isn't available.`}));
      const audioSource = model.options.find(source => source.kind === "video" && source.supported);
      const audioHeading = document.createElement("h2"); audioHeading.textContent = "Audio only"; options.append(audioHeading);
      const mp3 = document.createElement("button"); mp3.className = "option"; mp3.textContent = "Convert audio to MP3"; mp3.disabled = busy || model.protected || !audioSource;
      mp3.setAttribute("aria-pressed",String(selection?.format==="mp3"));mp3.addEventListener("click", () => {selection={source:audioSource,format:"mp3"};status.textContent="";render();}); options.append(mp3);
      if (model.protected) {
        const note = document.createElement("p"); note.textContent = "Protected video cannot be downloaded."; options.append(note);
      }
      const available=selection&&model.options.some(s=>s.url===selection.source.url&&s.label===selection.source.label&&s.supported);confirm.disabled=busy||model.protected||!available;confirm.textContent=busy?"Sending…":"Syphon this";
    }
    trigger.addEventListener("pointerenter", discover);
    trigger.addEventListener("focus", discover);
    trigger.addEventListener("click", () => { syncIdentity(); if (!panel.hidden) { closePanel(); return; } panel.hidden = false; trigger.setAttribute("aria-expanded", "true");dismiss.setAttribute("aria-label", "Close download options");dismiss.title = "Close download options";position();render();discover(); });
    dismiss.addEventListener("click", () => {
      if (!panel.hidden) { closePanel(); trigger.focus(); return; }
      dismissedVideos.add(identity);
      if (dismissedVideos.size > 32) dismissedVideos.delete(dismissedVideos.values().next().value);
      dismissed = true; interaction++; closePanel(); host.hidden = true; position();
    });
    const onMetadata = () => { syncIdentity(); position(); if (!panel.hidden) render(); if (automaticIdentity === identity) onSources(); };
    video.addEventListener("loadedmetadata", onMetadata); video.addEventListener("resize", onMetadata);
    video.textTracks?.addEventListener?.("addtrack", onMetadata); video.textTracks?.addEventListener?.("removetrack", onMetadata);
    video.addEventListener("play", onPlayback); video.addEventListener("playing", onPlayback);
    root.addEventListener("keydown", event => { if (event.key === "Escape") { closePanel(); trigger.focus(); } });
    function position() {
      syncIdentity();
      dismissed = dismissedVideos.has(identity);
      if (dismissed) closePanel();
      // Players may fullscreen either the video itself or its surrounding controls.
      // Hide the whole overlay and close its menu without persisting a dismissal.
      if (document.fullscreenElement) { closePanel(); host.hidden = true; return; }
      const rect = video.getBoundingClientRect();
      const visible = rect.width >= 160 && rect.height >= 90 && rect.bottom > 0 && rect.top < innerHeight && rect.right > 0 && rect.left < innerWidth;
      host.hidden = dismissed || !visible;
      // Keep the toolbar eight pixels inside the video, independent of panel width.
      host.style.right = `${Math.max(8, innerWidth - rect.right + 8)}px`;
      panel.style.maxWidth = `${Math.max(140, Math.min(290, rect.right - 16, innerWidth - 24))}px`;
      host.style.top = `${Math.max(8, Math.min(rect.top + 8, innerHeight - 48))}px`;
      const top = parseFloat(host.style.top), below = innerHeight - top - 48, above = top - 12;
      const openUp = below < 320 && above > below;
      panel.style.top = openUp ? "auto" : "100%"; panel.style.bottom = openUp ? "100%" : "auto";
      panel.style.marginTop = openUp ? "0" : "6px"; panel.style.marginBottom = openUp ? "6px" : "0";
      panel.style.maxHeight = `${Math.max(100, Math.min(640, openUp ? above : below))}px`;
      if (!video.paused && !video.ended) onPlayback();
    }
    controllers.set(video, { host, position, render, closePanel, onSources, cleanup() {
      clearTimeout(sourceTimer); video.textTracks?.removeEventListener?.("addtrack", onMetadata); video.textTracks?.removeEventListener?.("removetrack", onMetadata); video.removeEventListener("play", onPlayback); video.removeEventListener("playing", onPlayback);
      video.removeEventListener("loadedmetadata", onMetadata); video.removeEventListener("resize", onMetadata); host.remove();
    } }); position();
  }
  function scan() {
    if (stopped) return;
    for (const [video, controller] of controllers) {
      if (!video.isConnected) { controller.cleanup(); controllers.delete(video); }
    }
    for (const video of document.querySelectorAll("video")) {
      if (controllers.size >= 20) break;
      if (!controllers.has(video)) add(video);
    }
    position();
  }
  function position() { for (const controller of controllers.values()) controller.position(); }
  function schedule() { if (!scanTimer && !stopped) scanTimer = setTimeout(() => { scanTimer = null; scan(); }, 150); }
  const observer = new MutationObserver(schedule);
  observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ["src", "label", "data-quality", "res"] });
  const interval = setInterval(position, 1000);
  addEventListener("scroll", position, true); addEventListener("resize", position);
  document.addEventListener("fullscreenchange", position);
  function onOutside(event) {
    const path = event.composedPath();
    for (const controller of controllers.values()) if (!path.includes(controller.host)) controller.closePanel();
  }
  document.addEventListener("pointerdown", onOutside, true);
  document.addEventListener("click", onOutside, true);
  function stop() {
    stopped = true; observer.disconnect(); clearInterval(interval); clearTimeout(scanTimer);
    removeEventListener("scroll", position, true); removeEventListener("resize", position);
    document.removeEventListener("fullscreenchange", position);
    document.removeEventListener("pointerdown", onOutside, true); document.removeEventListener("click", onOutside, true);
    inspections.clear(); inspectionQueue.length = 0; dismissedVideos.clear();
    chrome.runtime.onMessage.removeListener(onMessage); chrome.storage.onChanged.removeListener(onChanged);
    for (const controller of controllers.values()) controller.cleanup(); controllers.clear();
    delete globalThis.syphonVideoController;
  }
  function onMessage(message) {
    if (message?.type === "syphon-detection-off") stop();
    if (message?.type === "syphon-media-found") for (const controller of controllers.values()) controller.onSources();
  }
  function onChanged(changes, area) {
    if (area !== "local") return;
    if (changes.detectVideos?.newValue === false) stop();
    if (changes.preferredVideoQuality) {
      preferredQuality = validPreference(changes.preferredVideoQuality.newValue) ? changes.preferredVideoQuality.newValue : undefined;
      for (const controller of controllers.values()) controller.render();
    }
  }
  chrome.runtime.onMessage.addListener(onMessage); chrome.storage.onChanged.addListener(onChanged);
  globalThis.syphonVideoController = { stop };
  chrome.storage.local.get(["detectVideos", "preferredVideoQuality"]).then(({ detectVideos, preferredVideoQuality }) => {
    preferredQuality = validPreference(preferredVideoQuality) ? preferredVideoQuality : undefined;
    if (detectVideos) scan(); else stop();
  }).catch(stop);
})();
