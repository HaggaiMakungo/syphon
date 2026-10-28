using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Syphon.Desktop;

/// <summary>A local-only presentation surface; the native coordinator remains the authority.</summary>
public sealed class LocalHtmlSurface : Grid, IDisposable
{
    public const double MainInterfaceScale = 1.0;
    public const double InterfaceScale = 1.0;
    private const string Origin = "https://syphon-ui.invalid";
    private readonly IWebView2 browser;
    public bool UsesDirectRendering { get; }
    private readonly Func<object> model;
    private readonly Func<JsonElement, Task> action;
    private readonly FrameworkElement fallback;
    private readonly Window owner;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private bool initialized, disposed;
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsReady { get; private set; }
    public string? InitializationError { get; private set; }
    public LocalHtmlSurface(Window owner, FrameworkElement nativeContent, Func<object> getModel, Func<JsonElement, Task> onAction)
    {
        TypographyComparison.Changed += PublishTypography;
        this.owner = owner; model = getModel; action = onAction; fallback = nativeContent;
        // Whole-window HTML does not need WPF overlays. Opaque windows can render
        // directly, avoiding the GraphicsCaptureSession/Image round trip.
        UsesDirectRendering = !owner.AllowsTransparency;
        browser = UsesDirectRendering ? new WebView2() : new WebView2CompositionControl();
        browser.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        Children.Add((UIElement)browser); Loaded += async (_, _) => await Initialize();
        owner.Content = null;
        var host = new Grid(); fallback.Visibility = Visibility.Collapsed;
        host.Children.Add(fallback); host.Children.Add(this); owner.Content = host;
        if (!owner.AllowsTransparency) {
            void UpdateFrameMargin() => Margin = new Thickness(owner is MainWindow && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && owner.WindowState!=WindowState.Maximized ? 6 : 0);
            UpdateFrameMargin(); owner.StateChanged += (_, _) => UpdateFrameMargin();
        }
        owner.Closed += (_, _) => Dispose();
    }
    private async Task Initialize()
    {
        if (initialized || disposed) return; initialized = true;
        try {
            var folder = Path.Combine(AppContext.BaseDirectory, "DesktopAssets");
            if (!File.Exists(Path.Combine(folder, "app.js"))) throw new IOException("Desktop UI assets are missing.");
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syphon", "DesktopSrgbProfile");
            // These options affect only Syphon's private UI environment, not Edge,
            // Windows display settings or the user's video/download content.
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--force-color-profile=srgb" };
            var environment = await CoreWebView2Environment.CreateAsync(null, profile, options);
            if (disposed) return;
            await browser.EnsureCoreWebView2Async(environment);
            if (disposed) return;
            if(browser is System.Windows.Interop.HwndHost directHost)CustomWindowFrame.RegisterContent(owner,directHost);
            var core = browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false; core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreHostObjectsAllowed = false; core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsNonClientRegionSupportEnabled = UsesDirectRendering;
            browser.ZoomFactor = owner is MainWindow ? MainInterfaceScale : InterfaceScale;
            if (owner is MainWindow && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                await core.AddScriptToExecuteOnDocumentCreatedAsync("document.addEventListener('DOMContentLoaded',()=>{document.body.dataset.smoothFrame='true'});");
            core.SetVirtualHostNameToFolderMapping("syphon-ui.invalid", folder, CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting += (_, e) => e.Cancel = e.Uri != Origin + "/index.html";
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => {
                if (e.Request.Uri is not (Origin + "/index.html" or Origin + "/app.js" or Origin + "/interface.css" or Origin + "/brand/emblem.png" or Origin + "/brand/wordmark.png" or Origin + "/brand/icon-64.png") && !TypographyComparison.IsFontAsset(e.Request.Uri.StartsWith(Origin + "/", StringComparison.Ordinal) ? e.Request.Uri[(Origin.Length + 1)..] : ""))
                    e.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            core.WebMessageReceived += async (_, e) => {
                if (disposed || e.Source != Origin + "/index.html") return;
                try {
                    var raw = e.WebMessageAsJson; if (raw.Length > 32768) return;
                    using var document = JsonDocument.Parse(raw);
                    var message = document.RootElement;
                    if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("action", out var name) || name.ValueKind != JsonValueKind.String) return;
                    if (name.GetString() == "ready") { IsReady = true; ready.TrySetResult(true); Publish(); }
                    else if (IsReady) await action(message);
                } catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or KeyNotFoundException or FormatException) {
                    if (!disposed) core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "error", message = ex.Message }, Json));
                }
            };
            core.Navigate(Origin + "/index.html");
            await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (!disposed && !IsReady) throw new IOException("The desktop interface did not load. Native controls are available.");
        } catch (Exception ex) {
            if(disposed||owner.Dispatcher.HasShutdownStarted)return;
            InitializationError = ex.Message; Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Visible;
            if (!owner.AllowsTransparency) CustomWindowFrame.RestoreSystemFrame(owner);
        }
    }
    public void Publish() { if (IsReady && !disposed) { browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "model", model = model() }, Json)); PublishTypography(); } }
    private void PublishTypography() { if (IsReady && !disposed) browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "typography", preset = TypographyComparison.Current }, Json)); }
    public Task<string> EvaluateAsync(string script) => browser.CoreWebView2.ExecuteScriptAsync(script);
    public async Task CaptureAsync(string path) { using var file = File.Create(path); await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file); }
    public void Dispose() { if (disposed) return; disposed = true; TypographyComparison.Changed -= PublishTypography; ready.TrySetResult(false); ((IDisposable)browser).Dispose(); }
}
