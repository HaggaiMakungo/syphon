using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Syphon.Desktop;

public sealed class RechartsSpeedChart : Grid, IDisposable
{
    private const string Origin = "https://syphon-chart.invalid";
    private readonly WebView2CompositionControl browser = new();
    private readonly TextBlock fallback = new() { Text = "Loading speed graph…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
    private double[] samples = [];
    private bool started, ready, disposed;
    public bool IsChartReady => ready;
    public int RenderedSampleCount { get; private set; }
    public string? InitializationError { get; private set; }
    public RechartsSpeedChart()
    {
        ClipToBounds = true;
        Children.Add(fallback); Children.Add(browser);
        browser.Visibility = Visibility.Hidden;
        Loaded += async (_, _) => await InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        if (started || disposed) return; started = true;
        try {
            var folder = Path.Combine(AppContext.BaseDirectory, "ChartAssets");
            if (!File.Exists(Path.Combine(folder, "chart.js"))) throw new IOException("Chart assets are missing.");
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syphon", "ChartProfile");
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            if (disposed) return;
            await browser.EnsureCoreWebView2Async(environment);
            if (disposed) return;
            var core = browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false; core.Settings.IsZoomControlEnabled = false; core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.SetVirtualHostNameToFolderMapping("syphon-chart.invalid", folder, CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting += (_, e) => e.Cancel = e.Uri != Origin + "/index.html";
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => {
                if (e.Request.Uri is not (Origin + "/index.html" or Origin + "/chart.js"))
                    e.Response = environment.CreateWebResourceResponse(null, 403, "Blocked", "");
            };
            core.WebMessageReceived += (_, e) => {
                if (disposed || e.Source != Origin + "/index.html") return;
                try {
                    using var json = JsonDocument.Parse(e.WebMessageAsJson);
                    var root = json.RootElement;
                    if (root.GetProperty("type").GetString() == "ready") { ready = true; fallback.Visibility = Visibility.Collapsed; browser.Visibility = Visibility.Visible; Push(); }
                    else if (root.GetProperty("type").GetString() == "rendered" && root.TryGetProperty("count", out var count) && count.TryGetInt32(out var n) && n is >= 0 and <= 60) RenderedSampleCount = n;
                } catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { }
            };
            core.Navigate(Origin + "/index.html");
        }
        catch (Exception ex) {
            if (disposed) return;
            InitializationError = ex.Message;
            fallback.Text = "Speed graph unavailable Â· transfer controls remain available";
            browser.Visibility = Visibility.Collapsed;
        }
    }
    public void Update(IEnumerable<double> values)
    {
        if (disposed) return;
        samples = values.TakeLast(60).Select(v => double.IsFinite(v) ? Math.Clamp(v, 0, 9007199254740991) : 0).ToArray();
        Push();
    }
    private void Push() { if (ready && !disposed) browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "speed", values = samples })); }
    public async Task CaptureAsync(string path)
    {
        if (!ready) throw new InvalidOperationException(InitializationError ?? "The chart is not ready.");
        await using var file = File.Create(path); await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
    }
    public void Dispose() { if (disposed) return; disposed = true; ready = false; browser.Dispose(); }
}
