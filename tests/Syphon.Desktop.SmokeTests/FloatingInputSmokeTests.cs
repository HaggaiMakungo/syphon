using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;
using Syphon.TestServer;

namespace Syphon.Desktop.SmokeTests;

internal static class FloatingInputSmokeTests
{
    // --live leaves the isolated fixture visible for OS mouse/drag verification.
    public static int Run(string[] args)
    {
        var live = args.Contains("--live");
        var output = Path.GetFullPath(args.FirstOrDefault(a => a != "--live") ?? "artifacts/floating-input");
        Directory.CreateDirectory(output);
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var code = 1;
        FloatingDownloadWindow? window = null;
        var pauses = 0; var resumes = 0; var cancels = 0; var managers = 0; var dismissed = 0; var updates = 0; var collapses = 0;
        var started = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        async void Start(object? sender, EventArgs e)
        {
            if (started) return; started = true;
            await using var server = await FixtureServer.StartAsync();
            var folder = Path.Combine(Path.GetTempPath(), "Syphon.Float.Tests", Guid.NewGuid().ToString("N"));
            await using var coordinator = await DownloadCoordinator.OpenAsync(folder);
            try
            {
                var task = coordinator.Add(new Uri(server.BaseAddress, "resume?size=67108864"), Path.Combine(folder, "input-regression.bin"), singleConnection: true);
                coordinator.SetSpeedLimit(task.Id, 262144);
                coordinator.Resume(task.Id);
                window = new FloatingDownloadWindow(
                    async id => { pauses++; await coordinator.PauseAsync(id); },
                    id => { resumes++; coordinator.Resume(id); },
                    async id => { cancels++; await coordinator.PauseAsync(id, true); },
                    () => managers++, _ => dismissed++)
                    { Left = live ? 120 : -10000, Top = live ? 120 : -10000, Topmost = live, ShowInTaskbar = live, Title = "Syphon — Input regression fixture" };
                void Report()
                {
                    var snapshot = coordinator.Get(task.Id);
                    File.WriteAllText(Path.Combine(output, "state.json"), JsonSerializer.Serialize(new {
                        state = snapshot.State.ToString(), snapshot.Received, pauses, resumes, cancels, managers, dismissed, updates, collapses,
                        window.Left, window.Top, window.Height, window.IsVisible
                    }));
                }
                timer.Tick += (_, _) => { window.Update(coordinator.Get(task.Id), coordinator.GetTasks()); updates++; Report(); };
                window.Update(coordinator.Get(task.Id), coordinator.GetTasks()); window.Show(); timer.Start();
                var view = ((Grid)window.Content).Children.OfType<IWebView2>().Single();
                await Until(() => view.CoreWebView2 != null);
                var core = view.CoreWebView2;
                core.WebMessageReceived += (_, message) => {
                    using var json = JsonDocument.Parse(message.WebMessageAsJson);
                    if (json.RootElement.GetProperty("action").GetString() == "collapse") collapses++;
                };
                async Task JsUntil(string predicate) { await UntilAsync(async () => await core.ExecuteScriptAsync(predicate) == "true"); }
                async Task Capture(string name) { await using var image = File.Create(Path.Combine(output,name)); await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image); }
                await JsUntil("!!document.querySelector('#pauseBtn')&&!document.querySelector('#pauseBtn').disabled");
                if (live)
                {
                    Console.WriteLine("READY isolated native mouse fixture; close it to finish (five minute timeout).");
                    await Until(() => !window.IsVisible, 300);
                    Check(pauses > 0 && resumes > 0 && managers > 0 && cancels > 0 && dismissed > 0 && updates > 10 && collapses >= 2,
                        "Native input run did not exercise every action while updating.");
                    Check(Math.Abs(window.Left - 120) > 10 || Math.Abs(window.Top - 120) > 10, "Native title-bar drag did not move the window.");
                }
                else
                {
                    Check(!window.AllowsTransparency && view is WebView2, "Floating window lost its opaque direct browser host.");
                    Check(core.Settings.IsNonClientRegionSupportEnabled, "Native caption regions are disabled.");
                    Check(await core.ExecuteScriptAsync("getComputedStyle(document.querySelector('#titlebar')).getPropertyValue('app-region')==='drag'") == "true", "Title bar is not a native drag region.");
                    Check(await core.ExecuteScriptAsync("[...document.querySelectorAll('button,button *')].every(b=>getComputedStyle(b).getPropertyValue('app-region')==='no-drag')") == "true", "Buttons or icons overlap native drag regions.");
                    // Browser input dispatch hit-tests the actual DOM; unlike element.click(), it catches overlays.
                    async Task Click(string id)
                    {
                        using var point = JsonDocument.Parse(await core.ExecuteScriptAsync($"(()=>{{const r=document.getElementById('{id}').getBoundingClientRect();return {{x:r.x+r.width/2,y:r.y+r.height/2}};}})()"));
                        var x = point.RootElement.GetProperty("x").GetDouble(); var y = point.RootElement.GetProperty("y").GetDouble();
                        foreach (var type in new[] { "mousePressed", "mouseReleased" })
                            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type, x, y, button = "left", clickCount = 1 }));
                    }
                    await Click("pauseBtn"); await Until(() => coordinator.Get(task.Id).State == DownloadState.Paused);
                    await JsUntil("document.querySelector('#pauseLabel').textContent==='Resume'&&!document.querySelector('#pauseBtn').disabled");
                    await Click("pauseBtn"); await Until(() => coordinator.Get(task.Id).State == DownloadState.Downloading);
                    await Until(() => coordinator.Get(task.Id).BytesPerSecond > 0);
                    await JsUntil("Number(document.querySelector('#speedChart').dataset.maxSpeed)>0");
                    Check(window.Width==480&&window.Height>=490&&window.Height<=540,"Expanded controller is outside its compact size target.");
                    Check(await core.ExecuteScriptAsync("!document.querySelector('#statSpeed')&&!document.querySelector('#segStrip')&&document.querySelectorAll('[role=progressbar]').length===1&&document.querySelector('#fileStatus').textContent==='Downloading'")=="true","Redundant statistics/segments or missing explicit status.");
                    await JsUntil("document.documentElement.scrollHeight<=innerHeight+1&&document.documentElement.scrollWidth<=innerWidth+1");
                    Check(await core.ExecuteScriptAsync("parseFloat(getComputedStyle(document.querySelector('#statPct')).fontSize)<parseFloat(getComputedStyle(document.querySelector('#chartSpeed')).fontSize)")=="true","Progress metadata competes with live speed.");
                    await Capture("expanded.png");
                    var before = updates;
                    await Click("minBtn"); await Until(() => window.Width==360&&window.Height>=150&&window.Height<=185);
                    await Until(() => updates >= before + 10);
                    Check(await core.ExecuteScriptAsync("document.querySelector('#winBody').hidden") == "true", "Progress updates expanded the collapsed window.");
                    Check(await core.ExecuteScriptAsync("document.querySelector('#fileName').getBoundingClientRect().width>0&&document.querySelector('#pauseBtn').getBoundingClientRect().bottom<=innerHeight&&document.querySelector('#minBtn').getAttribute('aria-expanded')==='false'")=="true","Mini mode lost its file identity, actions or toggle semantics.");
                    await Capture("mini.png");
                    await Click("pauseBtn"); await Until(() => coordinator.Get(task.Id).State == DownloadState.Paused);
                    await JsUntil("document.querySelector('#fileStatus').textContent==='Paused'");
                    await Click("pauseBtn"); await Until(() => coordinator.Get(task.Id).State == DownloadState.Downloading);
                    await Click("minBtn"); await Until(() => window.Height > 300);
                    await Click("minimizeBtn"); await Until(()=>window.WindowState==WindowState.Minimized);
                    Check(coordinator.Get(task.Id).State==DownloadState.Downloading,"Minimizing stopped the transfer.");
                    window.WindowState=WindowState.Normal;
                    // Repeated Loaded must not attach IPC callbacks again.
                    ((FrameworkElement)view).RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                    await Task.Delay(250);
                    await Click("managerBtn"); await Until(() => managers == 1);
                    await Click("closeBtn"); await Until(() => !window.IsVisible);
                    Check(coordinator.Get(task.Id).State==DownloadState.Downloading,"Closing the controller stopped the transfer.");
                    window.Show();await Click("cancelBtn"); await Until(() => coordinator.Get(task.Id).State == DownloadState.Cancelled);
                    Check(pauses == 2 && resumes == 2 && cancels == 1 && dismissed == 1, "A hit-tested action was lost or repeated.");
                }
                Report(); code = 0; Console.WriteLine("PASS floating input: caption regions, pause/resume, collapse/expand during updates, manager, cancel, close.");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { timer.Stop(); window?.Close(); app.Shutdown(code); }
        }
        app.MainWindow = new Window(); // Prevent App.OnStartup from opening the user's manager/store.
        app.Startup += Start;
        app.Run(); return code;
    }
    private static Task Until(Func<bool> predicate, int seconds = 30) => UntilAsync(() => Task.FromResult(predicate()), seconds);
    private static async Task UntilAsync(Func<Task<bool>> predicate, int seconds = 30)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (!await predicate()) { if (DateTime.UtcNow > end) throw new TimeoutException("Floating input condition timed out."); await Task.Delay(25); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
