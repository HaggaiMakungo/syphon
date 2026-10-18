using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Windows;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;
using Syphon.TestServer;

namespace Syphon.Desktop.SmokeTests;

internal static class FolderSmokeTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "Syphon.Folder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown; app.MainWindow = new Window();
        var code = 1;
        app.Startup += async (_, _) => {
            MainWindow? window = null;
            try {
                await using var server = await FixtureServer.StartAsync();
                var destination = Path.Combine(root, "Music, fixtures", "Overcome - Tricky - YouTube (2).bin");
                Guid id;
                await using (var owner = await DownloadCoordinator.OpenAsync(Path.Combine(root, "store"))) {
                    var task = owner.Add(new Uri(server.BaseAddress, "file?size=100003"), destination); id = task.Id;
                    owner.Resume(id); await owner.WaitForIdleAsync(id);
                    if (owner.Get(id).State != DownloadState.Completed) throw new Exception(owner.Get(id).Message);
                }
                var launches = new List<ProcessStartInfo>();
                window = new MainWindow(Path.Combine(root, "store")) { Left = -10000, Top = -10000, ShowInTaskbar = false };
                typeof(MainWindow).GetProperty("FolderProcessStarter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, (Action<ProcessStartInfo>)launches.Add);
                window.Show();
                var ui = window.HtmlSurface!;
                async Task Until(string expression) {
                    var end = DateTime.UtcNow.AddSeconds(20);
                    while (!ui.IsReady || await ui.EvaluateAsync(expression) != "true") {
                        if (ui.InitializationError is not null || DateTime.UtcNow > end) throw new Exception(ui.InitializationError ?? expression + " " + await ui.EvaluateAsync("JSON.stringify({error:window.__syphonError,text:document.body.innerText.slice(-600)})"));
                        await Task.Delay(40);
                    }
                }
                await Until("!document.querySelector('#startup-status')&&!!document.querySelector('[aria-label=History]')");
                await ui.EvaluateAsync("document.querySelector('[aria-label=History]').click()");
                await Until("!!document.querySelector('.history-row')");
                await ui.EvaluateAsync("document.querySelector('.history-row').click()");
                await ui.EvaluateAsync("if(document.querySelector('#history-pane-toggle').getAttribute('aria-expanded')!=='true')document.querySelector('#history-pane-toggle').click()");
                await Until("!![...document.querySelectorAll('#history-inspector button')].find(b=>b.textContent==='Open folder')");
                await ui.EvaluateAsync("[...document.querySelectorAll('#history-inspector button')].find(b=>b.textContent==='Open folder').click()");
                async Task Launched(int count) {
                    var deadline = DateTime.UtcNow.AddSeconds(5);
                    while (launches.Count < count && DateTime.UtcNow < deadline) await Task.Delay(40);
                    if (launches.Count != count) throw new Exception("Open folder did not reach the shell.");
                    var launch = launches[^1];
                    if (launch.FileName != Path.GetDirectoryName(destination) || !launch.UseShellExecute || launch.Verb != "open" || launch.Arguments.Length != 0)
                        throw new Exception("Open folder must ask the Windows shell to open the containing directory directly.");
                }
                await Launched(1);
                await ui.EvaluateAsync("document.querySelector('.history-actions-toggle').click()");
                await Until("!!document.querySelector('[data-history-action=folder]')");
                await ui.EvaluateAsync("document.querySelector('[data-history-action=folder]').click()");
                await Launched(2);
                await ui.EvaluateAsync("[...document.querySelectorAll('#history-inspector button')].find(b=>b.textContent==='Verify & troubleshoot').click()");
                await Until("!!document.querySelector('#history-details [aria-label=\"Open folder\"]')");
                await ui.EvaluateAsync("document.querySelector('#history-details [aria-label=\"Open folder\"]').click()");
                await Launched(3);
                File.Delete(destination);
                await ui.EvaluateAsync("document.querySelector('#history-details [aria-label=\"Open folder\"]').click()");
                await Launched(4);
                typeof(MainWindow).GetProperty("FolderProcessStarter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window,
                    (Action<ProcessStartInfo>)(_ => throw new Win32Exception(5)));
                await ui.EvaluateAsync("document.querySelector('#history-details [aria-label=\"Open folder\"]').click()");
                await Until("document.querySelector('.status-toast')?.textContent==='The folder could not be opened.'");
                typeof(MainWindow).GetProperty("FolderProcessStarter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, (Action<ProcessStartInfo>)launches.Add);
                await ui.EvaluateAsync("document.querySelector('#history-details [aria-label=\"Open folder\"]').click()");
                await Launched(5);
                await Until("!document.querySelector('.status-toast')");
                Directory.Delete(Path.GetDirectoryName(destination)!);
                await ui.EvaluateAsync("document.querySelector('#history-details [aria-label=\"Open folder\"]').click()");
                await Until("document.querySelector('.status-toast')?.textContent==='The download folder is no longer available at its saved location.'");
                if (launches.Count != 5) throw new Exception("Missing folder was launched.");
                Console.WriteLine("PASS folder: inspector, row menu and details use exact containing directory; missing file, missing folder, shell failure and successful retry handled.");
                code = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally {
                window?.Close();
                if (window is not null) { var end = DateTime.UtcNow.AddSeconds(10); while (window.IsVisible && DateTime.UtcNow < end) await Task.Delay(40); }
                Directory.Delete(root, true);
                app.Shutdown(code);
            }
        };
        app.Run(); return code;
    }
}
