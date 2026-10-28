using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Syphon.Desktop;
namespace Syphon.Desktop.SmokeTests;
internal static class Beta20LayoutRegressionTests {
 internal static int Run(string mode){
  var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var exit=1;
  app.Startup+=async(_,_)=>{
   var actions=new List<string>();var records=new JsonArray();
   for(var i=0;i<25;i++)records.Add(JsonSerializer.SerializeToNode(new{id=Guid.NewGuid(),filename=i==0?new string('W',180)+".mp4":"Transfer "+i,host="example.test",sourceUrl="https://example.test/video",destination="C:\\Downloads\\video.mp4",state=mode=="dashboard"?"Downloading":i==0?"Failed":"Completed",received=1234,total=(long?)null,percent=0,speed="87.5 KB/s",progress="66 MB received · ≈131.8 MB estimated",fileType="Video",canPause=true,canResume=true,fileAvailable=false,missingTools=i==0?new[]{"yt-dlp"}:Array.Empty<string>(),message=i==0?"A required media tool is missing.":null,finishedAt=DateTimeOffset.UtcNow.AddMinutes(-i)}));
   var model=new JsonObject{["kind"]="main",["page"]=mode=="dashboard"?"dashboard":"history",["ready"]=true,["tasks"]=records,["preferences"]=new JsonObject(),["limits"]=new JsonObject(),["samples"]=new JsonArray()};
   var window=new Window{Width=760,Height=580,Left=-10000,Top=-10000,ShowInTaskbar=false};LocalHtmlSurface? ui=null;
   ui=new LocalHtmlSurface(window,new Grid(),()=>model,message=>{var action=message.GetProperty("action").GetString()!;actions.Add(action);if(action=="recheck-download-tools"){records[0]!["missingTools"]=new JsonArray();ui!.Publish();}return Task.CompletedTask;});
   async Task Wait(string condition){var until=DateTime.UtcNow.AddSeconds(15);while(await ui.EvaluateAsync(condition)!="true"){if(DateTime.UtcNow>until)throw new Exception(condition+" "+await ui.EvaluateAsync("window.__syphonError||''"));await Task.Delay(30);}}
   async Task Check(string condition){if(await ui.EvaluateAsync(condition)!="true")throw new Exception("Layout regression: "+condition);}
   try{
    window.Show();while(!ui.IsReady){if(ui.InitializationError!=null)throw new Exception(ui.InitializationError);await Task.Delay(30);}ui.Publish();
    var selector=mode=="dashboard"?".transfer":".history-record";await Wait($"document.querySelectorAll('{selector}').length===25");
    foreach(var width in new[]{660,760,1280}){
     window.Width=width;await Task.Delay(100);
     await Check($"document.documentElement.scrollWidth<=innerWidth+1&&[...document.querySelectorAll('{selector}')].every(r=>r.scrollWidth<=r.clientWidth+1)");
     if(mode=="dashboard")await Check("(()=>{const r=document.querySelector('.rate').getBoundingClientRect(),b=document.querySelector('.round-control').getBoundingClientRect();return r.right<b.left&&getComputedStyle(document.querySelector('.rate small')).overflow==='hidden';})()");
    }
    window.Width=760;
    if(mode=="dashboard"){
     await Check("document.querySelector('#tasks').scrollHeight>document.querySelector('#tasks').clientHeight");
     await ui.EvaluateAsync("document.querySelector('.round-control').focus()");ui.Publish();await Task.Delay(250);await Check("document.activeElement.classList.contains('round-control')");
     model["tasks"]=new JsonArray();ui.Publish();await Wait("!!document.querySelector('.empty-state .button')");await ui.EvaluateAsync("document.querySelector('.empty-state .button').click()");if(!actions.Contains("new"))throw new Exception("Empty queue action did not open New Download.");
    }else{
     await Check("(()=>{const n=document.querySelector('.record-name');return n.title===n.textContent&&getComputedStyle(n).textOverflow==='ellipsis'&&n.scrollWidth>n.clientWidth;})()");
     if(mode=="history"){
      await ui.EvaluateAsync("(()=>{const e=document.querySelector('#history-search');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,'Transfer 17');e.dispatchEvent(new Event('input',{bubbles:true}));})()");await Wait("document.querySelectorAll('.history-record').length===1");
     }else{
      await ui.EvaluateAsync("document.querySelector('.record-select').click()");await Wait("!!document.querySelector('.detail-window')");
      await Check("document.querySelector('.detail-window .dialog-actions .primary').disabled&&!!document.querySelector('#recheck-download-tools')");
      await ui.EvaluateAsync("document.querySelector('#recheck-download-tools').click()");await Wait("!document.querySelector('.detail-window .dialog-actions .primary').disabled");
      await ui.EvaluateAsync("document.querySelectorAll('.detail-fields button').forEach(b=>b.click())");if(actions.Count(a=>a is "copy-path" or "copy-url")!=2)throw new Exception("Details copy actions failed.");
      await Check("document.querySelector('.detail-window .dialog-core').getBoundingClientRect().height<=innerHeight&&document.querySelector('.detail-window .dialog-actions').getBoundingClientRect().bottom<=innerHeight");
      await ui.EvaluateAsync("document.querySelector('.detail-window .dialog-actions .primary').click()");await Wait("!document.querySelector('.detail-window')");if(actions.Count(a=>a=="retry-download")!=1)throw new Exception("Retry action was lost or duplicated.");
     }
    }
    Console.WriteLine("PASS current Beta 20 "+mode+": long metadata, compact/wide bounds, scrolling and current controls.");exit=0;
   }catch(Exception ex){Console.Error.WriteLine(ex);}finally{window.Close();ui.Dispose();app.Shutdown(exit);}
  };app.Run();return exit;
 }
}
