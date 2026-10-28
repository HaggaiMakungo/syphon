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
    internal Guid? SelectedTaskId { get; private set; }
    internal bool? PinnedOverride { get; private set; }

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
                pinned = Topmost,
                typographyPreset = TypographyComparison.Current,
                actionPending = _actionPending,
                error = _actionError,
                task    = current is null ? null : MainWindow.TaskModel(current),
                tasks = _htmlTasks.Where(t=>t.State is not (DownloadState.Completed or DownloadState.Cancelled or DownloadState.Failed)).Select(MainWindow.TaskModel).ToArray(),
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
        Width  = 360*scale;
        Height = 510*scale;
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
        _view.DefaultBackgroundColor = System.Drawing.Color.Transparent;
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

            if(_view is System.Windows.Interop.HwndHost directHost)CustomWindowFrame.RegisterContent(this,directHost);
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

            var folder=Path.Combine(AppContext.BaseDirectory,"DesktopAssets");
            var allowed=new HashSet<string>(StringComparer.Ordinal){"index.html","app.js","interface.css","brand/emblem.png","inter-400.woff2","inter-500.woff2","inter-600.woff2"};
            core.NavigationStarting+=(_,e)=>e.Cancel=e.Uri!=FloatOrigin+"/";
            core.NewWindowRequested+=(_,e)=>e.Handled=true;
            core.DownloadStarting+=(_,e)=>e.Cancel=true;
            core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            core.WebResourceRequested+=(_,e)=>{
                var name=e.Request.Uri.StartsWith(FloatOrigin+"/",StringComparison.Ordinal)?e.Request.Uri[(FloatOrigin.Length+1)..]:"";
                if(e.Request.Uri==FloatOrigin+"/")name="index.html";
                if(!allowed.Contains(name)){e.Response=_env.CreateWebResourceResponse(null,403,"Blocked","");return;}
                var mime=name.EndsWith(".html")?"text/html":name.EndsWith(".js")?"application/javascript":name.EndsWith(".css")?"text/css":name.EndsWith(".png")?"image/png":"font/woff2";
                e.Response=_env.CreateWebResourceResponse(new MemoryStream(File.ReadAllBytes(Path.Combine(folder,name))),200,"OK","Content-Type: "+mime);
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
            var target=current;
            if(msg.TryGetProperty("id",out var id)){
                if(id.ValueKind!=JsonValueKind.String||!Guid.TryParse(id.GetString(),out var targetId))throw new InvalidOperationException("Invalid download selection.");
                target=_htmlTasks.FirstOrDefault(t=>t.Id==targetId);
            }
            if (target is null)
                throw new InvalidOperationException("The displayed download changed. Try the action again.");
            _actionPending = true; _actionError = null; PublishHtml();
            try
            {
                var row = new DownloadRow(target);
                if (command == "toggle" && (row.CanResume||target.State==DownloadState.Queued)) _resumeTask!(target.Id);
                else if (row.CanPause) await (command == "cancel" ? _cancelTask!(target.Id) : _pauseTask!(target.Id));
                else throw new InvalidOperationException("This download cannot be changed right now. Check its status in the manager.");
            }
            finally { _actionPending = false; PublishHtml(); }
            return;
        }

        switch (command)
        {
            case "select-task":
                var selected=msg.GetProperty("id").GetGuid();
                var selectedTask=_htmlTasks.FirstOrDefault(t=>t.Id==selected);
                if(selectedTask is not null){SelectedTaskId=selectedTask.Id;current=selectedTask;speeds.Clear();speedTimes.Clear();PublishHtml();}
                break;
            case "pause-all":
            case "resume-all":
                if(_actionPending)return;_actionPending=true;PublishHtml();
                try{foreach(var task in _htmlTasks.ToArray()){var row=new DownloadRow(task);if(command=="pause-all"&&row.CanPause&&task.State!=DownloadState.Queued)await _pauseTask!(task.Id);else if(command=="resume-all"&&(row.CanResume||task.State==DownloadState.Queued))_resumeTask!(task.Id);}}
                finally{_actionPending=false;PublishHtml();}
                break;
            case "folder":
                var folderId=msg.GetProperty("id").GetGuid();var folderTask=_htmlTasks.FirstOrDefault(t=>t.Id==folderId);
                if(folderTask is null)throw new InvalidOperationException("This download is no longer available.");
                DownloadLocation.Launch(DownloadLocation.OpenFolder(folderTask.Destination));
                break;
            case "pin":
                Topmost=!Topmost;PinnedOverride=Topmost;PublishHtml();
                break;
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
                Width = 360*scale;
                break;

        }
    }

}
