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
namespace Syphon.Desktop.SmokeTests;
internal static class FloatingInputSmokeTests
{
    public static int Run(string[] args)
    {
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var output=Path.GetFullPath(args.FirstOrDefault(a=>a!="--live")??"artifacts/floating-input");Directory.CreateDirectory(output);
        var tasks=Enumerable.Range(0,6).Select(i=>new StoredDownload {Id=Guid.NewGuid(),Url="https://example.test/download",Destination=Path.Combine(output,$"download-{i}.bin"),PartPath="unused.part",State=DownloadState.Downloading,Received=25,Total=100,BytesPerSecond=1000,Connections=2}).ToArray();
        var pauses=new List<Guid>();var resumes=new List<Guid>();var cancels=new List<Guid>();var managers=0;var dismissed=new List<Guid>();
        FloatingDownloadWindow? window=null;int code=1;bool started=false;
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(100)};
        async void Start(object? sender,EventArgs args2){if(started)return;started=true;
            try{
                window=new FloatingDownloadWindow(id=>{pauses.Add(id);var i=Array.FindIndex(tasks,t=>t.Id==id);tasks[i]=tasks[i] with {State=DownloadState.Paused};return Task.CompletedTask;},id=>{resumes.Add(id);var i=Array.FindIndex(tasks,t=>t.Id==id);tasks[i]=tasks[i] with {State=DownloadState.Downloading};},id=>{cancels.Add(id);return Task.CompletedTask;},()=>managers++,id=>dismissed.Add(id),true,Path.Combine(output,"geometry.json")){Left=120,Top=120,Width=480,Height=700};
                window.Update(tasks[0],tasks);window.Show();timer.Tick+=(_,_)=>window.Update(tasks[0],tasks);timer.Start();
                var view=((Grid)window.Content).Children.OfType<IWebView2>().Single();await Until(()=>Task.FromResult(view.CoreWebView2!=null));var core=view.CoreWebView2;
                async Task JsUntil(string predicate)=>await Until(async()=>await core.ExecuteScriptAsync(predicate)=="true");
                async Task Click(string selector){using var point=JsonDocument.Parse(await core.ExecuteScriptAsync($"(()=>{{const r=document.querySelector({JsonSerializer.Serialize(selector)}).getBoundingClientRect();return {{x:r.x+r.width/2,y:r.y+r.height/2}};}})()"));foreach(var type in new[]{"mousePressed","mouseReleased"})await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",JsonSerializer.Serialize(new{type,x=point.RootElement.GetProperty("x").GetDouble(),y=point.RootElement.GetProperty("y").GetDouble(),button="left",clickCount=1}));}
                await JsUntil("document.querySelectorAll('.row').length===6");
                await using(var compact=File.Create(Path.Combine(output,"shared-compact.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,compact);
                Check(!window.AllowsTransparency&&view is WebView2&&core.Settings.IsNonClientRegionSupportEnabled,"Opaque native browser input/caption surface regressed.");
                await JsUntil("document.querySelector('#rows').scrollHeight>document.querySelector('#rows').clientHeight");
                Check(await core.ExecuteScriptAsync("document.querySelector('#rows').clientHeight<=448&&document.querySelectorAll('.controls button').length===12")=="true","Compact four-row overflow or independent controls are missing.");
                Check(await core.ExecuteScriptAsync("getComputedStyle(document.querySelector('#titlebar')).getPropertyValue('app-region')==='drag'&&[...document.querySelectorAll('button')].every(b=>getComputedStyle(b).getPropertyValue('app-region')==='no-drag')")=="true","Caption hit-testing overlaps actions.");
                await Click(".row:nth-child(2) .controls button");await Until(()=>Task.FromResult(pauses.Count==1));Check(pauses[0]==tasks[1].Id,"Row control targeted the wrong download.");
                await JsUntil("document.querySelector('.row:nth-child(2) .controls button').textContent==='Resume'");await Click(".row:nth-child(2) .controls button");await Until(()=>Task.FromResult(resumes.Count==1));await JsUntil("document.querySelectorAll('.row .controls button:first-child').length===6&&[...document.querySelectorAll('.row .controls button:first-child')].every(b=>b.textContent==='Pause'&&!b.disabled)");
                await Click("#pauseAll");await Until(()=>Task.FromResult(pauses.Count==7));await JsUntil("[...document.querySelectorAll('.row .controls button:first-child')].every(b=>b.textContent==='Resume')");await Click("#resumeAll");await Until(()=>Task.FromResult(resumes.Count==7));
                await Click("#minBtn");await JsUntil("document.querySelector('.win').classList.contains('expanded')");await Task.Delay(350);Check(await core.ExecuteScriptAsync("document.querySelector('#minBtn').getAttribute('aria-expanded')==='true'")=="true","Updates reset expanded state.");
                window.Width=750;window.Height=680;await Task.Delay(250);Check(Math.Abs(window.Width-750)<1&&Math.Abs(window.Height-680)<1,$"Browser layout reset manual resize: {window.Width}×{window.Height}.");
                await using(var image=File.Create(Path.Combine(output,"shared-expanded.png")))await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
                await Click(".row:nth-child(3) .controls button:nth-child(2)");await Until(()=>Task.FromResult(cancels.Count==1));Check(cancels[0]==tasks[2].Id,"Cancel targeted the wrong task.");
                await Click("#managerBtn");await Until(()=>Task.FromResult(managers==1));await Click("#closeBtn");await Until(()=>Task.FromResult(!window.IsVisible));Check(dismissed.Distinct().Count()==6,"Shared close did not dismiss the visible tasks.");
                var geometry=JsonSerializer.Deserialize<WindowGeometryStore>(File.ReadAllText(Path.Combine(output,"geometry.json")))!;Check(Math.Abs(geometry.Width-750)<1&&Math.Abs(geometry.Height-680)<1,"Resized geometry was not saved.");
                Console.WriteLine("PASS shared browser floating: native hit-tested independent controls, six rows, four-row overflow, pause/resume all, expanded state, resize, saved geometry, manager and close.");code=0;
            }catch(Exception ex){Console.Error.WriteLine(ex);}finally{timer.Stop();window?.Close();app.Shutdown();}
        }
        app.Startup+=Start;app.Run();return code;
    }
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static async Task Until(Func<Task<bool>> condition){var stop=DateTime.UtcNow.AddSeconds(30);while(DateTime.UtcNow<stop){if(await condition())return;await Task.Delay(50);}throw new TimeoutException("Floating UI did not reach expected state.");}
}
