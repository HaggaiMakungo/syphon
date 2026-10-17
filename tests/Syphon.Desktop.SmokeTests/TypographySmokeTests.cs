using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;

namespace Syphon.Desktop.SmokeTests;

internal static class TypographySmokeTests
{
    public static int Run(string[] args) {
        var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/typography-comparison");Directory.CreateDirectory(output);
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var code=1;
        app.Startup+=async(_,_)=>{try{await CheckAsync(output);Console.WriteLine("PASS typography: three presets, live main/dialog/floating propagation, persistence, restore, all requested dimensions and both History densities.");code=0;}catch(Exception ex){Console.Error.WriteLine(ex);}finally{app.Shutdown(code);}};
        app.Run();return code;
    }
    private static async Task Until(Func<Task<bool>> predicate,string label) {var end=DateTime.UtcNow.AddSeconds(30);while(!await predicate()){if(DateTime.UtcNow>end)throw new Exception(label);await Task.Delay(30);}}
    private static async Task Ready(LocalHtmlSurface ui)=>await Until(()=>Task.FromResult(ui.IsReady),ui.InitializationError??"Frontend startup timed out.");
    private static async Task Check(LocalHtmlSurface ui,string script,string label) {if(await ui.EvaluateAsync(script)!="true")throw new Exception(label+": "+await ui.EvaluateAsync("window.__syphonError||document.body.innerText"));}
    private static async Task Settings(LocalHtmlSurface ui) {
        await ui.EvaluateAsync("document.querySelector('[aria-label=Settings]').click()");
        await Until(async()=>await ui.EvaluateAsync("!!document.querySelector('[aria-label=Appearance]')")=="true","Appearance navigation missing");
        await ui.EvaluateAsync("document.querySelector('[aria-label=Appearance]').click()");
        await Until(async()=>await ui.EvaluateAsync("!!document.querySelector('#interface-typography')")=="true","Typography selector missing");
    }
    private static async Task CheckAsync(string output) {
        var folder=Path.Combine(Path.GetTempPath(),"Syphon.Typography",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var date=new DateTimeOffset(2026,10,17,12,0,0,TimeSpan.Zero);
        var name="A very long filename — "+new string('w',100)+".mp4";
        var sample=new StoredDownload { Url="https://downloads.example.test/a/long/video?quality=original",Destination=Path.Combine(folder,name),PartPath=Path.Combine(folder,"scratch.part"),State=DownloadState.Paused,Total=123456789,CreatedAt=date,MediaDurationSeconds=7132.917 };
        using(var store=(IDisposable)Activator.CreateInstance(typeof(DownloadCoordinator).Assembly.GetType("Syphon.Engine.DownloadStore")!,folder)!) {
            void Save(StoredDownload value) => store.GetType().GetMethod("Save")!.Invoke(store,[value]);
            Save(sample);
            foreach(var state in new[]{DownloadState.Completed,DownloadState.Cancelled,DownloadState.Failed})Save(sample with {Id=Guid.NewGuid(),State=state,Destination=Path.Combine(folder,state+" — "+name),PartPath=Path.Combine(folder,Guid.NewGuid()+".part"),FinishedAt=date,Received=state==DownloadState.Completed?123456789:654321,Committed=0,Sha256=state==DownloadState.Completed?new string('a',64):null,Message=state==DownloadState.Failed?"The source connection ended. Retry when it is available.":"Fixture outcome"});
        }
        MainWindow? window=null;DownloadOptionsDialog? dialog=null;FloatingDownloadWindow? floating=null;
        var shots=new List<object>();
        try {
            window=new MainWindow(folder,"Syphon.Typography."+Guid.NewGuid().ToString("N")){Left=-10000,Top=-10000,ShowInTaskbar=false,MinHeight=0};window.Show();
            var ui=window.HtmlSurface!;await Ready(ui);await Until(()=>Task.FromResult(window.IsReady),"Saved history startup timed out");
            dialog=new DownloadOptionsDialog("Typography comparison",name,folder,"Original video",123456789,false,"https://downloads.example.test/very/long/path/video.mp4?quality=original",allowUrlEditing:true){Left=-10000,Top=-10000,ShowInTaskbar=false};dialog.Show();await Ready(dialog.HtmlSurface!);
            floating=new FloatingDownloadWindow(_=>Task.CompletedTask,_=>{},_=>Task.CompletedTask,()=>{},_=>{}){Left=-10000,Top=-10000,ShowInTaskbar=false,Topmost=false};floating.Show();floating.Update(sample with {State=DownloadState.Downloading,Received=12345678,BytesPerSecond=3456789},[sample]);
            var view=((Grid)floating.Content).Children.OfType<IWebView2>().Single();
            await Until(()=>Task.FromResult(view.CoreWebView2!=null),"Floating frontend startup timed out");
            foreach(var preset in new[]{"inter","plex","hybrid"}) {
                await Settings(ui);
                await ui.EvaluateAsync($"document.querySelector('#interface-typography').value='{preset}';document.querySelector('#interface-typography').dispatchEvent(new Event('change',{{bubbles:true}}))");
                await Until(()=>Task.FromResult(File.Exists(Path.Combine(folder,"download-preferences.json"))&&File.ReadAllText(Path.Combine(folder,"download-preferences.json")).Contains("\"TypographyPreset\":\""+preset+"\"")),"Preset was not persisted");
                foreach(var surface in new[]{ui,dialog.HtmlSurface!})await Until(async()=>await surface.EvaluateAsync($"document.documentElement.dataset.typography==='{preset}'&&document.fonts.status==='loaded'")=="true","Font did not update live");
                await Until(async()=>await view.CoreWebView2.ExecuteScriptAsync($"document.documentElement.dataset.typography==='{preset}'&&document.fonts.status==='loaded'")=="true","Floating preset did not update live");
                if(preset!="inter")await Check(ui,$"[...document.fonts].some(f=>f.family.includes('{(preset=="plex"?"IBM Plex Sans":"Space Grotesk")}')&&f.status==='loaded')","Bundled comparison font did not load");
                await ui.EvaluateAsync("document.querySelector('#history-density-settings').value='comfortable';document.querySelector('#history-density-settings').dispatchEvent(new Event('change',{bubbles:true}))");
                window.Width=1440/System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;window.Height=900/System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleY;
                await Check(ui,"(()=>{const b=document.querySelector('.typography-restore');return getComputedStyle(b).whiteSpace==='nowrap'&&b.scrollWidth<=b.clientWidth+1;})()","Restore button wrapping");
                Directory.CreateDirectory(Path.Combine(output,preset));await Task.Delay(80);
                await ui.CaptureAsync(Path.Combine(output,preset,"appearance.png"));
                var family=preset=="plex"?"IBM Plex Sans":"Inter";
                await Check(ui,$"getComputedStyle(document.body).fontFamily.includes('{family}')","Incorrect UI family");
                await Check(dialog.HtmlSurface!,$"getComputedStyle(document.querySelector('#download-filename')).fontFamily.includes('{family}')","Dialog field uses wrong font");
                foreach(var (width,height) in new[]{(1280,720),(1440,900),(1650,927)}) {
                    var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);
                    window.Width=width/dpi.DpiScaleX;window.Height=height/dpi.DpiScaleY;dialog.MaxHeight=height/dpi.DpiScaleY-24;await Task.Delay(80);
                    var target=Path.Combine(output,preset,$"{width}x{height}");Directory.CreateDirectory(target);
                    await ui.EvaluateAsync("document.querySelector('[aria-label=Downloads]').click()");await Until(async()=>await ui.EvaluateAsync("!!document.querySelector('.summary-value')")=="true","Downloads did not render");
                    await Until(async()=>await ui.EvaluateAsync("document.fonts.status==='loaded'")=="true","Font loading did not finish");
                    await Check(ui,$"getComputedStyle(document.querySelector('.summary-value')).fontFamily.includes('{family}')","Statistics changed to a display font");
                    await Check(ui,$"getComputedStyle(document.querySelector('.window-brand')).fontFamily.includes('{(preset=="hybrid"?"Space Grotesk":family)}')","Brand family incorrect");
                    await Check(ui,"document.documentElement.scrollWidth<=innerWidth+1&&[...document.querySelectorAll('.queue-button,.chrome-new')].filter(e=>e.getBoundingClientRect().width>0).every(e=>e.scrollWidth<=e.clientWidth+1)","Downloads controls overflow");
                    await ui.CaptureAsync(Path.Combine(target,"downloads.png"));
                    await ui.EvaluateAsync("document.querySelector('[aria-label=History]').click()");await Until(async()=>await ui.EvaluateAsync("document.querySelectorAll('.history-row').length===3")=="true","History rows missing");
                    await ui.EvaluateAsync("document.querySelector('.history-row').click()");await Until(async()=>await ui.EvaluateAsync("document.querySelector('.history-row').dataset.selected==='true'")=="true","History selection did not settle");
                    foreach(var density in new[]{"comfortable","compact"}) {
                        await ui.EvaluateAsync($"document.querySelector('#history-density').value='{density}';document.querySelector('#history-density').dispatchEvent(new Event('change',{{bubbles:true}}))");await Task.Delay(80);
                        await Check(ui,"document.documentElement.scrollWidth<=innerWidth+1&&[...document.querySelectorAll('.history-row')].every(e=>e.scrollWidth<=e.clientWidth+1)&&[...document.querySelectorAll('.history-filename')].every(e=>e.title===e.textContent&&getComputedStyle(e).textOverflow==='ellipsis')","History overflow/truncation");
                        await ui.CaptureAsync(Path.Combine(target,"history-"+density+".png"));
                    }
                    await ui.EvaluateAsync("document.querySelector('.history-filename').click()");await Until(async()=>await ui.EvaluateAsync("!!document.querySelector('.download-details')")=="true","Details did not open");
                    await Check(ui,"(()=>{const r=document.querySelector('.download-details').getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight+1;})()","Details overflows viewport");
                    await ui.CaptureAsync(Path.Combine(target,"details.png"));await ui.EvaluateAsync("document.querySelector('#details-header-close').click()");
                    await Check(dialog.HtmlSurface!,"document.documentElement.scrollWidth<=innerWidth+1&&[...document.querySelectorAll('button')].filter(e=>e.getBoundingClientRect().width>0).every(e=>e.scrollWidth<=e.clientWidth+1)","New Download button overflow");
                    await dialog.HtmlSurface!.CaptureAsync(Path.Combine(target,"new-download.png"));
                    await using(var file=File.Create(Path.Combine(target,"floating.png")))await view.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png,file);
                    shots.Add(new{preset,width,height,pages=6,actualWidth=window.ActualWidth,actualHeight=window.ActualHeight,dpi=dpi.DpiScaleX,viewport=await ui.EvaluateAsync("JSON.stringify({width:innerWidth,height:innerHeight})")});
                }
            }
            dialog.Close();dialog=null;floating.Close();floating=null;window.Close();window=null;
            window=new MainWindow(folder,"Syphon.Typography."+Guid.NewGuid().ToString("N")){Left=-10000,Top=-10000,ShowInTaskbar=false};window.Show();await Ready(window.HtmlSurface!);
            await Until(async()=>await window.HtmlSurface!.EvaluateAsync("document.documentElement.dataset.typography==='hybrid'")=="true","Preset did not survive reopening");
            await Settings(window.HtmlSurface!);await window.HtmlSurface!.EvaluateAsync("document.querySelector('[aria-label=\"Restore original Inter typography\"]').click()");
            await Until(async()=>await window.HtmlSurface!.EvaluateAsync("document.documentElement.dataset.typography==='inter'")=="true","Restore Inter failed");
            await File.WriteAllTextAsync(Path.Combine(output,"validation.json"),JsonSerializer.Serialize(new{passed=true,shots},new JsonSerializerOptions{WriteIndented=true}));
        }finally{dialog?.Close();floating?.Close();window?.Close();}
    }
}
