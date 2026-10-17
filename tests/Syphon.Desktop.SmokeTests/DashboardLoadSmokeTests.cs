using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Syphon.BrowserBridge;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;
using Syphon.TestServer;

namespace Syphon.Desktop.SmokeTests;

internal static class DashboardLoadSmokeTests
{
    public static int Run(string[] args)
    {
        var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/dashboard-load");Directory.CreateDirectory(output);
        var directory=Path.Combine(Path.GetTempPath(),"Syphon.Dashboard.Load",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"download-preferences.json"),JsonSerializer.Serialize(new { ShowFloatingWindow=false, Transfer=new TransferOptions { MaximumConcurrentDownloads=2 } }));
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var pipe="Syphon.Load."+Guid.NewGuid().ToString("N");var window=new MainWindow(directory,pipe) {Left=-10000,Top=-10000,ShowInTaskbar=false};var code=1;
        window.Loaded+=async (_,_)=>{
            await using var fixture=await FixtureServer.StartAsync();
            var ui=window.HtmlSurface!;
            try {
                await Until(()=>ui.IsReady&&window.IsReady);await JsUntil(ui,"!!document.querySelector('#empty-new-download')&&!document.querySelector('#empty-new-download').disabled");
                await ui.CaptureAsync(Path.Combine(output,"empty.png"));
                await ui.EvaluateAsync("window.chrome.webview.addEventListener('message',e=>{if(e.data?.type==='model')window.testQuickResult=e.data.model.quickDownload;})");
                var source=new Uri(fixture.BaseAddress,"resume?size=536870912").AbsoluteUri;
                var blockedFolder=Path.Combine(directory,"not-a-folder.txt");File.WriteAllText(blockedFolder,"fixture");
                File.WriteAllText(Path.Combine(directory,"download-preferences.json"),JsonSerializer.Serialize(new { Folder=blockedFolder,ShowFloatingWindow=false,Transfer=new TransferOptions {MaximumConcurrentDownloads=2} }));
                await ui.EvaluateAsync($"window.chrome.webview.postMessage({{action:'quick-download',requestId:'{Guid.NewGuid()}',url:{JsonSerializer.Serialize(source)}}})");
                await JsUntil(ui,"!!window.testQuickResult?.error");
                Check(((DataGrid)window.FindName("TaskList")).Items.Count==0,"Failed quick add created a record before validation finished.");
                File.WriteAllText(Path.Combine(directory,"download-preferences.json"),JsonSerializer.Serialize(new {ShowFloatingWindow=false,Transfer=new TransferOptions {MaximumConcurrentDownloads=2} }));
                await ui.EvaluateAsync($"window.chrome.webview.postMessage({{action:'quick-download',requestId:'{Guid.NewGuid()}',url:{JsonSerializer.Serialize(source)}}})");
                var rows=(DataGrid)window.FindName("TaskList");await Until(()=>rows.Items.Count==1);await Task.Delay(400);
                Check(rows.Items.Count==1&&!app.Windows.OfType<DownloadOptionsDialog>().Any(),"Quick add must create one transfer without an options dialog.");
                var first=rows.Items.Cast<DownloadRow>().Single();
                await ui.EvaluateAsync($"window.chrome.webview.postMessage({{action:'quick-download',requestId:'{first.Snapshot.BrowserRequestId}',url:{JsonSerializer.Serialize(source)}}})");
                await Task.Delay(300);Check(rows.Items.Count==1,"A repeated quick-add receipt created a duplicate transfer.");
                var ids=new List<Guid>();
                for(var i=0;i<24;i++){
                    var title=i==0?"Project assets — paused transfer with a deliberately long filename to verify truncation at narrow window widths.zip":i==1?"Desktop image.iso":$"Project package {i+1:D2}.zip";
                    var reply=await BrowserPipeClient.SendAsync(pipe,new BrowserRequest(1,"download",Guid.NewGuid(),new Uri(fixture.BaseAddress,$"slow?size=536870912&record={i}").AbsoluteUri,Title:title),TimeSpan.FromSeconds(10));
                    Check(reply.Status=="accepted"&&reply.TaskId is not null,"Load fixture was rejected.");ids.Add(reply.TaskId!.Value);
                    if(i==0){await JsUntil(ui,$"!!document.querySelector('#tasks [data-id=\"{ids[0]}\"] [aria-label=Pause]')");await ui.EvaluateAsync($"document.querySelector('#tasks [data-id=\"{ids[0]}\"] [aria-label=Pause]').click()");await Until(()=>rows.Items.Cast<DownloadRow>().Single(t=>t.Snapshot.Id==ids[0]).State==DownloadState.Paused);}
                }
                await JsUntil(ui,"document.querySelectorAll('#tasks>[data-id]').length===25&&document.querySelector('#download-queue-state').textContent==='2 active · 22 waiting · 1 paused'");
                await ui.EvaluateAsync("if(document.querySelector('#activity-toggle').getAttribute('aria-expanded')==='true')document.querySelector('#activity-toggle').click()");
                await ui.EvaluateAsync($"document.querySelector('#tasks [data-id=\"{ids[0]}\"]').dispatchEvent(new MouseEvent('contextmenu',{{bubbles:true,cancelable:true}}))");
                await JsUntil(ui,$"!!document.querySelector('#tasks [data-id=\"{ids[0]}\"] [aria-label=\"High priority\"]')");
                await ui.EvaluateAsync($"document.querySelector('#tasks [data-id=\"{ids[0]}\"] [aria-label=\"High priority\"]').click()");
                await Until(()=>rows.Items.Cast<DownloadRow>().Single(t=>t.Snapshot.Id==ids[0]).Snapshot.Priority==1);
                await ui.EvaluateAsync($"document.querySelector('#tasks [data-id=\"{ids[0]}\"] [aria-label=\"Open folder\"]').focus()");
                await Task.Delay(650);Check(await ui.EvaluateAsync("document.activeElement.getAttribute('aria-label')==='Open folder'")=="true","Live updates displaced a focused queue action.");
                Check(await ui.EvaluateAsync("document.querySelector('#summary-speed').dataset.rate===document.querySelector('#manager-speed-chart').dataset.lastRate")=="true","Live summary and graph endpoint diverged.");
                await ui.CaptureAsync(Path.Combine(output,"mixed-queue.png"));
                await ui.EvaluateAsync($"document.querySelector('#tasks [data-id=\"{ids[0]}\"] [aria-label=\"More actions\"]').click()");
                await JsUntil(ui,"!document.querySelector('#tasks .action-menu')");
                await ui.EvaluateAsync("document.querySelector('#tasks').scrollTop=0");
                await ui.CaptureAsync(Path.Combine(output,"mixed-queue-overview.png"));
                window.Width=980;window.Height=720;await Task.Delay(250);
                Check(await ui.EvaluateAsync("(()=>{const list=document.querySelector('#tasks');return list.scrollHeight>list.clientHeight&&document.documentElement.scrollWidth<=innerWidth&&[...list.children].every(row=>row.scrollWidth<=row.clientWidth+1);})()")=="true","The populated queue must scroll without horizontal clipping.");
                await ui.CaptureAsync(Path.Combine(output,"mixed-queue-minimum.png"));
                await ui.EvaluateAsync("document.querySelector('#tasks').lastElementChild.scrollIntoView({block:'nearest'})");
                Check(await ui.EvaluateAsync("document.querySelector('#tasks').scrollTop>0&&document.querySelector('#queue-controls').getBoundingClientRect().top>=0")=="true","Scrolling the queue lost its controls.");
                await ui.CaptureAsync(Path.Combine(output,"queue-bottom.png"));
                Console.WriteLine("PASS dashboard: quick add, receipt deduplication, 25 real active/queued/paused transfers, priority, focus retention, long filenames, minimum size and scrolling.");code=0;
            } catch(Exception ex){Console.Error.WriteLine(ex);if(ui.IsReady){Console.Error.WriteLine(await ui.EvaluateAsync("window.__syphonError||document.body.innerText"));await ui.CaptureAsync(Path.Combine(output,"failure.png"));}}
            finally {window.Close();await Until(()=>!window.IsVisible);app.Shutdown(code);}
        };
        app.Run(window);return code;
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static async Task Until(Func<bool> condition){var deadline=DateTime.UtcNow.AddSeconds(30);while(!condition()){if(DateTime.UtcNow>deadline)throw new TimeoutException("Dashboard fixture timed out.");await Task.Delay(30);}}
    private static async Task JsUntil(LocalHtmlSurface ui,string predicate){var deadline=DateTime.UtcNow.AddSeconds(30);while(await ui.EvaluateAsync(predicate)!="true"){if(DateTime.UtcNow>deadline)throw new TimeoutException(predicate);await Task.Delay(50);}}
}
