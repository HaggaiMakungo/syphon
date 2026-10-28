(() => {
  if (chrome.extension?.inIncognitoContext) return;
  const controllerVersion='0.8.5';
  if(globalThis.syphonVideoController?.version===controllerVersion)return;
  try{globalThis.syphonVideoController?.stop?.();}catch{}
  for(const host of document.querySelectorAll('[data-syphon-video-button],[data-syphon-page-videos]'))host.remove();
  const controllers = new Map();
  const playerIds=new WeakMap();let mainCandidate=null,claimRetry=0,selectedUntil=0;
  let activeVideo=null,claimId,claimPending=false;
  const encryptedMessage = "Yeah... I can't download this! Must be encrypted.";
  const encryptedReply = reply => !reply?.media && /\b(?:encrypted|drm|protected (?:hls|dash|playback|media|video))\b/i.test(reply?.message || '');
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
  const css = "/* Compact, joined video controls. Isolated inside the player shadow root. */\r\n:host{all:initial;position:fixed;z-index:2147483646;color-scheme:dark;font:13px/1.5 \"Segoe UI\",Arial,sans-serif}\r\n:host([hidden]){display:none!important}\r\n*{box-sizing:border-box} [hidden]{display:none!important}\r\n.video-example{width:100%;font:13px/1.5 \"Segoe UI\",Arial,sans-serif;color:#eadff2}\r\n.video-example button{font:inherit;cursor:pointer;color:inherit;border:1px solid transparent;transition:background .15s,border-color .15s,box-shadow .15s}\r\n.video-example button:disabled{opacity:.45;cursor:default}.video-example button:focus-visible,summary:focus-visible{outline:2px solid #d4a4f1;outline-offset:2px}\r\n.orbital-icon{width:16px;height:16px;flex:none;fill:currentColor}\r\n.video-example .toolbar{width:100%;position:relative;border:1px solid #c296e256;border-radius:22px;background:linear-gradient(125deg,#3e2850,#1c1129);box-shadow:inset 0 1px #f0d6ff1c,0 6px 20px #0005}\r\n.video-example .video-launcher{display:flex;align-items:center;gap:8px;width:100%;min-height:40px;padding:7px 32px 7px 12px;border:0;border-radius:inherit;background:transparent;box-shadow:none;text-align:left}\r\n.video-launcher span{font-size:12px;font-weight:600;white-space:nowrap}.video-example .video-launcher small{display:none}.trigger-mark{height:24px;width:24px;object-fit:contain;flex:none}\r\n.video-example .dismiss{display:grid;place-items:center;position:absolute;right:6px;top:8px;width:24px;height:24px;padding:4px;border:0;border-radius:50%;background:transparent;color:#c9b2d8}.video-example .dismiss:hover{background:#d9a5f522;color:#fff}\r\n.toolbar:has([aria-expanded=true]){border-radius:20px 20px 0 0;box-shadow:inset 0 1px #f0d6ff1c;border-bottom-color:#c296e220}\r\n.video-example .panel{position:absolute;width:100%;max-width:100%;overflow:auto;overscroll-behavior:contain;padding:14px;border:1px solid #c296e256;border-top:0;border-radius:0 0 20px 20px;background:radial-gradient(ellipse at 0 0,#62377d20,transparent 55%),linear-gradient(120deg,#1d1426,#100b17 80%);box-shadow:0 16px 32px #0007;scrollbar-width:thin;scrollbar-color:#74567f #17101f}\r\n.video-example .panel::-webkit-scrollbar{width:6px}.video-example .panel::-webkit-scrollbar-thumb{background:#74567f;border-radius:6px}.video-example .panel::-webkit-scrollbar-track{background:#17101f;border-radius:6px}\r\n.video-context{display:flex;align-items:center;gap:9px;margin-bottom:14px}.video-context>div:last-child{min-width:0}.video-thumb{flex:none;width:28px;height:28px}.video-thumb img{width:28px;height:28px;object-fit:contain}.video-context h1{font:600 14px/1.35 \"Segoe UI\",Arial,sans-serif;margin:0 0 3px;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;overflow-wrap:anywhere}.video-context p{font-size:11px;color:#b49bc5;margin:0}\r\n.video-example .media-tabs{display:flex;gap:3px;padding:3px;margin:0 0 12px;border:1px solid #c39ddd1a;border-radius:12px;background:#0d0912}.video-example .media-tabs button{display:flex;align-items:center;justify-content:center;gap:5px;flex:1;min-width:0;padding:7px 3px;font-size:11px;border-radius:9px;background:transparent}.media-tabs .orbital-icon{width:14px;height:14px}.video-example .media-tabs button.current{background:#76469344;color:#ebcdff}\r\n.media-tab-content h2{font-size:12px;font-weight:600;margin:12px 0 8px;color:#d9c5e4}.media-tab-content p,.media-status{font-size:11px;color:#b49bc5;line-height:1.6;overflow-wrap:anywhere}.media-status:empty{display:none}\r\n.video-example .quality-choice{display:flex;align-items:center;justify-content:space-between;gap:8px;width:100%;min-height:38px;margin-top:6px;padding:9px 10px;border:1px solid #cba7df1b;border-radius:12px;background:linear-gradient(125deg,#2c2333,#1e1725);text-align:left;font-size:12px}.quality-choice>span{min-width:0}.quality-choice strong{font-size:12px;font-weight:500}.quality-choice small{display:block;font-size:10px;overflow-wrap:anywhere;color:#b09aba}.quality-choice small:empty{display:none}.quality-choice em{font-size:10px;font-style:normal;color:#b09aba;white-space:nowrap}.video-example .quality-choice[aria-pressed=true]{border-color:#cb94eb88;background:#774c9540;box-shadow:inset 2px 0 #c78fe9}.video-example .quality-choice:hover:not(:disabled){border-color:#cb94eb66}\r\n.caption-group summary{cursor:pointer;font-size:12px;padding:8px 0}.caption-count{float:right;color:#b09aba}.caption-selection{font-size:11px}\r\n.video-example .media-footer{display:flex;align-items:center;gap:8px;margin:12px 0 0;padding:12px 0 0;border-top:1px solid #c395dc20;background:none}.video-example .media-footer button{font-size:10px;line-height:1.4;white-space:nowrap;min-width:0;padding:9px 6px;gap:4px;border-radius:11px;display:inline-flex;align-items:center;justify-content:center}.video-example .media-footer .text-button{background:transparent;border:0;color:#c7a6d8}.media-footer .text-button .orbital-icon{width:13px;height:13px}.video-example .media-footer .confirm{flex:1;background:linear-gradient(130deg,#bc84e5,#8b4bbf);border:1px solid #dcaaf482;color:#fff;box-shadow:inset 0 1px #f8ddff2b}\r\n.video-example.open-up .toolbar{border-radius:0 0 20px 20px}.video-example.open-up .panel{border-radius:20px 20px 0 0;border-top:1px solid #c296e256;border-bottom:0}\r\n@media(prefers-reduced-motion:reduce){*,*:before,*:after{animation:none!important;transition:none!important}}\r\n\r\n.catalog-bar{font:12px/1.5 \"Segoe UI\",sans-serif}.catalog-trigger,.catalog-bar .move-handle,.catalog-list button{font:inherit;color:#eadff2;background:linear-gradient(125deg,#35223f,#211429);border:1px solid #c296e250;border-radius:12px;padding:8px 10px;cursor:pointer}.catalog-trigger{border-radius:20px}.catalog-list{font:12px/1.5 \"Segoe UI\",sans-serif;color:#eadff2}.catalog-list p{font-size:11px;color:#b49bc5}.catalog-list button:disabled{opacity:.45;cursor:default}\r\n";
  /* OV_VISUAL_START */const identityCss="/* The browser companion shares Syphon's ink, lilac and stepped geometry. */\n:root,:host{--icon-accent:#c08cf8;--orbital-text:#f1e7f8;--orbital-muted:#b6a4c3;--orbital-ease:cubic-bezier(.16,1,.3,1)}\nbody,button,input,select{font-feature-settings:\"cv02\",\"cv03\",\"cv04\",\"cv11\";text-rendering:optimizeLegibility}\nbody{background:#0d0913;color:var(--orbital-text);color-scheme:dark}\n.popup-window{background:linear-gradient(145deg,#a589b94d,#3e2c4c60 24%,#18101e 70%)}\n.popup-window .dialog-core,.video-surface{background:radial-gradient(ellipse at 0 0,#62377d20,transparent 55%),linear-gradient(120deg,#1d1426,#100b17 80%)}\n/* Browser action popups have their own native outline. Fill that host instead\n   of exposing a rectangular backing behind a second large rounded shell. */\nhtml:has(body>.popup-window),body:has(>.popup-window){background:#100b17!important}\nbody>.popup-window,body>.popup-window>.dialog-core{border-radius:0!important;border:0!important;box-shadow:none!important}\nbody>.popup-window>.dialog-core>.popup-head,body>.popup-window>.dialog-core>.popup-footer{border-radius:0!important}\n.popup-head{background:radial-gradient(ellipse at 0 0,#8a4fa52b,transparent 75%),linear-gradient(120deg,#291b34,#160d20);border-bottom-color:#c490e427}\n.popup-head h1{letter-spacing:-.5px;color:#f5e8ff}\n.popup-head p,.feature-copy small,.handoff-note,.feature-help,.popup-footer{color:var(--orbital-muted)}\n.popup-mark{background:radial-gradient(ellipse at 35% 10%,#684080,#2c1938);border:1px solid #c88fe23b;box-shadow:inset 0 1px 0 #e6c0fb26,0 5px 16px #0003}\n.connection-chip{background:#a06bbc18;color:#d3b8e6;border-color:#d0a6e52a}\n.connection-chip[data-state=replied]{background:#83a79113;color:#c5dfce;border-color:#9fbba12b}\n.connection-chip[data-state=unavailable]{background:#b8799715;color:#e4b6cb;border-color:#c18ba931}\n.handoff-card{background:linear-gradient(130deg,#30213b,#1c1425);border-color:#c78fe225;box-shadow:inset 0 1px 0 #e2bff712,0 4px 10px #0002}\n.handoff-card[data-state=received]{border-color:#a8c0ad2b}\n.handoff-card[data-state=attention]{border-color:#cc8ba740;background:linear-gradient(130deg,#392133,#211622)}\n.file-copy strong,.feature-copy strong{color:#ecdef4;letter-spacing:-.12px}\n.file-copy span{color:#baa6c8}\n.eyebrow,.feature-set legend{color:#bba3cc;letter-spacing:1px;font-size:10px;font-weight:600}\n.feature{border-color:#ca9ee11a}\n.feature-icon,.handoff-icon{color:#cda3e5}\n.switch>i{background:#3b2a49;border-color:#c59ed52b;box-shadow:inset 0 1px 3px #0005;transition:background 160ms,box-shadow 160ms}\n.switch input:checked+i{background:linear-gradient(120deg,#c38bea,#8d50bd);box-shadow:0 0 13px #a86dd028,inset 0 1px 0 #edc4ff33}\n.switch i:before,.switch i:after{transition:transform 160ms var(--orbital-ease),left 160ms var(--orbital-ease),background 160ms}\n.feature-help summary,.popup-footer summary,.text-button{color:#bd9cd3}\n.feature-help summary:hover,.popup-footer summary:hover{color:#edd0ff}\n.popup-footer{background:linear-gradient(110deg,#25172f,#160e1e);border-top-color:#c59bd91c}\n.inline-actions button,.popup-footer button,.media-footer button{color:#e0c9ed;border-color:#c692e038;background:linear-gradient(140deg,#3b2948,#23162e)}\n.inline-actions button:hover,.popup-footer button:hover{background:linear-gradient(140deg,#50305f,#30203e);border-color:#d5a8ed55}\n.inline-actions button.muted{background:none;color:#ac91be}\n.video-example{--icon-accent:#c08cf8;color:#eadff2;font:400 13px/1.5 Inter,\"Segoe UI\",sans-serif;width:100%;max-width:100%;min-width:0}\n.toolbar{position:relative;width:100%}\n.video-example button{font-family:inherit;font-weight:400}\n.video-example .video-launcher{width:100%;box-sizing:border-box;min-height:58px;padding:12px 38px 12px 16px;gap:10px}\n.video-launcher span{font-weight:600}\n.video-launcher small{font-size:11px;font-weight:400}\n.video-example .dismiss{position:absolute;right:8px;top:8px;width:25px;height:25px;border:0;border-radius:8px;background:transparent;color:#c9b2d8;display:grid;place-items:center}\n.video-example .dismiss:hover{background:#d9a5f51a;color:#f2d9ff}\n.video-example .panel{width:100%;box-sizing:border-box;padding:18px}\n.video-example .intro{gap:10px;margin-bottom:17px}\n.video-example .intro h1{font-size:19px;line-height:1.3;font-weight:600;letter-spacing:-.4px}\n.video-example .intro p{font-size:12px;line-height:1.5;font-weight:400;color:#b49bc5}\n.video-example .media-header{gap:10px;margin-bottom:18px;padding-bottom:14px}\n.video-example .media-header h1{font-size:19px;line-height:1.3;font-weight:600;letter-spacing:-.4px;margin:0 0 5px}\n.video-example .media-header p{font-size:12px;line-height:1.5;font-weight:400;color:#b49bc5;margin:0}\n.video-example .video-thumb{width:26px;height:30px;flex-shrink:0}\n.video-example .video-thumb img{width:26px;height:26px}\n.video-example .media-tabs{display:flex;gap:3px;padding:4px;margin-bottom:16px}\n.video-example .media-tabs button{font-size:12px;padding:8px 9px;gap:5px}\n.video-example .media-tab-content h2,.video-example .media-tab-content h3{font-size:14px;font-weight:600;line-height:1.4;margin:14px 0}\n.video-example .quality-choice{font-size:12px;line-height:1.5;padding:12px;gap:9px}\n.video-example .media-footer{display:flex;gap:8px;align-items:center;padding:12px 0 0;margin-top:14px}\n.video-example .media-footer button{font-size:11px;line-height:1.4;padding:10px 8px;min-width:0;white-space:nowrap}\n.video-example .media-footer .confirm{flex:1}\n.video-launcher{background:linear-gradient(125deg,#3e2850,#1c1129);border-color:#c296e256;box-shadow:inset 0 1px 0 #f0d6ff1c,0 8px 22px #0005}\n.video-launcher:hover{border-color:#dbadfa88;box-shadow:inset 0 1px 0 #f0d6ff25,0 8px 26px #6a329636}\n.video-launcher small{color:#bca2cd}\n.video-surface{border-color:#cc9be33b;box-shadow:inset 0 1px 0 #e3c1f612,0 18px 38px #0007}\n.media-header{border-bottom-color:#c49dd51b}\n.media-header strong{color:#f2e5fb;letter-spacing:-.2px}\n.media-tabs{border-color:#c39ddd1a}\n.media-tabs button{display:inline-flex;align-items:center;gap:7px;color:#b49bc5}\n.media-tabs button.current{background:linear-gradient(110deg,#7847953b,#4c2b642e);color:#ebcdff;box-shadow:inset 0 1px 0 #d4b0eb15}\n.quality-choice{background:linear-gradient(120deg,#30213b,#1d1426);border-color:#bc8dd726;transition:background 140ms,border-color 140ms,transform 140ms}\n.quality-choice:hover,.quality-choice.selected{border-color:#cea0ed66;background:linear-gradient(120deg,#52326377,#2a1a3877)}\n.media-footer{border-top-color:#c395dc20;background:linear-gradient(100deg,#2a1a35,#190f23)}\n.media-footer .confirm{background:linear-gradient(130deg,#bc84e5,#8b4bbf);border:1px solid #dcaaf482;color:#fff;box-shadow:inset 0 1px 0 #f8ddff2b,0 4px 14px #8d45b02a}\n.orbital-icon{display:inline-block;width:17px;height:17px;vertical-align:middle;flex-shrink:0;overflow:visible}\n.dismiss .orbital-icon{width:14px;height:14px}\n.media-footer button{display:inline-flex;align-items:center;justify-content:center;gap:7px}\n.feature-label,.handoff-card,button,summary{transition:background 150ms,color 150ms,border-color 150ms,box-shadow 180ms,transform 150ms var(--orbital-ease)}\nbutton:active:not(:disabled){transform:translateY(1px) scale(.98)}\nbutton:focus-visible,summary:focus-visible,input:focus-visible{outline:2px solid #d4a4f1;outline-offset:3px}\n.popup-body{animation:orbital-companion-in 180ms var(--orbital-ease) both}\n.panel:not([hidden]){animation:orbital-companion-in 180ms var(--orbital-ease) both;transform-origin:top center}\n.media-tab-content:not([hidden]){animation:orbital-companion-in 150ms var(--orbital-ease) both}\n.handoff-card[aria-busy=true]{border-color:#c691e380}\n.handoff-card[aria-busy=true] .handoff-icon{animation:orbital-companion-breathe 800ms ease-in-out infinite alternate}\n@keyframes orbital-companion-in{from{opacity:0;transform:translateY(4px)}to{opacity:1;transform:none}}\n@keyframes orbital-companion-breathe{from{opacity:.5}to{opacity:1}}\n@media(prefers-reduced-motion:reduce){*,*:before,*:after{animation:none!important;transition:none!important;scroll-behavior:auto!important}}\n";const orbitalIcons={"download":"<svg class=\"icon icon-tabler-download icon-phosphor\" data-icon-family=\"phosphor\" aria-hidden=\"true\" focusable=\"false\"  xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" fill=\"currentColor\"><path d=\"M224,144v64a8,8,0,0,1-8,8H40a8,8,0,0,1-8-8V144a8,8,0,0,1,16,0v56H208V144a8,8,0,0,1,16,0Zm-101.66,5.66a8,8,0,0,0,11.32,0l40-40a8,8,0,0,0-11.32-11.32L136,124.69V32a8,8,0,0,0-16,0v92.69L93.66,98.34a8,8,0,0,0-11.32,11.32Z\"/></svg>","refresh":"<svg class=\"icon icon-tabler-refresh icon-phosphor\" data-icon-family=\"phosphor\" aria-hidden=\"true\" focusable=\"false\"  xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" fill=\"currentColor\"><path d=\"M224,48V96a8,8,0,0,1-8,8H168a8,8,0,0,1,0-16h28.69L182.06,73.37a79.56,79.56,0,0,0-56.13-23.43h-.45A79.52,79.52,0,0,0,69.59,72.71,8,8,0,0,1,58.41,61.27a96,96,0,0,1,135,.79L208,76.69V48a8,8,0,0,1,16,0ZM186.41,183.29a80,80,0,0,1-112.47-.66L59.31,168H88a8,8,0,0,0,0-16H40a8,8,0,0,0-8,8v48a8,8,0,0,0,16,0V179.31l14.63,14.63A95.43,95.43,0,0,0,130,222.06h.53a95.36,95.36,0,0,0,67.07-27.33,8,8,0,0,0-11.18-11.44Z\"/></svg>","video":"<svg class=\"icon icon-tabler-video icon-phosphor\" data-icon-family=\"phosphor\" aria-hidden=\"true\" focusable=\"false\"  xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" fill=\"currentColor\"><path d=\"M251.77,73a8,8,0,0,0-8.21.39L208,97.05V72a16,16,0,0,0-16-16H32A16,16,0,0,0,16,72V184a16,16,0,0,0,16,16H192a16,16,0,0,0,16-16V159l35.56,23.71A8,8,0,0,0,248,184a8,8,0,0,0,8-8V80A8,8,0,0,0,251.77,73ZM192,184H32V72H192V184Zm48-22.95-32-21.33V116.28L240,95Z\"/></svg>","music":"<svg class=\"icon icon-tabler-music icon-phosphor\" data-icon-family=\"phosphor\" aria-hidden=\"true\" focusable=\"false\"  xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" fill=\"currentColor\"><path d=\"M212.92,17.69a8,8,0,0,0-6.86-1.45l-128,32A8,8,0,0,0,72,56V166.08A36,36,0,1,0,88,196V110.25l112-28v51.83A36,36,0,1,0,216,164V24A8,8,0,0,0,212.92,17.69ZM52,216a20,20,0,1,1,20-20A20,20,0,0,1,52,216ZM88,93.75V62.25l112-28v31.5ZM180,184a20,20,0,1,1,20-20A20,20,0,0,1,180,184Z\"/></svg>","file-text":"<svg class=\"icon icon-tabler-file-text icon-phosphor\" data-icon-family=\"phosphor\" aria-hidden=\"true\" focusable=\"false\"  xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" fill=\"currentColor\"><path d=\"M213.66,82.34l-56-56A8,8,0,0,0,152,24H56A16,16,0,0,0,40,40V216a16,16,0,0,0,16,16H200a16,16,0,0,0,16-16V88A8,8,0,0,0,213.66,82.34ZM160,51.31,188.69,80H160ZM200,216H56V40h88V88a8,8,0,0,0,8,8h48V216Zm-32-80a8,8,0,0,1-8,8H96a8,8,0,0,1,0-16h64A8,8,0,0,1,168,136Zm0,32a8,8,0,0,1-8,8H96a8,8,0,0,1,0-16h64A8,8,0,0,1,168,168Z\"/></svg>","x":"<svg class=\"icon icon-tabler-x icon-phosphor\" data-icon-family=\"phosphor\" aria-hidden=\"true\" focusable=\"false\"  xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" fill=\"currentColor\"><path d=\"M205.66,194.34a8,8,0,0,1-11.32,11.32L128,139.31,61.66,205.66a8,8,0,0,1-11.32-11.32L116.69,128,50.34,61.66A8,8,0,0,1,61.66,50.34L128,116.69l66.34-66.35a8,8,0,0,1,11.32,11.32L139.31,128Z\"/></svg>","circle-check":"<svg class=\"icon icon-tabler-circle-check icon-phosphor\" data-icon-family=\"phosphor\" aria-hidden=\"true\" focusable=\"false\"  xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" fill=\"currentColor\"><path d=\"M173.66,98.34a8,8,0,0,1,0,11.32l-56,56a8,8,0,0,1-11.32,0l-24-24a8,8,0,0,1,11.32-11.32L112,148.69l50.34-50.35A8,8,0,0,1,173.66,98.34ZM232,128A104,104,0,1,1,128,24,104.11,104.11,0,0,1,232,128Zm-16,0a88,88,0,1,0-88,88A88.1,88.1,0,0,0,216,128Z\"/></svg>","alert-triangle":"<svg class=\"icon icon-tabler-alert-triangle icon-phosphor\" data-icon-family=\"phosphor\" aria-hidden=\"true\" focusable=\"false\"  xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" fill=\"currentColor\"><path d=\"M236.8,188.09,149.35,36.22h0a24.76,24.76,0,0,0-42.7,0L19.2,188.09a23.51,23.51,0,0,0,0,23.72A24.35,24.35,0,0,0,40.55,224h174.9a24.35,24.35,0,0,0,21.33-12.19A23.51,23.51,0,0,0,236.8,188.09ZM222.93,203.8a8.5,8.5,0,0,1-7.48,4.2H40.55a8.5,8.5,0,0,1-7.48-4.2,7.59,7.59,0,0,1,0-7.72L120.52,44.21a8.75,8.75,0,0,1,15,0l87.45,151.87A7.59,7.59,0,0,1,222.93,203.8ZM120,144V104a8,8,0,0,1,16,0v40a8,8,0,0,1-16,0Zm20,36a12,12,0,1,1-12-12A12,12,0,0,1,140,180Z\"/></svg>"};/* OV_VISUAL_END */
  function visualIcon(name){const svg=new DOMParser().parseFromString(orbitalIcons[name],'image/svg+xml').documentElement;const node=document.importNode(svg,true);node.setAttribute('class','orbital-icon');return node;}
  function add(video) {
    const host = document.createElement("div");
    host.setAttribute("data-syphon-video-button", "");
    const root = host.attachShadow({ mode: "closed" });
    const style=document.createElement("style");style.textContent=identityCss+css;root.append(style);
    const shell=document.createElement("div");shell.className="video-example v18";root.append(shell);
    const trigger = document.createElement("button"); trigger.className = "trigger video-launcher"; trigger.type = "button";
    const triggerMark = document.createElement("img"); triggerMark.className = "trigger-mark"; triggerMark.alt = ""; triggerMark.setAttribute("aria-hidden", "true"); triggerMark.draggable = false;
    if (chrome.runtime.getURL) triggerMark.src = chrome.runtime.getURL("brand/icon-64.png");
    const triggerLabel = document.createElement("span"); triggerLabel.textContent = "Syphon this"; const launchCopy=document.createElement("span"), launchHint=document.createElement("small");launchHint.textContent="Download from this video";launchCopy.append(triggerLabel,launchHint);trigger.append(triggerMark,launchCopy);
    const dismiss = document.createElement("button"); dismiss.className = "dismiss"; dismiss.append(visualIcon("x"));
    dismiss.setAttribute("aria-label", "Hide Syphon for this video"); dismiss.title = "Hide for this video";
    const toolbar = document.createElement("div"); toolbar.className = "toolbar"; toolbar.append(trigger, dismiss);
    trigger.setAttribute("aria-expanded", "false"); trigger.setAttribute("aria-controls", "syphon-options");
    const panel = document.createElement("section"); panel.className = "panel video-surface"; panel.id = "syphon-options"; panel.hidden = true;
    panel.setAttribute("aria-label", "Syphon video downloads");
    const options = document.createElement("div"), status = document.createElement("p");
    status.setAttribute("role", "status"); status.setAttribute("aria-live", "polite");
    const confirm=document.createElement("button");confirm.className="confirm";confirm.append(visualIcon("download"),"Syphon this");confirm.disabled=true;let selection=null;confirm.onclick=()=>{if(selection)send(selection.source,selection.format);};const footer=document.createElement("footer");footer.className="media-footer";const refresh=document.createElement("button");refresh.className="text-button";refresh.append(visualIcon("refresh"),"Refresh sources");refresh.onclick=()=>discover(true);footer.append(refresh,confirm);status.className="media-status";panel.append(options,status,footer); shell.append(toolbar,panel); document.documentElement.append(host);
    let subtitlesExpanded = false, mediaTab="video", resolvedTitle="";
    let busy = false, inspectionRun, discovered = [], interaction = 0, dismissed = false, automaticIdentity, sourceTimer, sourcesPending = false;
    const knownMedia = new Map();
    const sourceIdentity = value => SyphonMedia.resolve(value, video.baseURI) || value || "";
    const identityNow = () => {
      const assigned = sourceIdentity(video.getAttribute("src"));
      const sources = [...video.querySelectorAll("source[src]")].map(s => sourceIdentity(s.getAttribute("src")));
      const loaded = sourceIdentity(video.currentSrc);
      // Initial loading of a declared URL is not navigation; a conflicting loaded URL is.
      const extraLoaded = loaded && loaded !== assigned && !sources.includes(loaded) ? loaded : "";
      return `${video.baseURI}\n${assigned}\n${sources.join("\n")}\n${extraLoaded}`;
    };
    let identity = identityNow(), intent = false, gestureUntil = 0;
    dismissed = dismissedVideos.has(identity);
    function syncIdentity() {
      const next = identityNow();
      if (next === identity) return;
      intent=false;gestureUntil=0;claimRetry=0;activeVideo=activeVideo===video?null:activeVideo;
      resolvedTitle=""; identity = next; dismissed = dismissedVideos.has(identity); discovered = []; inspectionRun = null; automaticIdentity = undefined;
      selection=null;knownMedia.clear();
      sourcesPending = false; clearTimeout(sourceTimer); interaction++; closePanel(); status.textContent = "";
    }
    function closePanel() { mediaTab="video";subtitlesExpanded = false;const captions=options.querySelector(".caption-group");if(captions)captions.open=false;panel.hidden = true; trigger.hidden = false; trigger.setAttribute("aria-expanded", "false");dismiss.setAttribute("aria-label", "Hide Syphon for this video");dismiss.title = "Hide for this video"; host.style.width = "168px"; shell.classList.remove("open-up"); }
    function videoTitle(){const inspected=[...knownMedia.values()].map(r=>r.media?.title);return SyphonMedia.readableTitle([...inspected,resolvedTitle,...SyphonMedia.titleCandidates(video)])||'Video from '+location.hostname;}
    async function resolveTitle(){const requestedIdentity=identity;try{const reply=await chrome.runtime.sendMessage({type:'syphon-video-title',candidates:SyphonMedia.titleCandidates(video)});if(!stopped&&identityNow()===requestedIdentity&&typeof reply?.title==='string'){resolvedTitle=reply.title.slice(0,512);if(!panel.hidden)render();}}catch{}}
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
        const result = encryptedReply(fresh) ? fresh : fresh?.media ? fresh : knownMedia.get(source.url) || fresh;
        if (encryptedReply(result)) { model.protected=true;return [{...source,supported:false,note:encryptedMessage}]; }
        if (result?.media?.qualities?.length) return [...((source.youtube||source.page) ? [{...source,label:'Best available',youtubeFormat:undefined,estimatedBytes:undefined}] : []), ...SyphonMedia.qualityChoices(result.media.qualities).map(choice => ({ ...source,
          label: SyphonMedia.qualityLabel(SyphonMedia.qualityTier(choice)) + (source.label === "Page stream" ? " · another video on this page" : ""),
          format: "mp4", videoStream: choice.youtubeFormat || choice.resolveAtDownload ? undefined : choice.videoStream, requestedHeight: choice.resolveAtDownload ? SyphonMedia.qualityTier(choice) : undefined, audioStream: choice.youtubeFormat ? undefined : choice.audioStream,
          youtubeFormat: choice.youtubeFormat, qualityHeight: SyphonMedia.qualityTier(choice), estimatedBytes: choice.estimatedBytes, title: result.media.title, note: "" }))];
        if (result && !result.media && (source.manifest || source.youtube||source.page)) return [{ ...source, supported: !!(source.youtube||source.page), note: (source.youtube||source.page) ? "Qualities unavailable — automatic selection remains available" : result.message || "Could not inspect stream" }];
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
          title: source.kind === "video" && source.label.indexOf("another video on this page") < 0 ? SyphonMedia.readableTitle([source.title,videoTitle()]).slice(0, 512) || undefined : undefined,
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
        const result = {sources:[]}; // Page-wide observations do not establish player ownership.
        if (!current()) return;
        discovered = (result?.sources ?? []).filter(s => SyphonMedia.resolve(s.url, video.baseURI)).slice(-12);
        render();
        if (!SyphonMedia.collect(video).protected) {
          const sources = modelNow().options.filter(s => s.kind === "video" && !inspectionFor(s.url)?.reply?.media && (includeDirect === true || s.youtube || s.page || s.manifest || /unspecified|Page stream/.test(s.label)));
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
      if (!intent && gestureUntil && Date.now() <= gestureUntil && !video.paused && !video.ended) {
        gestureUntil = 0;
        claimPlayback();
        return;
      }
      if (stopped || !intent || activeVideo !== video || dismissed || !video.isConnected || automaticIdentity === identity) return;
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
      const context=document.createElement("div");context.className="video-context";const thumb=document.createElement("div");thumb.className="video-thumb";thumb.append(triggerMark.cloneNode());const copy=document.createElement("div"),title=document.createElement("h1"),hint=document.createElement("p");title.textContent=videoTitle();hint.textContent="Choose what you want to keep.";copy.append(title,hint);context.append(thumb,copy);options.append(context);
      const tabs=document.createElement("div");tabs.className="media-tabs";tabs.setAttribute("role","tablist");tabs.setAttribute("aria-label","Media type");const panels={};
      for(const [kind,label]of [["video","Video"],["audio","Audio"],["subtitle","Captions"]]){const button=document.createElement("button");button.append(visualIcon(kind==="video"?"video":kind==="audio"?"music":"file-text"),label);button.className=kind===mediaTab?"current":"";button.setAttribute("role","tab");button.setAttribute("aria-selected",String(kind===mediaTab));button.tabIndex=kind===mediaTab?0:-1;button.onclick=()=>{mediaTab=kind;render();};button.onkeydown=event=>{const order=["video","audio","subtitle"],index=order.indexOf(mediaTab);if(["ArrowLeft","ArrowRight","Home","End"].includes(event.key)){event.preventDefault();mediaTab=event.key==="Home"?order[0]:event.key==="End"?order[2]:order[(index+(event.key==="ArrowLeft"?-1:1)+3)%3];render();options.querySelector('[role="tab"][aria-selected="true"]')?.focus();}};tabs.append(button);const content=document.createElement("div");content.className="media-tab-content";content.hidden=mediaTab!==kind;panels[kind]=content;}
      options.append(tabs,panels.video,panels.subtitle,panels.audio);
      for (const [kind, title] of [["video", "Video quality"], ["subtitle", "Subtitles & closed captions"]]) {
        const sources = model.options.filter(option => option.kind === kind);
        let container = panels[kind];
        if (kind === "subtitle") {
          const group = document.createElement("details"); group.className = "caption-group"; group.open = subtitlesExpanded;
          const summary = document.createElement("summary");summary.textContent = title;
          const count = document.createElement("span");count.className = "caption-count";count.textContent = String(sources.length);summary.append(count);group.append(summary);
          const tracks = document.createElement("div");tracks.className = "caption-options";tracks.setAttribute("aria-label", "Subtitle and closed-caption tracks");group.append(tracks);
          if(selection?.source.kind === "subtitle"){const selected=document.createElement("p");selected.className="caption-selection";selected.textContent=selection.source.label;group.insertBefore(selected,tracks);}
          panels.subtitle.append(group);container = tracks;
        } else { const heading = document.createElement("h2"); heading.textContent = title; panels.video.append(heading); }
        if (!sources.length) { const note = document.createElement("p"); note.textContent = kind === "video" ? "No direct video file exposed by this player." : "No subtitle or closed-caption tracks detected."; container.append(note); }
        for (const source of sources) {
          const button = document.createElement("button"); button.className = "option quality-choice";
          const preferred = kind === "video" && source.qualityHeight === preferredQuality && validPreference(preferredQuality);
          const copy=document.createElement("span"),name=document.createElement("strong"),note=document.createElement("small"),size=document.createElement("em");name.textContent=source.label+(preferred?" · Preferred":"");note.textContent=source.note?" — "+source.note:"";size.textContent=SyphonMedia.sizeLabel(source.estimatedBytes)?" · "+SyphonMedia.sizeLabel(source.estimatedBytes):"";copy.append(name,note);button.append(copy,size);
          button.setAttribute("aria-pressed", String(selection ? selection.source.url===source.url && selection.source.label===source.label && !selection.format : false));
          button.disabled = busy || !source.supported || (kind === "video" && model.protected);
          if(source.qualityHeight>=2160||source.qualityHeight<=144)button.classList.add("edge");button.addEventListener("click", () => {selection={source};status.textContent="";render();}); container.append(button);
        }
      }
      if (validPreference(preferredQuality) && model.options.some(s => s.qualityHeight) && !model.options.some(s => s.qualityHeight === preferredQuality)) panels.video.append(Object.assign(document.createElement('p'),{textContent:`Your preferred quality (${SyphonMedia.qualityLabel(preferredQuality)}) isn't available.`}));
      const audioSource = model.options.find(source => source.kind === "video" && source.supported);
      const audioHeading = document.createElement("h2"); audioHeading.textContent = "MP3 audio"; panels.audio.append(audioHeading);
      const mp3 = document.createElement("button"); mp3.className = "option quality-choice"; mp3.textContent = "Convert audio to MP3"; mp3.disabled = busy || model.protected || !audioSource;
      mp3.setAttribute("aria-pressed",String(selection?.format==="mp3"));mp3.addEventListener("click", () => {selection={source:audioSource,format:"mp3"};status.textContent="";render();}); panels.audio.append(mp3);
      if (model.protected) {
        const note = document.createElement("p"); note.textContent = encryptedMessage; panels.video.append(note);
      }
      const available=selection&&model.options.some(s=>s.url===selection.source.url&&s.label===selection.source.label&&s.supported);confirm.disabled=busy||model.protected||!available;confirm.textContent=busy?"Sending…":"Continue in Syphon";
    }
    trigger.addEventListener("pointerenter", discover);
    trigger.addEventListener("focus", discover);
    trigger.addEventListener("click", async () => { syncIdentity(); if(!intent||activeVideo!==video)await claimPlayback(false);if (!intent || activeVideo !== video) return; if (!panel.hidden) { closePanel(); return; } panel.hidden = false; trigger.setAttribute("aria-expanded", "true");dismiss.setAttribute("aria-label", "Close download options");dismiss.title = "Close download options";position();render();resolveTitle();discover(); });
    dismiss.addEventListener("click", () => {
      if (!panel.hidden) { closePanel(); trigger.focus(); return; }
      dismissedVideos.add(identity);
      if (dismissedVideos.size > 32) dismissedVideos.delete(dismissedVideos.values().next().value);
      dismissed = true; interaction++; closePanel(); host.hidden = true; position();
    });
    const onMetadata = () => { syncIdentity(); scan(); if (!panel.hidden) render(); if (automaticIdentity === identity) onSources(); };
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
      const visible = frameVisible() && rect.width >= 160 && rect.height >= 90 && rect.bottom > 0 && rect.top < innerHeight && rect.right > 0 && rect.left < innerWidth;
      host.hidden = !intent || activeVideo !== video || dismissed || !visible;
      // Keep the toolbar eight pixels inside the video, independent of panel width.
      host.style.right = `${Math.max(8, innerWidth - rect.right + 8)}px`;
      const width = `${Math.max(140, Math.min(panel.hidden ? 168 : 280, rect.right - 16, innerWidth - 24))}px`; host.style.width = width; panel.style.maxWidth = width;
      host.style.top = `${Math.max(8, Math.min(rect.top + 8, innerHeight - 48))}px`;
      const top = parseFloat(host.style.top), below = innerHeight - top - 48, above = top - 12;
      const openUp = below < 320 && above > below;
      panel.style.top = openUp ? "auto" : "100%"; panel.style.bottom = openUp ? "100%" : "auto";
      shell.classList.toggle("open-up", !panel.hidden && openUp);
      panel.style.marginTop = "0"; panel.style.marginBottom = "0";
      panel.style.maxHeight = `${Math.max(100, Math.min(420, openUp ? above : below))}px`;
    }
    function gesture(event) {
      syncIdentity();
      if (!SyphonMedia.playbackGesture(event, video)) return;
      // Only a paused player's recent gesture may authorize its subsequent playback transition.
      // Gestures on an already autoplaying player cannot confer intent through polling.
      gestureUntil = !intent && video.paused ? Date.now() + 2000 : 0;
    }
    async function claimPlayback(automatic=false) {
      if(claimPending||!frameVisible())return;
      // Playback has now started from the recent, player-bound gesture.
      intent = false; activeVideo = video;
      if(!automatic)selectedUntil=Date.now()+15000;
      for (const [other, controller] of controllers) if (other !== video) controller.revoke();
      claimId = crypto.randomUUID();
      claimPending = true;
      const requestedClaim = claimId, requestedIdentity = identity;
      let reply;
      try { reply = await chrome.runtime.sendMessage({type:"syphon-video-claim", claimId, automatic, score:playerScore(video)}); } catch { if (claimId === requestedClaim) claimPending = false; return; }
      if (claimId === requestedClaim) claimPending = false;
      if (stopped || reply?.ok !== true || claimId !== requestedClaim || identityNow() !== requestedIdentity || activeVideo !== video) return;
      intent = true;
      position();
      if (!video.paused && !video.ended) onPlayback();
    }
    document.addEventListener("pointerdown", gesture, true);
    document.addEventListener("keydown", gesture, true);
    controllers.set(video, { host, isPrimary:()=>intent&&activeVideo===video, activate:()=>claimPlayback(false), ensureMain(){if(mainCandidate===video&&!intent&&!claimPending&&Date.now()>claimRetry){claimRetry=Date.now()+2000;claimPlayback(true);}}, revoke() { intent = false; closePanel(); host.hidden = true; }, position, render, closePanel, onSources, cleanup() {
      document.removeEventListener("pointerdown", gesture, true); document.removeEventListener("keydown", gesture, true);
      clearTimeout(sourceTimer); video.textTracks?.removeEventListener?.("addtrack", onMetadata); video.textTracks?.removeEventListener?.("removetrack", onMetadata); video.removeEventListener("play", onPlayback); video.removeEventListener("playing", onPlayback);
      video.removeEventListener("loadedmetadata", onMetadata); video.removeEventListener("resize", onMetadata); host.remove();
    } }); position();
  }
  let catalogHost=null,catalogRoot=null,catalogRows=[],catalogSources=[],catalogAt=0,catalogBusy=false,catalogOpen=false;
  let catalogPosition=null,catalogDrag=null;
  function fitCatalog(){
    if(!catalogHost)return;
    const width=catalogOpen?Math.min(344,innerWidth-16):150;
    const main=controllers.get(activeVideo)?.host?.getBoundingClientRect();
    const defaultTop=main?.width?main.top:SyphonMedia.youtubeUrl(location.href)?76:64;
    const defaultLeft=main?.width?main.left-width-8:innerWidth-width-16;
    const x=Math.max(8,Math.min(innerWidth-width-8,catalogPosition?.x??defaultLeft));
    const y=Math.max(8,Math.min(innerHeight-60,catalogPosition?.y??defaultTop));
    catalogHost.style.left=x+'px';catalogHost.style.top=y+'px';catalogHost.style.right='auto';
    const list=catalogRoot?.querySelector('section');if(list)list.style.maxHeight=Math.max(80,innerHeight-y-58)+'px';
  }
  function installCatalogDrag(handle){
    handle.onpointerdown=e=>{if(e.button!==0)return;e.preventDefault();const rect=catalogHost.getBoundingClientRect();catalogDrag={x:e.clientX,y:e.clientY,left:rect.left,top:rect.top,id:e.pointerId};handle.setPointerCapture?.(e.pointerId);};
    handle.onpointermove=e=>{if(!catalogDrag)return;catalogPosition={x:catalogDrag.left+e.clientX-catalogDrag.x,y:catalogDrag.top+e.clientY-catalogDrag.y};fitCatalog();};
    const finish=()=>{catalogDrag=null;};handle.onpointerup=finish;handle.onpointercancel=finish;handle.onlostpointercapture=finish;
    handle.onkeydown=e=>{if(!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key))return;e.preventDefault();const rect=catalogHost.getBoundingClientRect();catalogPosition={x:rect.left+({ArrowLeft:-20,ArrowRight:20}[e.key]||0),y:rect.top+({ArrowUp:-20,ArrowDown:20}[e.key]||0)};fitCatalog();};
  }
  function renderCatalog(){
    if(!catalogRoot)return;
    const button=catalogRoot.querySelector('.catalog-trigger'),list=catalogRoot.querySelector('section');
    const other=catalogRows.filter(p=>!p.isMain&&p.id!==playerIds.get(activeVideo));catalogHost.hidden=!!document.fullscreenElement||(!other.length&&!catalogSources.length);button.textContent=`Videos · ${other.length}`;list.hidden=!catalogOpen;button.setAttribute("aria-expanded",String(catalogOpen));fitCatalog();list.replaceChildren();
    const heading=document.createElement('strong');heading.textContent='Other videos on this tab';list.append(heading);
    for(const row of other){const item=document.createElement('div');item.style.cssText='padding:12px 0;border-bottom:1px solid #302639';
      const title=document.createElement('div');title.textContent=row.title||'Video';
      const time=document.createElement('p');time.textContent=SyphonMedia.durationLabel(row.duration)||'Duration unknown';
      const choose=document.createElement('button');choose.textContent='Show video';choose.onclick=()=>chrome.runtime.sendMessage({type:'syphon-player-focus',id:row.id});
      const download=document.createElement('button');download.textContent='Review download';download.disabled=!row.sourceUrls?.length;
      download.onclick=async()=>{download.disabled=true;const reply=await chrome.runtime.sendMessage({type:'syphon-player-download',id:row.id,url:row.sourceUrls[0]});time.textContent=reply?.message||'Open Syphon to review';download.disabled=false;};
      item.append(title,time,choose,download);list.append(item);
    }
    if(!other.length){const p=document.createElement('p');p.textContent='No other players detected.';list.append(p);}
    if(catalogSources.length){const heading=document.createElement('p');heading.textContent='Other detected streams · may be previews or duplicate qualities';list.append(heading);
      for(const source of catalogSources.slice(-12)){
        const row=document.createElement('div'),label=document.createElement('p'),review=document.createElement('button');
        const entry=inspectionFor(source.url),checked=entry&&!entry.pending,duration=entry?.reply?.media?.durationSeconds??entry?.reply?.media?.duration;
        label.textContent=(SyphonMedia.durationLabel(duration)||'Duration unknown')+' \u00b7 '+new URL(source.url).hostname;
        review.disabled=!!entry?.pending;review.textContent=entry?.pending?'Checking duration...':checked?'Download this source':'Check duration and review';
        review.onclick=checked?()=>chrome.runtime.sendMessage({type:'syphon-media-download',url:source.url,showOptions:true,format:source.manifest?'mp4':undefined}):async()=>{const pending=inspect(source.url);renderCatalog();await pending;const completed=inspections.get(source.url);if(completed&&!completed.pending)completed.expires=Math.max(completed.expires,Date.now()+60000);if(!stopped)renderCatalog();};
        row.append(label,review);list.append(row);
      }
    }
  }
  async function refreshCatalog(){
    if(catalogBusy||Date.now()-catalogAt<1500||stopped)return;catalogAt=Date.now();catalogBusy=true;
    const players=[...document.querySelectorAll('video')].slice(0,60).map(video=>{if(!playerIds.has(video))playerIds.set(video,crypto.randomUUID());const m=SyphonMedia.collect(video);return {id:playerIds.get(video),title:SyphonMedia.readableTitle([m.title,...SyphonMedia.titleCandidates(video)])||'Video',duration:m.duration,sourceUrls:m.protected?[]:m.options.filter(o=>o.kind==='video'&&o.supported).map(o=>o.url),score:playerScore(video),isMain:onScreen(video)&&(controllers.get(video)?.isPrimary()||false)};});
    try{const result=await chrome.runtime.sendMessage({type:'syphon-player-catalog',players});if(stopped||!result?.players)return;catalogRows=result.players;const known=new Set(catalogRows.flatMap(p=>p.sourceUrls||[]));catalogSources=(result.sources||[]).filter(s=>!known.has(s.url));
      if(window.top!==window)return;
      if(!catalogHost){catalogHost=document.createElement('div');catalogHost.setAttribute('data-syphon-page-videos','');catalogRoot=catalogHost.attachShadow({mode:'closed'});const style=document.createElement('style');style.textContent=identityCss+css+':host{top:76px;right:16px}.catalog-bar{display:flex;align-items:center;justify-content:flex-end;gap:3px}.move-handle{cursor:grab;touch-action:none;padding:8px 6px;color:#b2afbf}.move-handle:active{cursor:grabbing}section{margin-top:8px;width:320px;max-height:65vh;overflow:auto;padding:16px;background:#0b0811;border:1px solid #453456;border-radius:12px}p{color:#b2afbf}button{margin:3px}';const trigger=document.createElement('button');trigger.className='catalog-trigger';trigger.textContent='Videos';trigger.onclick=()=>{catalogOpen=!catalogOpen;renderCatalog();};const list=document.createElement('section');list.className='catalog-list';list.hidden=true;const bar=document.createElement('div');bar.className='catalog-bar';const move=document.createElement('button');move.className='move-handle';move.textContent='⠿';move.setAttribute('aria-label','Move Videos control');move.title='Drag to move · arrow keys also move';installCatalogDrag(move);bar.append(move,trigger);catalogRoot.append(style,bar,list);document.documentElement.append(catalogHost);fitCatalog();}
      catalogHost.hidden=!!document.fullscreenElement;renderCatalog();
    }catch{}finally{catalogBusy=false;}
  }
  function playerScore(video){const r=video.getBoundingClientRect();return (video.closest("#movie_player")?10000000:0)+r.width*r.height;}
  function elementVisible(element,view){
    const r=element.getBoundingClientRect();if(r.width<=0||r.height<=0||r.bottom<=0||r.top>=view.innerHeight||r.right<=0||r.left>=view.innerWidth)return false;
    for(let node=element;node?.nodeType===1;node=node.parentElement){const style=view.getComputedStyle(node);if(style.display==='none'||style.visibility==='hidden'||style.visibility==='collapse'||style.opacity==='0')return false;}return true;
  }
  let parentVisibility={visible:false,time:0},visibilityNonce=crypto.randomUUID();
  function frameVisible(){
    let view=window;
    try{while(view!==view.top){const frame=view.frameElement;if(!frame)return parentVisibility.visible&&Date.now()-parentVisibility.time<5000;if(!elementVisible(frame,view.parent))return false;view=view.parent;}return true;}
    catch{return parentVisibility.visible&&Date.now()-parentVisibility.time<5000;}
  }
  function askParentVisibility(){if(window.parent!==window)window.parent.postMessage({type:'syphon-frame-visibility-query',nonce:visibilityNonce},'*');}
  function visibilityMessage(event){
    const data=event.data;if(!data||typeof data.nonce!=='string'||data.nonce.length>64)return;
    if(data.type==='syphon-frame-visibility-query'){
      const frame=[...document.querySelectorAll('iframe,frame')].slice(0,256).find(f=>f.contentWindow===event.source);
      if(frame)event.source.postMessage({type:'syphon-frame-visibility-reply',nonce:data.nonce,visible:frameVisible()&&elementVisible(frame,window)},'*');
    }else if(data.type==='syphon-frame-visibility-reply'&&event.source===window.parent&&data.nonce===visibilityNonce){parentVisibility={visible:data.visible===true,time:Date.now()};}
  }
  window.addEventListener('message',visibilityMessage);
  function onScreen(video) {
    const rect = video.getBoundingClientRect();
    return frameVisible() && elementVisible(video,window) && rect.width >= 160 && rect.height >= 90 && rect.bottom > 0 && rect.top < innerHeight && rect.right > 0 && rect.left < innerWidth;
  }
  function scan() {
    if (stopped) return;
    askParentVisibility();
    for (const [video, controller] of controllers) {
      if (!video.isConnected || video !== activeVideo && !onScreen(video)) { controller.cleanup(); controllers.delete(video); }
    }
    for (const video of document.querySelectorAll("video")) {
      if (controllers.size >= 20) break;
      if (!controllers.has(video) && onScreen(video)) add(video);
    }
    position();
  }
  function position() {
    mainCandidate=!frameVisible()?null:activeVideo&&onScreen(activeVideo)&&controllers.has(activeVideo)&&Date.now()<selectedUntil?activeVideo:SyphonMedia.mainPlayer([...controllers.keys()]);
    if(mainCandidate&&activeVideo!==mainCandidate){for(const [v,c]of controllers)if(v!==mainCandidate)c.revoke();}
    for (const controller of controllers.values()) controller.position();
    controllers.get(mainCandidate)?.ensureMain();
    refreshCatalog();
  }
  function schedule() { if (!scanTimer && !stopped) scanTimer = setTimeout(() => { scanTimer = null; scan(); }, 150); }
  const observer = new MutationObserver(schedule);
  observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ["src", "label", "data-quality", "res", "title", "data-preview"] });
  const interval = setInterval(()=>{position();schedule();}, 1000);
  function onViewport() { fitCatalog(); position(); schedule(); }
  addEventListener("scroll", onViewport, true); addEventListener("resize", onViewport);
  document.addEventListener("fullscreenchange", position);
  const catalogEscape=e=>{if(e.key==='Escape'&&catalogOpen){catalogOpen=false;renderCatalog();catalogRoot?.querySelector('.catalog-trigger')?.focus();}};
  document.addEventListener("keydown",catalogEscape);
  function onOutside(event) {
    const path = event.composedPath();
    if(catalogOpen&&catalogHost&&!path.includes(catalogHost)){catalogOpen=false;renderCatalog();}
    if(![...controllers.values()].some(c=>path.includes(c.host))&&!path.includes(catalogHost)&&event.isTrusted&&event.type==='pointerdown')chrome.runtime.sendMessage({type:'syphon-overlay-outside'}).catch(()=>{});
    for (const controller of controllers.values()) if (!path.includes(controller.host)) controller.closePanel();
  }
  document.addEventListener("pointerdown", onOutside, true);
  document.addEventListener("click", onOutside, true);
  function stop() {
    stopped = true; observer.disconnect(); clearInterval(interval); clearTimeout(scanTimer);
    removeEventListener("scroll", onViewport, true); removeEventListener("resize", onViewport);
    document.removeEventListener("fullscreenchange", position);
    document.removeEventListener("keydown",catalogEscape);
    document.removeEventListener("pointerdown", onOutside, true); document.removeEventListener("click", onOutside, true);
    window.removeEventListener('message',visibilityMessage);
    inspections.clear(); inspectionQueue.length = 0; dismissedVideos.clear();
    catalogHost?.remove();
    chrome.runtime.onMessage.removeListener(onMessage); chrome.storage.onChanged.removeListener(onChanged);
    for (const controller of controllers.values()) controller.cleanup(); controllers.clear();
    delete globalThis.syphonVideoController;
  }
  function onMessage(message, sender, respond) {
    if(message?.type==='syphon-player-focus-target'){
      const video=[...document.querySelectorAll('video')].find(v=>playerIds.get(v)===message.id);
      if(video&&!controllers.has(video)){const disposable=[...controllers].find(([v])=>v!==activeVideo);if(controllers.size>=20&&disposable){disposable[1].cleanup();controllers.delete(disposable[0]);}add(video);}
      if(video){mainCandidate=video;controllers.get(video).activate();video.scrollIntoView?.({block:'center',behavior:'smooth'});}respond?.({ok:!!video});return;
    }
    if(message?.type==='syphon-close-options'){for(const c of controllers.values())c.closePanel();if(catalogOpen){catalogOpen=false;renderCatalog();}}
    if(message?.type==='syphon-media-found')for(const c of controllers.values())c.onSources();
    if (message?.type === "syphon-detection-off") stop();
    if (message?.type === "syphon-video-owner") {
      if (message.claimId !== claimId) { if (!claimPending) activeVideo = null; for (const controller of controllers.values()) controller.revoke(); }
      respond?.({ok:true});
    }

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
  globalThis.syphonVideoController = { stop, version: controllerVersion };
  chrome.storage.local.get(["detectVideos", "preferredVideoQuality"]).then(({ detectVideos, preferredVideoQuality }) => {
    preferredQuality = validPreference(preferredVideoQuality) ? preferredVideoQuality : undefined;
    if (detectVideos) scan(); else stop();
  }).catch(stop);
})();
