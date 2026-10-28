using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;
using Syphon.TestServer;

namespace Syphon.Desktop.SmokeTests;
internal static class Beta20LiveSmokeTests
{
    public static int Run(string[] args)
    {
        var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/beta20-rebuilt");Directory.CreateDirectory(output);
        var directory=Path.Combine(Path.GetTempPath(),"Syphon.Beta20",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"download-preferences.json"),"{\"ShowFloatingWindow\":false,\"CloseToTray\":false}");
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var result=1;
        app.Startup+=async(_,_)=>{
            var window=new MainWindow(directory,"Syphon.Beta20."+Guid.NewGuid().ToString("N")){Left=-10000,Top=-10000,ShowInTaskbar=false};
            try{
                await using var server=await FixtureServer.StartAsync();window.Show();await Until(()=>window.IsReady&&window.HtmlSurface?.IsReady==true);
                Check(Math.Abs(window.Width-760)<1&&Math.Abs(window.Height-580)<1,"Main window does not match the calibrated 760×580 dimensions.");
                var ui=window.HtmlSurface!;await JsUntil(ui,"!!document.querySelector('.shell.v18 .rail .add')");
                Check(await ui.EvaluateAsync("!document.querySelector('.app-frame,#reference-style,.beta20-rail')")=="true","Legacy layout is still loaded.");
                var corner=OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)?"\"32px\"":"\"28px\"";
                Check(await ui.EvaluateAsync("getComputedStyle(document.querySelector('.shell')).borderTopLeftRadius")==corner,"Rounded outer frame is missing.");
                Check(await ui.EvaluateAsync("!document.querySelector('.rail [aria-label=Downloads]') && document.querySelectorAll('.rail .nav-button').length===2")=="true","Current page navigation should be omitted.");
                Check(await ui.EvaluateAsync("getComputedStyle(document.querySelector('.pixel-logo .orbital-mark')).width")=="\"64px\"","Main logo sizing was not applied.");
                await ui.CaptureAsync(Path.Combine(output,"main-empty.png"));
                var coordinator=(DownloadCoordinator)typeof(MainWindow).GetField("coordinator",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                var task=coordinator.Add(new Uri(server.BaseAddress,"resume?size=16777216"),Path.Combine(directory,"Coastal landscapes.bin"));coordinator.SetSpeedLimit(task.Id,512*1024);await coordinator.PauseAsync(task.Id);
                await JsUntil(ui,"document.querySelectorAll('.transfer').length===1&&document.querySelector('.transfer .pause').getAttribute('aria-label').startsWith('Resume')");
                await ui.EvaluateAsync("document.querySelector('.transfer .pause').click()");await Until(()=>coordinator.Get(task.Id).State==DownloadState.Downloading);await Task.Delay(350);
                await ui.EvaluateAsync("document.querySelector('.transfer .pause').click()");await Until(()=>coordinator.Get(task.Id).State==DownloadState.Paused);
                await ui.CaptureAsync(Path.Combine(output,"main-paused.png"));
                await ui.EvaluateAsync("document.querySelector('.more-actions').click()");await JsUntil(ui,"!!document.querySelector('.task-menu')");await ui.EvaluateAsync("[...document.querySelectorAll('.task-menu button')].find(b=>b.textContent==='Speed limit').click()");
                await JsUntil(ui,"!!document.querySelector('.speed-window')");await Fill(ui,"task-speed-limit","1024");await ui.CaptureAsync(Path.Combine(output,"speed-limit.png"));await ui.EvaluateAsync("document.querySelector('[aria-label=\"Apply download speed limit\"]').click()");await Until(()=>coordinator.Get(task.Id).SpeedLimitBytesPerSecond==1048576);
                await ui.EvaluateAsync("document.querySelector('.rail .add').click()");await JsUntil(ui,"!!document.querySelector('.menu')");await ui.EvaluateAsync("[...document.querySelectorAll('.menu button')].find(b=>b.textContent==='Paste link').click()");
                await Until(()=>app.Windows.OfType<DownloadOptionsDialog>().Any());var dialog=app.Windows.OfType<DownloadOptionsDialog>().Single();await Until(()=>dialog.HtmlSurface?.IsReady==true);
                await JsUntil(dialog.HtmlSurface!,"!!document.querySelector('.dialog-window .intro h1')");await dialog.HtmlSurface!.CaptureAsync(Path.Combine(output,"new-download.png"));await dialog.HtmlSurface.EvaluateAsync("document.querySelector('.dialog-chrome .close').click()");await Until(()=>!dialog.IsVisible);
                await ui.EvaluateAsync("document.querySelector('.more-actions').click()");await JsUntil(ui,"!!document.querySelector('.task-menu')");await ui.EvaluateAsync("[...document.querySelectorAll('.task-menu button')].find(b=>b.textContent==='Floating window').click()");await Until(()=>app.Windows.OfType<FloatingDownloadWindow>().Any());
                var floating=app.Windows.OfType<FloatingDownloadWindow>().Single();var field=typeof(FloatingDownloadWindow).GetField("_view",BindingFlags.Instance|BindingFlags.NonPublic)!;IWebView2 view=(IWebView2)field.GetValue(floating)!;
                await Until(()=>view.CoreWebView2 is not null);await ScriptUntil(()=>view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.monitor.expanded')"));
                using(var file=File.Create(Path.Combine(output,"floating-single-expanded.png")))await view.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,file);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"Collapse monitor\"]').click()");await ScriptUntil(()=>view.CoreWebView2.ExecuteScriptAsync("!!document.querySelector('.monitor.collapsed')"));
                using(var file=File.Create(Path.Combine(output,"floating-single-collapsed.png")))await view.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,file);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"Close monitor\"]').click()");await Until(()=>!floating.IsVisible);floating.Close();
                var second=coordinator.Add(new Uri(server.BaseAddress,"resume?size=1048576"),Path.Combine(directory,"Second.bin"));
                await ui.EvaluateAsync("window.chrome.webview.postMessage({action:'floating',id:"+JsonSerializer.Serialize(task.Id.ToString())+"})");await Until(()=>app.Windows.OfType<FloatingDownloadWindow>().Any());floating=app.Windows.OfType<FloatingDownloadWindow>().Single();view=(IWebView2)field.GetValue(floating)!;await Until(()=>view.CoreWebView2 is not null);await ScriptUntil(()=>view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.mini-row').length===2"));
                using(var file=File.Create(Path.Combine(output,"floating-multiple.png")))await view.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,file);
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"Expand monitor\"]').click()");await ScriptUntil(()=>view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.multi-expanded .transfer-row').length===2"));
                using(var file=File.Create(Path.Combine(output,"floating-multiple-expanded.png")))await view.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,file);
                var wasPinned=floating.Topmost;await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('.title-controls button').click()");await Task.Delay(500);Check(floating.Topmost!=wasPinned,"Pin was overwritten by a model refresh.");
                await view.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.row-select')[1].click()");await Task.Delay(500);Check(floating.TaskId==second.Id,"Floating selection was overwritten by a model refresh.");
                floating.Close();coordinator.SetSpeedLimit(task.Id,0);coordinator.Resume(task.Id);await coordinator.WaitForIdleAsync(task.Id);Check(coordinator.Get(task.Id).State==DownloadState.Completed,"Real transfer failed.");Check(new FileInfo(task.Destination).Length==16777216,"Completed file length is wrong.");
                await ui.EvaluateAsync("document.querySelector('.rail [aria-label=History]').click()");await JsUntil(ui,"document.querySelectorAll('.history-record').length===1");await ui.EvaluateAsync("document.querySelector('.record-select').click()");await JsUntil(ui,"!!document.querySelector('.detail-window')");
                await ui.CaptureAsync(Path.Combine(output,"download-details.png"));await ui.EvaluateAsync("[...document.querySelectorAll('.detail-section summary')].find(e=>e.textContent==='File integrity').click();document.querySelector('#verify-file').click()");await JsUntil(ui,"document.querySelector('#verification-result')?.textContent.includes('Current SHA-256')");
                await ui.EvaluateAsync("document.querySelector('.detail-window .dialog-chrome .close').click();document.querySelector('.head-action').click()");await JsUntil(ui,"!!document.querySelector('#confirm-clear-downloads')");await ui.EvaluateAsync("document.querySelector('#confirm-clear-downloads').click()");await Until(()=>!coordinator.GetTasks().Any(t=>t.Id==task.Id));Check(File.Exists(task.Destination),"Clear history removed a completed file.");Check(coordinator.GetTasks().Any(t=>t.Id==second.Id),"Clear history removed a paused task.");
                await ui.EvaluateAsync("document.querySelector('.rail [aria-label=Settings]').click()");await JsUntil(ui,"!!document.querySelector('.settings-form')");
                foreach(var section in new[]{"Downloads","Network","Queue","Browser Extension","Media","Appearance","About"}){
                    await ui.EvaluateAsync("[...document.querySelectorAll('.settings-nav button')].find(b=>b.textContent==="+JsonSerializer.Serialize(section)+").click()");await ui.CaptureAsync(Path.Combine(output,"settings-"+section.Replace(' ','-')+".png"));
                }
                await ui.EvaluateAsync("window.chrome.webview.postMessage({action:'browser-setup'})");await Until(()=>app.Windows.Cast<Window>().Any(w=>w.Title=="Connect your browser"));
                var guide=app.Windows.Cast<Window>().Single(w=>w.Title=="Connect your browser");var guideUi=((Grid)guide.Content).Children.OfType<LocalHtmlSurface>().Single();await Until(()=>guideUi.IsReady);await JsUntil(guideUi,"!!document.querySelector('.connection-steps')");await guideUi.CaptureAsync(Path.Combine(output,"browser-setup.png"));guide.Close();
                foreach(var confirmation in new[]{"restart","remove"}){
                    await ui.EvaluateAsync("window.chrome.webview.postMessage({action:"+JsonSerializer.Serialize(confirmation)+",id:"+JsonSerializer.Serialize(second.Id.ToString())+"})");
                    await Until(()=>app.Windows.Cast<Window>().Any(w=>w.Title==(confirmation=="restart"?"Restart download":"Remove download")));
                    var confirmWindow=app.Windows.Cast<Window>().Single(w=>w.Title==(confirmation=="restart"?"Restart download":"Remove download"));var confirmUi=((Grid)confirmWindow.Content).Children.OfType<LocalHtmlSurface>().Single();await Until(()=>confirmUi.IsReady);await JsUntil(confirmUi,"!!document.querySelector('#confirm-action')");await confirmUi.CaptureAsync(Path.Combine(output,confirmation+"-download.png"));await confirmUi.EvaluateAsync("document.querySelector('.dialog-chrome .close').click()");await Until(()=>!confirmWindow.IsVisible);
                }
                Check(await ui.EvaluateAsync("!window.__syphonError")=="true","Frontend exception: "+await ui.EvaluateAsync("window.__syphonError"));
                result=0;File.WriteAllText(Path.Combine(output,"report.json"),"{\"passed\":true,\"newLayouts\":true,\"realPauseResume\":true,\"speedLimit\":true,\"newDialog\":true,\"floatingStates\":true,\"realDownloadBytes\":true,\"fileVerification\":true,\"historyClearPreservesFilesAndPausedTasks\":true,\"settingsSections\":7}");
                Console.WriteLine("PASS rebuilt Beta 20 live UI, native commands, transfer bytes, verification, clear history and floating states.");
            }catch(Exception ex){Console.Error.WriteLine(ex);File.WriteAllText(Path.Combine(output,"error.txt"),ex.ToString());if(window.HtmlSurface?.IsReady==true){Console.Error.WriteLine(await window.HtmlSurface.EvaluateAsync("window.__syphonError||document.body.innerText"));await window.HtmlSurface.CaptureAsync(Path.Combine(output,"failure.png"));}}
            finally{foreach(var popup in app.Windows.Cast<Window>().Where(w=>w!=window).ToArray())popup.Close();window.Close();await Task.Delay(300);app.Shutdown();}
        };app.Run();return result;
    }
    private static async Task Until(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(30);while(!condition()){if(DateTime.UtcNow>end)throw new TimeoutException("Native UI condition timed out.");await Task.Delay(30);}}
    private static async Task ScriptUntil(Func<Task<string>> evaluate){var end=DateTime.UtcNow.AddSeconds(30);while(await evaluate()!="true"){if(DateTime.UtcNow>end)throw new TimeoutException("Floating UI condition timed out.");await Task.Delay(40);}}
    private static Task JsUntil(LocalHtmlSurface ui,string code)=>ScriptUntil(()=>ui.EvaluateAsync(code));
    private static Task Fill(LocalHtmlSurface ui,string id,string value)=>ui.EvaluateAsync("(()=>{const e=document.getElementById("+JsonSerializer.Serialize(id)+");Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,"+JsonSerializer.Serialize(value)+");e.dispatchEvent(new Event('input',{bubbles:true}));})()");
    private static void Check(bool result,string message){if(!result)throw new InvalidOperationException(message);}
}
