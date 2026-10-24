using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Syphon.Core;
using Syphon.Engine;

namespace Syphon.Desktop;

public sealed partial class FloatingDownloadWindow
{
    private const string FloatOrigin = "https://syphon-float.invalid";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private IWebView2?                 _view;
    private CoreWebView2Environment?   _env;
    private bool                       _htmlReady;
    private bool                       _htmlDisposed;
    private bool                       _htmlInitializing;
    private StoredDownload[]           _htmlTasks = [];
    private bool                       _actionPending;
    private string?                    _actionError;
    private bool                       _restoreTaskbarVisibility;

    /// <summary>Push the current model to the HTML layer (no-op if not ready).</summary>
    private void PublishHtml()
    {
        if (!_htmlReady || _htmlDisposed || _view?.CoreWebView2 is not { } core) return;
        var payload = new
        {
            type  = "model",
            model = new
            {
                kind    = "floating",
                typographyPreset = TypographyComparison.Current,
                actionPending = _actionPending,
                error = _actionError,
                task    = current is null ? null : MainWindow.TaskModel(current),
                tasks = _htmlTasks.Select(MainWindow.TaskModel).ToArray(),
                title = Title,
                samples = speeds.Append(current?.State == DownloadState.Downloading ? Math.Max(0, current.BytesPerSecond) : 0).ToArray(),
                sampleTimes = speedTimes.Append(DateTimeOffset.UtcNow).ToArray(),
                next    = _htmlTasks
                    .Where(t => t.Id != current?.Id &&
                                t.State is DownloadState.Queued or DownloadState.Paused)
                    .Select(MainWindow.TaskModel)
                    .FirstOrDefault(),
            },
        };
        core.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOpts));
    }

    private void InstallHtml(Action openManager, Action<Guid> dismissed)
    {
        TypographyComparison.Changed += PublishHtml;
        const double scale = LocalHtmlSurface.InterfaceScale;
        Width  = 480;
        Height = Math.Min(700, SystemParameters.WorkArea.Height - 48);
        Top = SystemParameters.WorkArea.Bottom - Height - 24;
        Left = SystemParameters.WorkArea.Right - Width - 24;
        SizeToContent = SizeToContent.Manual;
        StateChanged += (_, _) => {
            if (WindowState != WindowState.Minimized && _restoreTaskbarVisibility) {
                _restoreTaskbarVisibility = false;
                ShowInTaskbar = false;
            }
        };

        // Keep native fallback hidden in a host grid; WebView2 sits on top.
        var fallback = (FrameworkElement)Content;
        fallback.Visibility = Visibility.Collapsed;
        Content = null;
        var host = new Grid();
        host.Children.Add(fallback);

        // Match the manager's opaque native input surface. No WPF overlay is
        // placed above the direct browser host.
        _view = AllowsTransparency ? new WebView2CompositionControl() : new WebView2();
        _view.DefaultBackgroundColor = System.Drawing.Color.FromArgb(10, 5, 15);
        host.Children.Add((UIElement)_view);
        Content = host;

        Closed += (_, _) =>
        {
            _htmlDisposed = true;
            TypographyComparison.Changed -= PublishHtml;
            if (_view is IDisposable disposable) disposable.Dispose();
        };

        ((FrameworkElement)_view).Loaded += async (_, _) => await InitBrowser(scale, openManager, dismissed);
    }

    private async Task InitBrowser(double scale, Action openManager, Action<Guid> dismissed)
    {
        // Loaded can recur when the floating window is hidden and shown.
        // Install each IPC/resource handler only once for this browser lifetime.
        if (_htmlInitializing || _htmlDisposed) return;
        _htmlInitializing = true;
        try
        {
            var profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Syphon", "FloatProfile");
            var opts = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--force-color-profile=srgb",
            };
            _env = await CoreWebView2Environment.CreateAsync(null, profile, opts);
            if (_htmlDisposed) return;

            await _view!.EnsureCoreWebView2Async(_env);
            if (_htmlDisposed) return;

            var core = _view.CoreWebView2;
            core.Settings.AreDevToolsEnabled              = false;
            core.Settings.AreDefaultContextMenusEnabled   = false;
            core.Settings.IsStatusBarEnabled              = false;
            core.Settings.IsZoomControlEnabled            = false;
            core.Settings.AreHostObjectsAllowed           = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            // Let WebView2/Windows own caption hit-testing and the move loop.
            // DragMove from WebMessageReceived enters a nested message loop
            // inside a COM callback (unsupported WebView2 reentrancy).
            core.Settings.IsNonClientRegionSupportEnabled = !AllowsTransparency;
            _view.ZoomFactor = scale;

            // Serve the neuro HTML from memory — no disk writes needed.
            var fonts = string.Join("\n", new[] { 400, 500, 600 }.Select(weight =>
                "@font-face{font-family:Inter;font-display:swap;font-weight:" + weight + ";src:url(data:font/woff2;base64," +
                Convert.ToBase64String(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                    "DesktopAssets", $"inter-{weight}.woff2"))) + ") format('woff2')}"));
            var brandFolder = Path.Combine(AppContext.BaseDirectory, "DesktopAssets", "brand");
            var htmlBytes = Encoding.UTF8.GetBytes(BuildHtml()
                .Replace("__SYPHON_EMBLEM__", Convert.ToBase64String(File.ReadAllBytes(Path.Combine(brandFolder, "emblem.png"))))
                .Replace("__SYPHON_WORDMARK__", Convert.ToBase64String(File.ReadAllBytes(Path.Combine(brandFolder, "wordmark.png"))))
                .Replace("/* LOCAL_FONTS */", fonts + "\n" + TypographyComparison.InlineCss())
                .Replace("/* SHARED_THEME */", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DesktopAssets", "theme-tokens.css")) + "\n" + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DesktopAssets", "theme.css"))));
            core.AddWebResourceRequestedFilter(FloatOrigin + "/", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                e.Response = e.Request.Uri == FloatOrigin + "/"
                    ? _env.CreateWebResourceResponse(
                          new MemoryStream(htmlBytes), 200, "OK",
                          "Content-Type: text/html; charset=utf-8")
                    : _env.CreateWebResourceResponse(null, 403, "Blocked", "");
            };

            core.WebMessageReceived += async (_, e) =>
            {
                if (_htmlDisposed || e.Source != FloatOrigin + "/") return;
                try { await HandleMessage(e.WebMessageAsJson, scale, openManager, dismissed); }
                catch (Exception ex) { _actionError = ex.Message; PublishHtml(); }
            };

            core.Navigate(FloatOrigin + "/");
        }
        catch
        {
            // Fall back to the native WPF layer already in the host grid.
            if (Content is Grid g && g.Children.Count > 0)
                ((FrameworkElement)g.Children[0]).Visibility = Visibility.Visible;
            if (_view is UIElement view) view.Visibility = Visibility.Collapsed;
        }
    }

    private async Task HandleMessage(
        string json, double scale, Action openManager, Action<Guid> dismissed)
    {
        using var doc = JsonDocument.Parse(json);
        var msg = doc.RootElement;
        if (!msg.TryGetProperty("action", out var act)) return;

        var command = act.GetString();
        if (command is "pause-all" or "resume-all") { await RunAll(command == "resume-all"); return; }
        if (command is "toggle" or "cancel")
        {
            if (_actionPending || current is null) return;
            var target = msg.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var taskId) ? _htmlTasks.FirstOrDefault(t => t.Id == taskId) : current;
            if (target is null) throw new InvalidOperationException("The displayed download changed. Try the action again.");
            _actionPending = true; _actionError = null; PublishHtml();
            try
            {
                var row = new DownloadRow(target);
                if (command == "toggle" && row.CanResume) _resumeTask!(target.Id);
                else if (row.CanPause) await (command == "cancel" ? _cancelTask!(target.Id) : _pauseTask!(target.Id));
                else throw new InvalidOperationException("This download cannot be changed right now. Check its status in the manager.");
            }
            finally { _actionPending = false; PublishHtml(); }
            return;
        }

        switch (command)
        {
            case "ready":
                _htmlReady = true;
                PublishHtml();
                break;

            case "close":
                foreach (var task in _htmlTasks) dismissed(task.Id);
                Hide();
                break;

            case "manager":
                openManager();
                break;

            case "minimize":
                _restoreTaskbarVisibility = !ShowInTaskbar;
                ShowInTaskbar = true;
                WindowState = WindowState.Minimized;
                break;

            case "collapse":
                expanded = !msg.GetProperty("collapsed").GetBoolean();
                UpdateRows();
                break;

        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Neuro HTML — fully self-contained, no external resources
    // ─────────────────────────────────────────────────────────────────────────
    private static string BuildHtml() => """
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; font-src data:; img-src data:;">
<style>
/* LOCAL_FONTS */
*{box-sizing:border-box}body{margin:0;background:#181221;color:#ece9f2;font:13px Inter,Segoe UI,sans-serif}html,body,.win{height:100%;overflow:hidden}.win{display:flex;flex-direction:column;border:1px solid #403248;border-radius:15px;padding:10px}button{font:inherit;color:inherit;background:#2c223a;border:1px solid #493657;border-radius:8px;padding:7px 10px;cursor:pointer}button:hover{background:#473258}button:disabled{opacity:.4;cursor:default}button,button *{app-region:no-drag}#titlebar{app-region:drag;display:flex;align-items:center;gap:6px;min-height:42px}#title{flex:1;min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-weight:600;padding-left:6px}#toolbar{display:flex;gap:7px;padding:10px 0;border-bottom:1px solid #403248}.rows{overflow:auto;min-height:0;flex:1;margin-top:8px}.rows.compact{flex:0 1 auto;max-height:360px}.row{padding:10px 6px;height:90px;border-bottom:1px solid #403248;display:grid;grid-template-columns:minmax(0,1fr) auto;gap:6px 12px;align-items:center}.filename{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:500}.status{font-size:11px;color:#b9aacd;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.track{height:5px;background:#352740;border-radius:8px;overflow:hidden}.fill{height:100%;background:#c49af5}.stats{font-size:11px;color:#b9aacd;white-space:nowrap}.controls{grid-column:2;grid-row:1/5;display:flex;gap:5px}.controls button{padding:6px 8px}.details{display:none;font-size:11px;color:#b9aacd}.expanded .row{height:132px}.expanded .details{display:block}#error{color:#f6a8ac;font-size:12px;margin:6px}#chart{height:46px;min-height:46px;width:100%;margin-top:auto}.chart-label{font-size:10px;color:#9284aa;margin:7px 0 0}[hidden]{display:none!important}
</style><div class="win"><header id="titlebar"><span id="title">Syphon downloads</span><button id="minBtn" aria-expanded="false">Expand</button><button id="minimizeBtn" aria-label="Minimize">−</button><button id="closeBtn" aria-label="Close">×</button></header>
<div id="toolbar"><button id="pauseAll">Pause all</button><button id="resumeAll">Resume all</button><button id="managerBtn">Manager</button></div><div id="error" role="alert" hidden></div><main class="rows compact" id="rows" aria-label="Downloads"></main><div class="chart-label" id="chartLabel">Combined transfer activity · Last 60s</div><svg id="chart" viewBox="0 0 440 46" preserveAspectRatio="none" aria-label="Transfer speed history"><defs><linearGradient id="speedFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#c49af5" stop-opacity=".35"/><stop offset="1" stop-color="#c49af5" stop-opacity="0"/></linearGradient></defs><path d="M0 10H440M0 25H440M0 44H440" stroke="#403248" stroke-width=".5"/><path id="area" fill="url(#speedFill)"/><path id="line" stroke="#c49af5" stroke-width="2" fill="none"/></svg></div>
<script>
const $=id=>document.getElementById(id), send=m=>window.chrome.webview.postMessage(m);let expanded=false;
function element(tag,cls,text){const e=document.createElement(tag);e.className=cls;if(text!=null)e.textContent=text;return e;}
const rendered=new Map();
function update(model){$('title').textContent=model.title||'Syphon downloads';$('error').hidden=!model.error;$('error').textContent=model.error||'';
const live=new Set();for(const task of model.tasks||[]){live.add(task.id);let r=rendered.get(task.id);if(!r){const root=element('section','row');root.dataset.id=task.id;const name=element('div','filename'),status=element('div','status'),track=element('div','track'),fill=element('div','fill'),stats=element('div','stats'),details=element('div','details'),controls=element('div','controls'),toggle=element('button','','Pause'),cancel=element('button','','Cancel');track.append(fill);track.setAttribute('role','progressbar');controls.append(toggle,cancel);root.append(name,status,track,stats,details,controls);toggle.addEventListener('click',()=>send({action:'toggle',id:task.id}));cancel.addEventListener('click',()=>send({action:'cancel',id:task.id}));r={root,name,status,track,fill,stats,details,toggle,cancel};rendered.set(task.id,r);$('rows').append(root);}r.name.textContent=task.filename;r.name.title=task.filename;r.status.textContent=task.message||task.status||task.state;const pct=task.total>0?Math.min(100,100*task.received/task.total):null;r.fill.style.width=(pct||0)+'%';r.track.setAttribute('aria-valuetext',pct==null?'Size unknown':pct.toFixed(1)+'%');if(pct!=null)r.track.setAttribute('aria-valuenow',pct);else r.track.removeAttribute('aria-valuenow');r.stats.textContent=(pct==null?'Size unknown':pct.toFixed(1)+'%')+' · '+task.speed;r.details.textContent=task.progress+' · ETA '+task.remaining+' · '+task.connections+' connections';r.toggle.textContent=task.canResume?'Resume':'Pause';r.toggle.disabled=model.actionPending||(!task.canResume&&!task.canPause);r.cancel.disabled=model.actionPending||!task.canPause;}
for(const [id,r]of rendered)if(!live.has(id)){r.root.remove();rendered.delete(id);}const a=(model.samples||[]).filter(Number.isFinite),max=Math.max(1,...a)*1.18,times=model.sampleTimes||[],pts=a.map((v,i)=>({x:440-Math.min(60000,Math.max(0,Date.now()-Date.parse(times[i]||new Date())))/60000*440,y:44-42*v/max}));let d='';pts.forEach((p,i)=>{if(!i)d='M'+p.x+','+p.y;else{const prev=pts[i-1],mid=(prev.x+p.x)/2;d+=' C'+mid+','+prev.y+' '+mid+','+p.y+' '+p.x+','+p.y;}});$('line').setAttribute('d',d);$('area').setAttribute('d',pts.length?d+' L'+pts.at(-1).x+',44 L'+pts[0].x+',44 Z':'');$('chartLabel').textContent='Combined transfer activity · '+((a.at(-1)||0)/1048576).toFixed(2)+' MB/s · Last 60s';}
window.chrome.webview.addEventListener('message',e=>{if(e.data.type==='model')update(e.data.model);});$('minBtn').onclick=()=>{expanded=!expanded;$('rows').classList.toggle('compact',!expanded);document.querySelector('.win').classList.toggle('expanded',expanded);$('minBtn').textContent=expanded?'Compact':'Expand';$('minBtn').setAttribute('aria-expanded',expanded);send({action:'collapse',collapsed:!expanded});};$('pauseAll').onclick=()=>send({action:'pause-all'});$('resumeAll').onclick=()=>send({action:'resume-all'});$('managerBtn').onclick=()=>send({action:'manager'});$('closeBtn').onclick=()=>send({action:'close'});$('minimizeBtn').onclick=()=>send({action:'minimize'});send({action:'ready'});
</script></html>
""";
}
