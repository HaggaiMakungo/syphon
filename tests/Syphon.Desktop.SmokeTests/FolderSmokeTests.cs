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
                    var other=owner.Add(new Uri(server.BaseAddress, "file?size=100003"), Path.Combine(root,"other.mp4"));
                    owner.Resume(other.Id); await owner.WaitForIdleAsync(other.Id);
                }
                var launches = new List<ProcessStartInfo>();
                window = new MainWindow(Path.Combine(root, "store"),"Syphon.Folder."+Guid.NewGuid().ToString("N")) { Left = -10000, Top = -10000, ShowInTaskbar = false };
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
                await Until("!!document.querySelector('.history-record')");
                async Task Launched(int count) {
                    var deadline=DateTime.UtcNow.AddSeconds(5);
                    while(launches.Count<count&&DateTime.UtcNow<deadline)await Task.Delay(40);
                    if(launches.Count!=count)throw new Exception("Open folder did not reach the shell.");
                    var launch=launches[^1];
                    if(File.Exists(destination)) {
                        if(Path.GetFileName(launch.FileName)!="explorer.exe"||launch.Arguments!="/select,\""+destination+"\""||!launch.UseShellExecute)
                            throw new Exception("Explorer must select the exact downloaded file.");
                    } else if(launch.FileName!=Path.GetDirectoryName(destination)||launch.Arguments.Length!=0||!launch.UseShellExecute)
                        throw new Exception("Missing file must fall back to opening its containing folder.");
                }
                // A different native selection can be excluded by the hidden legacy view filter.
                var nativeList=(System.Windows.Controls.DataGrid)window.FindName("TaskList");
                var nativeView=System.Windows.Data.CollectionViewSource.GetDefaultView(nativeList.ItemsSource);
                nativeView.Filter=item=>item is DownloadRow row&&row.Snapshot.Id!=id;
                nativeList.SelectedIndex=0;
                await ui.EvaluateAsync("[...document.querySelectorAll('.record-folder')].find(b=>b.getAttribute('aria-label')==="+System.Text.Json.JsonSerializer.Serialize("Open folder for "+Path.GetFileName(destination))+").click()");await Launched(1);
                await ui.EvaluateAsync("[...document.querySelectorAll('.record-select')].find(b=>b.getAttribute('aria-label')==="+System.Text.Json.JsonSerializer.Serialize("Details for "+Path.GetFileName(destination))+").click()");
                await Until("!!document.querySelector('.detail-window')");
                const string open="[...document.querySelectorAll('.detail-window .dialog-actions button')].find(b=>b.textContent.includes('Open folder')).click()";
                await ui.EvaluateAsync(open);await Launched(2);
                File.Delete(destination);await ui.EvaluateAsync(open);await Launched(3);
                typeof(MainWindow).GetProperty("FolderProcessStarter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window,(Action<ProcessStartInfo>)(_=>throw new Win32Exception(5)));
                await ui.EvaluateAsync(open);await Until("document.body.textContent.includes('The folder could not be opened.')");
                typeof(MainWindow).GetProperty("FolderProcessStarter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window,(Action<ProcessStartInfo>)launches.Add);
                await ui.EvaluateAsync(open);await Launched(4);
                Directory.Delete(Path.GetDirectoryName(destination)!);await ui.EvaluateAsync(open);
                await Until("document.body.textContent.includes('The download folder is no longer available at its saved location.')");
                if(launches.Count!=4)throw new Exception("Missing folder was launched.");
                Console.WriteLine("PASS folder selection from history and details, quoted comma/space paths, missing file fallback, missing folder and shell failure.");
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
