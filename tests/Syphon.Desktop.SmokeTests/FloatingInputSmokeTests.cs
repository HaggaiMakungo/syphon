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
                    async id => { pauses++; await coordinator.PauseAsync(id); window?.Update(coordinator.Get(id),coordinator.GetTasks()); },
                    id => { resumes++; coordinator.Resume(id); window?.Update(coordinator.Get(id),coordinator.GetTasks()); },
                    async id => { cancels++; await coordinator.PauseAsync(id, true); window?.Update(coordinator.Get(id),coordinator.GetTasks()); },
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
                await JsUntil("!!document.querySelector('.monitor-actions button.primary')&&!document.querySelector('.monitor-actions button.primary').disabled");
                Check(!window.AllowsTransparency && view is WebView2, "Floating window lost its direct browser host.");
                Check(core.Settings.IsNonClientRegionSupportEnabled, "Native caption regions are disabled.");
                Check(await core.ExecuteScriptAsync("getComputedStyle(document.querySelector('.monitor-title')).getPropertyValue('app-region')==='drag'")=="true","Title bar lost native dragging.");
                Check(await core.ExecuteScriptAsync("[...document.querySelectorAll('button,button *')].every(b=>getComputedStyle(b).getPropertyValue('app-region')!=='drag')")=="true","A button overlaps the drag region.");
                async Task Click(string selector) {
                    using var point=JsonDocument.Parse(await core.ExecuteScriptAsync($"(()=>{{const r=document.querySelector('{selector}').getBoundingClientRect();return {{x:r.x+r.width/2,y:r.y+r.height/2}};}})()"));
                    foreach(var type in new[]{"mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x=point.RootElement.GetProperty("x").GetDouble(),y=point.RootElement.GetProperty("y").GetDouble(),button="left",clickCount=1}));
                }
                await Click(".monitor-actions button.primary");await Until(()=>coordinator.Get(task.Id).State==DownloadState.Paused);
                await JsUntil("document.querySelector('.monitor-actions button.primary').textContent==='Resume'&&!document.querySelector('.monitor-actions button.primary').disabled");
                await Click(".monitor-actions button.primary");await Until(()=>coordinator.Get(task.Id).State==DownloadState.Downloading);
                await Until(()=>coordinator.Get(task.Id).BytesPerSecond>0);await JsUntil("Number(document.querySelector('.activity').dataset.maxSpeed)>0");
                Check(window.Width<=400&&window.Height>300&&window.Height<600,"Expanded monitor size is outside its compact target.");
                await JsUntil("document.documentElement.scrollHeight<=innerHeight+1&&document.documentElement.scrollWidth<=innerWidth+1");await Capture("expanded.png");
                var before=updates;await Click("#minBtn");await Until(()=>window.Height<220);await Until(()=>updates>=before+10);
                Check(await core.ExecuteScriptAsync("!!document.querySelector('.monitor.collapsed')&&document.querySelector('#minBtn').getAttribute('aria-expanded')==='false'&&document.querySelector('.compact-file h1').textContent==='input-regression.bin'")=="true","Progress updates lost the collapsed monitor or its file identity.");await Capture("mini.png");
                await Click(".compact-pause");await Until(()=>coordinator.Get(task.Id).State==DownloadState.Paused);await JsUntil("!document.querySelector('.compact-pause').disabled&&document.querySelector('.compact-pause').getAttribute('aria-label').startsWith('Resume ')");
                await Click(".compact-pause");await Until(()=>coordinator.Get(task.Id).State==DownloadState.Downloading);
                await Click("#minBtn");await Until(()=>window.Height>300);
                ((FrameworkElement)view).RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));await Task.Delay(250);
                await Click(".monitor-actions button:last-child");await Until(()=>managers==1);
                await Click(".close-button");await Until(()=>!window.IsVisible);
                Check(coordinator.Get(task.Id).State==DownloadState.Downloading,"Closing the monitor stopped its transfer.");
                Check(pauses==2&&resumes==2&&dismissed==1,"A hit-tested action was lost or duplicated.");
                Report(); code = 0; Console.WriteLine("PASS floating input: caption regions, hit-tested pause/resume, collapse/expand during updates, manager and close.");
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
