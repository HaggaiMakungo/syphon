using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;

namespace Syphon.Desktop.SmokeTests;

internal static class DetailsSmokeTests
{
    public static int Run(string[] args) {
        var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/details-fixtures");Directory.CreateDirectory(output);
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var code=1;
        app.Startup+=async(_,_)=>{try{await RunAsync(output);Console.WriteLine("PASS details: missing component gates retry, trusted setup instructions, recheck and single retry.");code=0;}catch(Exception ex){Console.Error.WriteLine(ex);}finally{app.Shutdown(code);}};
        app.Run();return code;
    }
    public static async Task RunAsync(string output)
    {
        var task = new StoredDownload { Id=Guid.NewGuid(), Url="https://www.youtube.com/watch?v=aAZ8qx43FT4", Destination=@"C:\Downloads\The Hunger (1983) - Do You Love Me.mp4", PartPath=@"C:\Downloads\.fixture.part", State=DownloadState.Failed, MediaFormat="mp4", Message="YouTube support needs yt-dlp. Run install-youtube.ps1 in the Syphon folder.", StartedAt=DateTimeOffset.UtcNow.AddHours(-2), FinishedAt=DateTimeOffset.UtcNow, ActiveSeconds=39 };
        var record=JsonSerializer.SerializeToNode(new { id=task.Id,filename=task.Filename,host="www.youtube.com",sourceUrl=task.Url,destination=task.Destination,state="Failed",status="Failed",message=task.Message,mediaFormat="mp4",received=0,total=(long?)null,canResume=true,fileType="Video",fileAvailable=false,startedAt=task.StartedAt,finishedAt=task.FinishedAt,activeSeconds=39 })!.AsObject();record["missingTools"]=new JsonArray("yt-dlp");
        var model=new JsonObject { ["kind"]="main",["page"]="history",["ready"]=true,["tasks"]=new JsonArray(record),["preferences"]=new JsonObject { ["historyDensity"]="comfortable" } };
        var window=new Window { Width=980, Height=720, Left=-10000, Top=-10000, ShowInTaskbar=false };
        LocalHtmlSurface? ui=null;var retries=0;var checks=0;var copies=new List<string>();
        ui=new LocalHtmlSurface(window,new Grid(),()=>model,message=>{
            var action=message.GetProperty("action").GetString();
            if(action=="recheck-download-tools"){checks++;record["missingTools"]=new JsonArray();ui!.Publish();}
            if(action=="retry-download")retries++;
            if(action is "copy-path" or "copy-url")copies.Add(action);
            return Task.CompletedTask;
        });
        async Task Until(string condition){var end=DateTime.UtcNow.AddSeconds(20);while(await ui.EvaluateAsync(condition)!="true"){if(DateTime.UtcNow>end)throw new Exception("Details fixture: "+condition+" "+await ui.EvaluateAsync("window.__syphonError || JSON.stringify({height:document.querySelector('.download-details')?.getBoundingClientRect().height,viewport:innerHeight,collapsed:window.collapsedDetailsHeight})"));await Task.Delay(30);}}
        try {
            window.Show();var deadline=DateTime.UtcNow.AddSeconds(20);while(!ui.IsReady){if(ui.InitializationError is not null||DateTime.UtcNow>deadline)throw new Exception(ui.InitializationError??"Details surface startup timed out.");await Task.Delay(30);}ui.Publish();await Until("!!document.querySelector('.history-filename')");
            await ui.EvaluateAsync("document.querySelector('.history-filename').click()");
            await Until("document.querySelector('#details-retry')?.disabled===true");
            await Until("document.querySelector('#download-technical-details')?.open===true");
            await ui.EvaluateAsync("document.querySelector('#download-technical-details').open=false");ui.Publish();
            await Until("document.querySelector('#download-technical-details')?.open===false");
            await ui.EvaluateAsync("document.querySelector('#download-setup-instructions').open=true");
            await Until("document.querySelector('#download-setup-instructions').textContent.includes('does not run an installer')");
            if(retries!=0)throw new Exception("Setup instructions unexpectedly retried the task.");
            await ui.CaptureAsync(Path.Combine(output,"download-details-missing-component.png"));
            await ui.EvaluateAsync("document.querySelector('#recheck-download-tools').click()");
            await Until("document.querySelector('#details-retry')?.disabled===false&&document.querySelector('.details-remedy').textContent.includes('now available')");
            await Until("document.querySelector('#download-technical-details')?.open===false");
            if(checks!=1)throw new Exception("Component recheck was lost or repeated.");
            await ui.CaptureAsync(Path.Combine(output,"download-details-component-ready.png"));
            await ui.EvaluateAsync("document.querySelector('#details-retry').click()");
            await Until("!document.querySelector('#history-details')");
            if(retries!=1)throw new Exception("Retry was lost or repeated.");
            window.Height=927;await Task.Delay(80);
            record["destination"]=@"C:\Downloads\"+new string('p',1500)+".mp4";record["sourceUrl"]="https://downloads.example.test/file?token="+new string('q',3000);
            foreach(var status in new[]{"Cancelled","Completed"}) {
                record["estimatedTotal"]=status=="Cancelled"?10485760:null;record["total"]=status=="Completed"?0:null;record["state"]=status;record["canResume"]=status!="Completed";record["message"]="Transfer "+status.ToLowerInvariant();ui.Publish();
                await Until("!!document.querySelector('.history-filename')");await Task.Delay(80);await ui.EvaluateAsync("document.querySelector('.history-filename').click()");
                await Until("!!document.querySelector('#history-details')");
                await Until("[...document.querySelectorAll('#history-details details')].every(e=>!e.open)");
                if(status=="Cancelled")await Until("document.querySelector('.details-metrics').textContent.includes('Estimated')&&!document.querySelector('.details-metrics').textContent.includes('%')");
                await Until("[...document.querySelectorAll('.details-truncated-value')].every(e=>e.title===e.textContent&&e.scrollWidth>e.clientWidth&&getComputedStyle(e).userSelect==='text')&&document.querySelector('.download-details').scrollWidth<=document.querySelector('.download-details').clientWidth+1");
                await ui.EvaluateAsync("document.querySelector('[aria-label=\"Copy file path\"]').click();document.querySelector('.details-copy-value[aria-label=\"Copy original URL\"]').click()");
                await ui.EvaluateAsync("window.collapsedDetailsHeight=document.querySelector('.download-details').getBoundingClientRect().height;document.querySelector('#download-integrity').open=true");await Task.Delay(80);
                await Until("document.querySelector('.download-details').getBoundingClientRect().height>window.collapsedDetailsHeight&&document.querySelector('.download-details').getBoundingClientRect().height<=innerHeight*.85+1");
                await ui.EvaluateAsync("document.querySelector('#download-integrity').open=false");await Task.Delay(80);
                await Until("Math.abs(document.querySelector('.download-details').getBoundingClientRect().height-window.collapsedDetailsHeight)<1");
                await ui.CaptureAsync(Path.Combine(output,"download-details-"+status.ToLowerInvariant()+"-compact.png"));
                await ui.EvaluateAsync("document.querySelector('#details-header-close').click()");await Until("!document.querySelector('#history-details')");
            }
            if(copies.Count!=4)throw new Exception("Path or URL copy action was lost.");
        } finally { window.Close();ui.Dispose(); }
    }
}
