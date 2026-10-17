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
        Height = 510;
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
        if (command is "toggle" or "cancel")
        {
            if (_actionPending || current is null) return;
            if (msg.TryGetProperty("id", out var id) && id.GetString() != current.Id.ToString())
                throw new InvalidOperationException("The displayed download changed. Try the action again.");
            _actionPending = true; _actionError = null; PublishHtml();
            try
            {
                var row = new DownloadRow(current);
                if (command == "toggle" && row.CanResume) _resumeTask!(current.Id);
                else if (row.CanPause) await (command == "cancel" ? _cancelTask!(current.Id) : _pauseTask!(current.Id));
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

            case "layout":
                var h = msg.GetProperty("height").GetDouble();
                if (double.IsFinite(h) && h is >= 40 and <= 1200)
                    Height = Math.Min(h * scale, SystemParameters.WorkArea.Height - 24);
                break;

            case "close":
                if (TaskId is Guid cid) dismissed(cid);
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
                Width = msg.GetProperty("collapsed").GetBoolean() ? 360 : 480;
                break;

        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Neuro HTML — fully self-contained, no external resources
    // ─────────────────────────────────────────────────────────────────────────
    private static string BuildHtml() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <meta http-equiv="Content-Security-Policy"
              content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; font-src data:; img-src data:;">
        <style>
        /* LOCAL_FONTS */
        [hidden] { display: none !important; }
        *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
        :root {
          --bg:         #1a1625;
          --surface:    #1e1a2e;
          --raised:     #231f33;
          --inset:      #161222;
          --ls:         rgba(255,255,255,0.055);
          --ds:         rgba(0,0,0,0.6);
          --accent:     #8350C4;
          --accent-dim: #5e3a90;
          --bright:     #ECE9F2;
          --sub:        #B9AACD;
          --text:       #ECE9F2;
          --muted:      #9284AA;
          --success:    #4a9e6a;
          --warning:    #b87a2a;
          --danger:     #9e4a4a;
          --accent-text: #C49AF5;
          --emphasis:    #F0EAF8;
          --font:       var(--font-ui);
        }
        html, body {
          height: 100%;
          background: transparent;
          font-family: var(--font);
          color: var(--text);
          -webkit-font-smoothing: antialiased;
        }
        body { display: flex; flex-direction: column; border-radius: 16px; overflow: hidden; }
        button { font: inherit; cursor: pointer; }
        button, button * { app-region: no-drag; }
        button:disabled { opacity: 0.4; cursor: default; }

        /* Window shell */
        .win {
          background: var(--surface);
          border-radius: 16px;
          overflow: hidden;
          box-shadow:
            6px 6px 18px var(--ds),
            -3px -3px 10px var(--ls),
            0 0 0 1px rgba(255,255,255,0.04);
          display: flex;
          flex-direction: column;
          flex: 0 0 auto;
        }

        /* ── Title bar ── */
        .titlebar {
          app-region: drag;
          height: 40px;
          border-radius: 16px 16px 0 0;
          background: var(--raised);
          display: flex;
          align-items: center;
          padding: 0 10px;
          gap: 8px;
          box-shadow: 0 2px 8px var(--ds), inset 0 1px 0 var(--ls);
          user-select: none;
          flex-shrink: 0;
          cursor: move;
        }
        .brand-mark { display: block; width: 28px; height: 24px; object-fit: contain; flex-shrink: 0; }
        .brand-wordmark { display: block; width: 90px; height: 20px; object-fit: contain; }
        .brand-name { font-size: 11px; font-weight: 500; color: var(--text); }
        .spacer { flex: 1; }
        .win-btn {
          width: 24px; height: 24px; border-radius: 6px;
          border: none; display: flex; align-items: center; justify-content: center;
          background: var(--surface);
          box-shadow: 3px 3px 7px var(--ds), -2px -2px 5px var(--ls);
          color: var(--sub); flex-shrink: 0;
          transition: background 0.1s, color 0.1s;
        }
        .win-btn:hover { background: var(--raised); color: var(--text); }
        .win-btn.close-btn:hover { background: rgba(176,96,96,0.25); color: #e09090; }
        svg[data-icon-family="phosphor"] { fill:currentColor;stroke:none; }
        .win-btn svg { width: 12px; height: 12px; }

        /* ── Body ── */
        .body { padding: 16px 14px 14px; flex-shrink: 0; }

        /* File row */
        .file-row { display: flex; align-items: center; gap: 10px; margin-bottom: 12px; }
        .icon-well {
          width: 34px; height: 34px; border-radius: 10px; flex-shrink: 0;
          display: flex; align-items: center; justify-content: center;
          background: var(--inset);
          box-shadow: inset 3px 3px 7px var(--ds), inset -2px -2px 5px var(--ls);
        }
        .icon-well svg { width: 16px; height: 16px; }
        .file-info { min-width: 0; flex: 1; }
        .file-name {
          font-size: 11px; font-weight: 500; color: var(--bright);
          white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
        }
        .file-host { font-size: 10px; color: var(--sub); margin-top: 2px; }

        /* Speed chart */
        .chart-well {
          border-radius: 14px; padding: 12px 12px 8px; margin-bottom: 11px;
          background: radial-gradient(ellipse at 80% 0%,#302044 0%,#1c142a 58%);
          border: 1px solid #3a2b51;
          box-shadow: inset 0 1px 0 #b99bff12, 0 8px 20px #09051025;
        }
        .chart-heading { font-size: 10px; color: var(--text); }
        .chart-speed { font-size: 25px; font-weight: 500; line-height: 1.4; letter-spacing: -.6px; color: var(--text); font-variant-numeric: tabular-nums; }
        .chart-speed span { font-size: 10px; font-weight: 400; letter-spacing: 0; color: var(--sub); }
        #speedChart { width: 100%; height: 72px; display: block; }
        .chart-hint { font-size: 9px; color: var(--sub); }
        .chart-footer { display: flex; justify-content: space-between; font-size: 9px; color: var(--sub); padding-top: 3px; }

        /* Progress */
        .progress-track {
          height: 9px; border-radius: 5px; margin-bottom: 9px; overflow: hidden;
          background: #665471;
          border: 1px solid #b9aacd38;
          box-shadow: inset 0 1px 2px #0003;
        }
        .progress-fill {
          height: 100%; width: 0%; border-radius: 3px;
          background: linear-gradient(90deg, var(--accent-dim), var(--accent), #b07edc);
          box-shadow: 0 0 8px rgba(131,80,196,0.5);
          transition: width 0.9s linear;
        }

        .progress-fill.indeterminate { width:35%!important; animation:transfer 1.4s ease-in-out infinite alternate; }
        @keyframes transfer { from { transform:translateX(0); } to { transform:translateX(180%); } }
        @media(prefers-reduced-motion:reduce) { .progress-fill.indeterminate { animation:none; } }
        .task-status { font-size:10px;line-height:1.5;color:var(--sub);margin:0 0 10px;overflow-wrap:anywhere; }
        .action-error { font-size:10px;line-height:1.5;color:#f0adad;margin:0 0 10px;overflow-wrap:anywhere; }
        button:focus-visible { outline:2px solid var(--bright);outline-offset:2px; }

        /* Segment strip */
        .seg-strip { display: flex; gap: 3px; margin-bottom: 14px; }
        .seg-strip div { height: 3px; border-radius: 2px; flex: 1; background: var(--muted); opacity: 0.3; }
        .seg-strip div.done {
          background: var(--accent); opacity: 1;
          box-shadow: 0 0 4px rgba(131,80,196,0.55);
        }
        .seg-strip div.active { background: var(--accent-dim); opacity: 1; }

        /* Stats */
        .stats { display: grid; grid-template-columns: 1fr 1fr 1fr; margin-bottom: 12px; }
        .stat { padding: 8px 10px; border-radius: 8px; }
        .stat.raised {
          background: var(--raised);
          box-shadow: 3px 3px 7px var(--ds), -2px -2px 5px var(--ls);
        }
        .stat.inset {
          margin: 0 6px;
          background: var(--inset);
          box-shadow: inset 2px 2px 6px var(--ds), inset -1px -1px 4px var(--ls);
        }
        .stat-label { font-size: 9px; color: var(--sub); margin-bottom: 3px; }
        .stat-val   { font-size: 14px; font-weight: 500; line-height: 1; color: var(--bright); }
        .stat.inset .stat-label, .stat.inset .stat-val { text-align: center; }
        .stat.inset .stat-val { color: var(--accent); }
        .stat:last-child .stat-label, .stat:last-child .stat-val { text-align: right; }

        /* Detail */
        .detail { font-size: 10px; color: var(--sub); margin-bottom: 12px;
                  white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

        /* Actions */
        .actions { display: flex; gap: 8px; }
        .action-btn {
          flex: 1; padding: 9px 0; border: none; border-radius: 8px;
          font-size: 11px; display: flex; align-items: center; justify-content: center; gap: 5px;
          background: var(--raised);
          box-shadow: 3px 3px 8px var(--ds), -2px -2px 6px var(--ls);
          color: var(--text);
          transition: box-shadow 0.1s;
        }
        .action-btn:active:not(:disabled) {
          box-shadow: inset 2px 2px 5px var(--ds), inset -1px -1px 3px var(--ls);
        }
        .action-btn.danger { color: var(--danger); }
        .action-btn svg { width: 12px; height: 12px; flex-shrink: 0; }

        /* Queue footer */
        .queue-footer {
          border-top: 1px solid rgba(255,255,255,0.03);
          padding: 9px 14px;
          display: flex; align-items: center; gap: 8px;
          background: var(--inset);
          box-shadow: inset 0 2px 6px var(--ds);
          flex-shrink: 0;
        }
        .queue-footer svg { width: 12px; height: 12px; flex-shrink: 0; fill: var(--muted); }
        .queue-text { font-size: 10px; color: var(--muted); flex: 1;
                      white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

        /* Mini pill (collapsed) */
        .mini-pill {
          display: none;
          align-items: center; gap: 10px;
          padding: 8px 12px; flex-shrink: 0;
        }
        .pill-track {
          flex: 1; height: 5px; border-radius: 3px; overflow: hidden;
          background: var(--inset);
          box-shadow: inset 2px 2px 4px var(--ds);
        }
        .pill-fill {
          height: 100%; width: 0%; border-radius: 3px;
          background: linear-gradient(90deg, var(--accent-dim), var(--accent));
          transition: width 0.9s linear;
        }
        .pill-speed { font-size: 10px; color: var(--bright); flex-shrink: 0; min-width: 54px; text-align: right; }
        /* Compact controller; CSS sizes account for the existing 1.25 interface zoom. */
        html,body{background:var(--surface)}
        body{overflow:auto}.win{min-height:400px;border-radius:12px}
        .titlebar{height:32px;padding:0 10px;border-radius:12px 12px 0 0}
        .brand-name{font-size:10.4px}.win-btn{width:25px;height:25px}
        .file-row{margin:12px 12px 10px;gap:9px}.file-name{font-size:11.2px;font-weight:600}
        .file-meta{display:flex;gap:8px;align-items:center;margin-top:3px;min-width:0}.file-host{font-size:9.6px;margin:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .file-status{font-size:9.6px;color:var(--accent-text);flex-shrink:0}.file-status[data-state=Failed]{color:#e7a5af}.file-status[data-state=Completed]{color:#a9cfb5}.file-status[data-state=Paused]{color:#dfc18e}
        .body{padding:0 12px;flex:1}.chart-well{padding:10px 12px 7px;margin-bottom:12px}
        .chart-heading,.chart-hint,.chart-footer{font-size:9.6px}.chart-header{display:flex;justify-content:space-between;gap:8px}.chart-hint{color:var(--sub)}
        .chart-speed{font-size:23.2px;line-height:1.4}.chart-speed span{font-size:9.6px}#speedChart{height:86px}
        .stats{grid-template-columns:1fr 1fr;gap:10px;margin-bottom:10px}.stat{padding:8px 10px}.stat.inset{margin:0;background:var(--raised);box-shadow:none}.stat.raised{box-shadow:none}
        .stat-label{font-size:9.6px}.stat-val{font-size:16px;color:var(--text)!important}.stat.inset .stat-label,.stat.inset .stat-val{text-align:left}
        .progress-track{height:8px;margin:0 12px 6px;flex-shrink:0}.detail{font-size:10.4px;font-weight:500;color:var(--bright);margin:0 12px 10px;flex-shrink:0}
        .task-status,.action-error{font-size:9.6px;margin:0 12px 8px;max-height:58px;overflow:auto;line-height:1.45}
        .actions{margin:0 12px 12px;flex-shrink:0}.action-btn{font-size:10.4px;min-height:28px;padding:5px 0}.action-btn.danger{color:#dc9bA7}
        .queue-footer{padding:7px 12px}.queue-text{font-size:9.6px}
        .mini-pill{margin:0 12px 6px;padding:0;justify-content:space-between;gap:8px}.pill-speed{font-size:12px;text-align:left}.mini-progress{font-size:9.6px;color:var(--sub)}
        .win.mini{min-height:128px}.mini .titlebar{height:26px}.mini .file-row{margin:6px 12px;gap:7px}.mini .icon-well{display:none}.mini .file-meta{margin-top:2px}.mini .file-host{display:none}.mini .file-status{font-size:9.6px}.mini .file-name{font-size:10.4px}.mini .file-info{display:flex;align-items:baseline;gap:8px}.mini .file-name{flex:1}.mini .file-meta{flex-shrink:0}
        .mini .detail{display:none}.mini .actions{margin-bottom:8px;gap:6px}.mini .action-btn{min-height:26px}.mini .task-status{display:none}.mini .action-error{max-height:36px}
        @media(prefers-reduced-motion:reduce){.progress-fill,.pill-fill{transition:none}}
        @media(pointer:coarse){.win-btn{width:35.2px;height:35.2px}.titlebar,.mini .titlebar{height:40px}.action-btn,.mini .action-btn{min-height:35.2px}}
        </style>
        <style>/* SHARED_THEME */</style>
        </head>
        <body>
        <div class="win">

          <!-- Title bar -->
          <div class="titlebar" id="titlebar">
            <img class="brand-mark" src="data:image/png;base64,__SYPHON_EMBLEM__" alt="" draggable="false">
            <span class="brand-name"><img class="brand-wordmark" src="data:image/png;base64,__SYPHON_WORDMARK__" alt="Syphon" draggable="false"></span>
            <div class="spacer"></div>
            <button class="win-btn" id="minBtn" title="Switch to Mini mode" aria-label="Mini mode" aria-expanded="true" aria-controls="winBody">
              <svg data-icon-family="phosphor" aria-hidden="true"  xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="currentColor"><path d="M216,40H40A16,16,0,0,0,24,56V200a16,16,0,0,0,16,16H216a16,16,0,0,0,16-16V56A16,16,0,0,0,216,40ZM40,56H80V200H40ZM216,200H96V56H216V200Z"/></svg>
            </button>
            <button class="win-btn" id="minimizeBtn" title="Minimize — download continues" aria-label="Minimize floating window"><svg data-icon-family="phosphor" aria-hidden="true"  xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="currentColor"><path d="M224,128a8,8,0,0,1-8,8H40a8,8,0,0,1,0-16H216A8,8,0,0,1,224,128Z"/></svg></button>
            <button class="win-btn close-btn" id="closeBtn" title="Close — downloads continue" aria-label="Close floating window; download continues">
              <svg data-icon-family="phosphor" aria-hidden="true"  xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="currentColor"><path d="M205.66,194.34a8,8,0,0,1-11.32,11.32L128,139.31,61.66,205.66a8,8,0,0,1-11.32-11.32L116.69,128,50.34,61.66A8,8,0,0,1,61.66,50.34L128,116.69l66.34-66.35a8,8,0,0,1,11.32,11.32L139.31,128Z"/></svg>
            </button>
          </div>

            <!-- File row -->
            <div class="file-row">
              <div class="icon-well">
                <svg data-icon-family="phosphor" aria-hidden="true"  xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="currentColor"><path d="M213.66,82.34l-56-56A8,8,0,0,0,152,24H56A16,16,0,0,0,40,40V216a16,16,0,0,0,16,16H200a16,16,0,0,0,16-16V88A8,8,0,0,0,213.66,82.34ZM160,51.31,188.69,80H160ZM200,216H56V40h88V88a8,8,0,0,0,8,8h48V216Z"/></svg>
              </div>
              <div class="file-info">
                <div class="file-name" id="fileName" title="">—</div>
                <div class="file-meta"><div class="file-host" id="fileHost"></div><span class="file-status" id="fileStatus" role="status"></span></div>
              </div>
            </div>

          <div class="mini-pill" id="miniPill"><span class="pill-speed" id="pillSpeed">—</span><span class="mini-progress" id="miniProgress"></span></div>
          <div class="body" id="winBody">
            <!-- Speed chart -->
            <div class="chart-well">
              <div class="chart-header"><span class="chart-heading">Transfer speed</span><span class="chart-hint" id="chartHint">Last 60s</span></div>
              <div class="chart-speed" id="chartSpeed">—</div>
              <canvas id="speedChart" role="img" aria-label="Recent download speed in MB per second"></canvas>
              <div class="chart-footer"><span>Earlier</span><span>Now</span></div>
            </div>

            <!-- Stats -->
            <div class="stats">
              <div class="stat inset">
                <div class="stat-label">Progress</div>
                <div class="stat-val" id="statPct">—</div>
              </div>
              <div class="stat raised">
                <div class="stat-label">Time remaining</div>
                <div class="stat-val" id="statEta">—</div>
              </div>
            </div>

          </div><!-- /body -->

            <!-- Progress bar -->
            <div class="progress-track" id="progressTrack" role="progressbar" aria-label="Download progress" aria-valuemin="0" aria-valuemax="100">
              <div class="progress-fill" id="progressFill"></div>
            </div>

            <!-- Detail -->
            <div class="detail" id="detail"></div>

            <div class="task-status" id="taskStatus" role="status"></div>
            <div class="action-error" id="actionError" role="alert" hidden></div>

            <!-- Actions -->
            <div class="actions">
              <button class="action-btn" id="pauseBtn">
                <svg data-icon-family="phosphor" aria-hidden="true" id="pauseIcon" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="currentColor"><path d="M200,32H160a16,16,0,0,0-16,16V208a16,16,0,0,0,16,16h40a16,16,0,0,0,16-16V48A16,16,0,0,0,200,32Zm0,176H160V48h40ZM96,32H56A16,16,0,0,0,40,48V208a16,16,0,0,0,16,16H96a16,16,0,0,0,16-16V48A16,16,0,0,0,96,32Zm0,176H56V48H96Z"/></svg>
                <span id="pauseLabel">Pause</span>
              </button>
              <button class="action-btn" id="managerBtn">
                <svg data-icon-family="phosphor" aria-hidden="true"  xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="currentColor"><path d="M224,104a8,8,0,0,1-16,0V59.32l-66.33,66.34a8,8,0,0,1-11.32-11.32L196.68,48H152a8,8,0,0,1,0-16h64a8,8,0,0,1,8,8Zm-40,24a8,8,0,0,0-8,8v72H48V80h72a8,8,0,0,0,0-16H48A16,16,0,0,0,32,80V208a16,16,0,0,0,16,16H176a16,16,0,0,0,16-16V136A8,8,0,0,0,184,128Z"/></svg>
                Manager
              </button>
              <button class="action-btn danger" id="cancelBtn">
                <svg data-icon-family="phosphor" aria-hidden="true"  xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="currentColor"><path d="M205.66,194.34a8,8,0,0,1-11.32,11.32L128,139.31,61.66,205.66a8,8,0,0,1-11.32-11.32L116.69,128,50.34,61.66A8,8,0,0,1,61.66,50.34L128,116.69l66.34-66.35a8,8,0,0,1,11.32,11.32L139.31,128Z"/></svg>
                Cancel
              </button>
            </div>

          <!-- Queue footer -->
          <div class="queue-footer" id="queueFooter" hidden>
            <svg data-icon-family="phosphor" aria-hidden="true"  xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" fill="currentColor"><path d="M128,24A104,104,0,1,0,232,128,104.11,104.11,0,0,0,128,24Zm0,192a88,88,0,1,1,88-88A88.1,88.1,0,0,1,128,216Zm64-88a8,8,0,0,1-8,8H128a8,8,0,0,1-8-8V72a8,8,0,0,1,16,0v48h48A8,8,0,0,1,192,128Z"/></svg>
            <span class="queue-text" id="queueText"></span>
          </div>

        </div><!-- /win -->

        <script>
        'use strict';
        const $ = id => document.getElementById(id);
        let displayedTaskId=null,latestSamples=[],latestTimes=[];
        const send = obj => window.chrome.webview.postMessage({id:displayedTaskId,...obj});

        // ── Speed chart ──────────────────────────────────────────────────────
        const canvas = $('speedChart');
        const ctx    = canvas.getContext('2d');

        function drawChart(samples) {
          latestSamples=Array.isArray(samples)?samples.filter(v=>Number.isFinite(v)&&v>=0):[];samples=latestSamples;
          canvas.dataset.sampleCount=samples.length;canvas.dataset.maxSpeed=Math.max(0,...samples);
          $('chartHint').textContent='Last 60s';
          if(canvas.offsetWidth<=0)return;
          const dpr = devicePixelRatio || 1;
          const W   = canvas.offsetWidth;
          const H   = canvas.offsetHeight;
          canvas.width  = Math.round(W * dpr);
          canvas.height = Math.round(H * dpr);
          ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
          ctx.clearRect(0, 0, W, H);

          // samples are bytes/s — convert to MB/s
          const mbps = samples.map(s => s / 1_048_576);
          const max  = Math.max(...mbps, 0.01) * 1.18;
          const left=4, right=W-8, top=10, bottom=H-5;
          const theme=getComputedStyle(document.documentElement),colour=name=>theme.getPropertyValue('--ov-'+name).trim();
          ctx.strokeStyle=colour('chart-grid');ctx.lineWidth=1;
          for(let i=0;i<4;i++){const y=top+(bottom-top)*i/3;ctx.beginPath();ctx.moveTo(left,y);ctx.lineTo(right,y);ctx.stroke();}
          if (!samples.length) return;
          const pts  = mbps.map((v, i) => ({
            x: Number.isFinite(latestTimes[i])?right-Math.max(0,Math.min(60000,Date.now()-latestTimes[i]))/60000*(right-left):right,
            y: bottom - (v / max) * (bottom-top),
          }));
          const trace=()=>{ctx.moveTo(pts[0].x,pts[0].y);for(let i=1;i<pts.length;i++){const p=pts[i-1],q=pts[i],mid=(p.x+q.x)/2;ctx.bezierCurveTo(mid,p.y,mid,q.y,q.x,q.y);}};

          // Filled area
          ctx.beginPath();
          trace();
          ctx.lineTo(pts[pts.length - 1].x, bottom);
          ctx.lineTo(pts[0].x, bottom);
          ctx.closePath();
          const grad = ctx.createLinearGradient(0, 0, 0, H);
          grad.addColorStop(0, colour('chart-fill'));
          grad.addColorStop(1, colour('chart-clear'));
          ctx.fillStyle = grad;
          ctx.fill();

          // Stroke
          ctx.beginPath();
          trace();
          ctx.strokeStyle = colour('edge-light');ctx.lineWidth=8;ctx.stroke();
          ctx.strokeStyle = colour('chart-line');
          ctx.lineWidth   = 2.5;
          ctx.lineJoin    = 'round';
          ctx.lineCap     = 'round';
          ctx.stroke();
          const end=pts[pts.length-1];
          ctx.beginPath();ctx.arc(end.x,end.y,7,0,Math.PI*2);ctx.fillStyle=colour('glow-soft');ctx.fill();
          ctx.beginPath();ctx.arc(end.x,end.y,3,0,Math.PI*2);ctx.fillStyle=colour('chart-line');ctx.fill();
        }

        // ── Model updates ────────────────────────────────────────────────────
        window.chrome.webview.addEventListener('message', ev => {
          let msg;
          try { msg = typeof ev.data === 'string' ? JSON.parse(ev.data) : ev.data; } catch { return; }
          if (msg.type !== 'model') return;
          document.documentElement.dataset.typography=msg.model.typographyPreset||'inter';

          const { task, samples, sampleTimes, next } = msg.model || {};
          if (!task) return;
          displayedTaskId=task.id;
          const explanation=msg.model.actionPending?'Applying your action…':task.message||task.status||'';
          if($('taskStatus').textContent!==explanation)$('taskStatus').textContent=explanation;
          $('actionError').hidden=!msg.model.error;
          if($('actionError').textContent!==(msg.model.error||''))$('actionError').textContent=msg.model.error||'';

          $('fileName').textContent  = task.filename ?? '—';
          $('fileName').title        = task.filename ?? '';
          $('fileHost').textContent  = (task.host ?? '')+(task.mediaDuration?' · '+task.mediaDuration:'');
          $('fileHost').title=$('fileHost').textContent;
          $('fileStatus').textContent=msg.model.actionPending?'Applying…':task.state==='RetryWaiting'?'Retrying':task.state==='Starting'?'Starting':task.state;
          $('fileStatus').dataset.state=task.state;
          $('fileStatus').title=explanation;
          $('taskStatus').hidden=!['Failed','RetryWaiting'].includes(task.state)&&!msg.model.actionPending;
          $('taskStatus').title=explanation;
          latestTimes=(sampleTimes||[]).map(t=>Date.parse(t));

          const progressTotal = task.total > 0 ? task.total : task.estimatedTotal > task.received ? task.estimatedTotal : null;
          const estimated = task.total == null && progressTotal != null;
          const pct = progressTotal > 0
            ? Math.min(100, (task.received / progressTotal) * 100)
            : (task.state === 'Completed' ? 100 : 0);

          $('progressFill').style.width = pct.toFixed(1) + '%';
          $('progressFill').classList.toggle('indeterminate',progressTotal==null&&task.canPause);
          if(progressTotal!=null)$('progressTrack').setAttribute('aria-valuenow',pct.toFixed(1));else $('progressTrack').removeAttribute('aria-valuenow');
          $('progressTrack').setAttribute('aria-valuetext',task.state+' · '+(task.progress||'')+(progressTotal>0?' · '+(estimated?'estimated ':'')+pct.toFixed(1)+'%':''));

          const speedParts=(task.speed||'—').split(' ');
          const speedValue=document.createTextNode(speedParts.shift()+' '),speedUnit=document.createElement('span');
          speedUnit.textContent=speedParts.join(' ');
          $('chartSpeed').replaceChildren(speedValue,speedUnit);
          $('statPct').textContent   = progressTotal > 0 ? (estimated?'≈':'') + pct.toFixed(1) + '%' : task.state==='Completed'?'100%':'Waiting for size';
          $('statEta').textContent   = task.remaining ?? '—';
          $('detail').textContent    = task.progress
            ? task.progress + (task.connections > 1 ? `  ·  ${task.connections} connections` : '')
            : task.status ?? '';

          $('pillSpeed').textContent = task.speed ?? '—';
          const estimate=task.estimatedTotal>task.received?'≈'+(task.expectedSize||'').replace(/^≈/,'').replace(' · Estimated',''):null;
          $('miniProgress').textContent=(progressTotal>0?(estimated?'≈':'')+pct.toFixed(1)+'%':task.state==='Completed'?'100%':estimate||'Size unknown')+(task.remaining&&task.remaining!=='—'?' · '+task.remaining+' left':'');
          $('miniProgress').title=task.progress||'';

          // Pause / resume label
          const resume = ['Paused','Cancelled','Failed'].includes(task.state);
          $('pauseLabel').textContent = resume ? 'Resume' : 'Pause';
          const pauseIcon = $('pauseIcon');
          if (resume) {
            pauseIcon.innerHTML = '<path d="M232.4,114.49,88.32,26.35a16,16,0,0,0-16.2-.3A15.86,15.86,0,0,0,64,39.87V216.13A15.94,15.94,0,0,0,80,232a16.07,16.07,0,0,0,8.36-2.35L232.4,141.51a15.81,15.81,0,0,0,0-27ZM80,215.94V40l143.83,88Z"/>';
          } else {
            pauseIcon.innerHTML =
              '<path d="M200,32H160a16,16,0,0,0-16,16V208a16,16,0,0,0,16,16h40a16,16,0,0,0,16-16V48A16,16,0,0,0,200,32Zm0,176H160V48h40ZM96,32H56A16,16,0,0,0,40,48V208a16,16,0,0,0,16,16H96a16,16,0,0,0,16-16V48A16,16,0,0,0,96,32Zm0,176H56V48H96Z"/>';
          }
          $('pauseBtn').disabled = msg.model.actionPending || (!task.canResume&&!task.canPause);
          $('pauseBtn').setAttribute('aria-label',resume?'Resume download':'Pause download');
          $('cancelBtn').disabled = msg.model.actionPending || !task.canPause;

          // Queue footer
          const qt = next?.filename ? `Next: ${next.filename}` : '';
          $('queueText').textContent = qt;
          $('queueFooter').hidden    = collapsed || !qt;

          drawChart(samples);

          // Report natural height so C# can resize the window
          send({ action: 'layout', height: document.querySelector('.win').scrollHeight });
        });

        // ── User actions ─────────────────────────────────────────────────────
        let collapsed = false;

        $('closeBtn').addEventListener('click', () => send({ action: 'close' }));
        $('minimizeBtn').addEventListener('click',()=>send({action:'minimize'}));

        $('minBtn').addEventListener('click', () => {
          collapsed = !collapsed;
          document.querySelector('.win').classList.toggle('mini',collapsed);
          $('minBtn').title=collapsed?'Switch to Expanded mode':'Switch to Mini mode';
          $('minBtn').setAttribute('aria-label',collapsed?'Expanded mode':'Mini mode');
          $('minBtn').setAttribute('aria-expanded',String(!collapsed));
          $('winBody').hidden   = collapsed;
          $('queueFooter').hidden = true; // hide during any state change; model will restore
          $('miniPill').style.display = collapsed ? 'flex' : 'none';
          send({ action: 'collapse', collapsed });
          if(!collapsed)requestAnimationFrame(()=>drawChart(latestSamples));
          send({action:'layout',height:document.querySelector('.win').scrollHeight});
        });

        $('pauseBtn').addEventListener('click',   () => send({ action: 'toggle'  }));
        $('managerBtn').addEventListener('click', () => send({ action: 'manager' }));
        $('cancelBtn').addEventListener('click',  () => send({ action: 'cancel'  }));

        const observer=new ResizeObserver(()=>{drawChart(latestSamples);send({action:'layout',height:document.querySelector('.win').scrollHeight});});
        observer.observe(document.querySelector('.win'));
        // Handshake
        send({ action: 'ready' });
        </script>
        </body>
        </html>
        """;
}
