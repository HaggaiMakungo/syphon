using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Syphon.Desktop;

namespace Syphon.Desktop.SmokeTests;

internal static class HistoryLayoutSmokeTests
{
    public static int Run(string[] args) {
        var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/history-layout");Directory.CreateDirectory(output);
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var code=1;
        app.Startup+=async(_,_)=>{try{await CheckAsync(output);Console.WriteLine("PASS History layout: 1280/1440/1650/1920, Comfortable/Compact, Details closed/open, grid alignment, long filenames, unknown sizes, selection and toolbar bounds.");code=0;}catch(Exception ex){Console.Error.WriteLine(ex);}finally{app.Shutdown(code);}};
        app.Run();return code;
    }
    private static async Task CheckAsync(string output) {
        var records=new JsonArray();for(var n=0;n<18;n++) {
            var name=n==0?"A very long filename — "+new string('w',180)+".mp4":$"Transfer {n:00} — "+(n%2==0?"A document.pdf":"A video.mp4");
            records.Add(JsonSerializer.SerializeToNode(new { id=Guid.NewGuid(),filename=name,host="downloads.example.test",sourceUrl="https://downloads.example.test/file",destination=@"C:\Downloads\"+name,state=new[]{"Completed","Cancelled","Failed"}[n%3],status="Saved outcome",message="Fixture transfer",received=n%3==0?104857600:0,total=n%2==0?(long?)104857600:null,canResume=n%3!=0,fileType=n%2==0?"Documents":"Video",fileAvailable=false,finishedAt=DateTimeOffset.Now.AddDays(-(n/6)),activeSeconds=39 }));
        }
        var preferences=new JsonObject { ["historyDensity"]="comfortable" };
        var model=new JsonObject { ["kind"]="main",["page"]="history",["ready"]=true,["tasks"]=records,["preferences"]=preferences };
        var window=new Window { Width=1280,Height=927,Left=-10000,Top=-10000,ShowInTaskbar=false,ResizeMode=ResizeMode.CanResize };
        LocalHtmlSurface? ui=null;
        ui=new LocalHtmlSurface(window,new Grid(),()=>model,message=>{switch(message.GetProperty("action").GetString()){
            case "select":model["selectedId"]=message.GetProperty("id").GetString();break;
            case "history-density":preferences["historyDensity"]=message.GetProperty("density").GetString();break;
        }ui!.Publish();return Task.CompletedTask;});
        async Task Until(string predicate){var end=DateTime.UtcNow.AddSeconds(15);while(await ui.EvaluateAsync(predicate)!="true"){if(DateTime.UtcNow>end)throw new Exception("History layout condition: "+predicate+" "+await ui.EvaluateAsync("window.__syphonError||''"));await Task.Delay(25);}}
        async Task Assert(string predicate,string label){if(await ui.EvaluateAsync(predicate)!="true")throw new Exception(label+": "+await ui.EvaluateAsync("JSON.stringify({viewport:innerWidth,grid:getComputedStyle(document.querySelector('.history-row')).gridTemplateColumns,header:getComputedStyle(document.querySelector('.history-columns')).gridTemplateColumns})"));}
        try {
            window.Show();var end=DateTime.UtcNow.AddSeconds(20);while(!ui.IsReady){if(ui.InitializationError!=null||DateTime.UtcNow>end)throw new Exception(ui.InitializationError??"History startup timed out.");await Task.Delay(25);}ui.Publish();await Until("document.querySelectorAll('.history-row').length===18");
            foreach(var width in new[]{1280,1440,1650,1920,980})foreach(var density in new[]{"comfortable","compact"}) {
                window.Width=width;window.Height=width switch {1280=>720,1440=>900,1920=>1080,980=>720,_=>927};preferences["historyDensity"]=density;ui.Publish();await Until($"document.querySelector('#history-list').dataset.density==='{density}'");await Task.Delay(100);
                foreach(var pane in new[]{false,true}) {
                    await ui.EvaluateAsync("document.querySelector('.history-row').click()");await Until("document.querySelector('.history-row').dataset.selected==='true'");await Task.Delay(60);
                    if(!pane){await ui.EvaluateAsync("document.querySelector('#history-pane-toggle').click()");await Until("document.querySelector('.history-row').dataset.selected==='false'");}
                    var label=$"{width}-{density}-{(pane?"details":"list")}";
                    await Assert("(()=>{const h=document.querySelector('.history-columns'),r=document.querySelector('.history-row'),cells=[...h.children],values=[...r.children].slice(0,4);return getComputedStyle(h).gridTemplateColumns===getComputedStyle(r).gridTemplateColumns&&cells.every((c,i)=>Math.abs(c.getBoundingClientRect().left-values[i].getBoundingClientRect().left)<1)&&[1,2].every(i=>Math.abs(cells[i].getBoundingClientRect().right-values[i].getBoundingClientRect().right)<1);})()",label+" header/row alignment");
                    await Assert("(()=>{const rows=[...document.querySelectorAll('.history-row')];return rows.every(r=>r.scrollWidth<=r.clientWidth+1)&&document.documentElement.scrollWidth<=innerWidth&&document.querySelector('#history-list').scrollWidth<=document.querySelector('#history-list').clientWidth+1;})()",label+" overflow");
                    await Assert("(()=>{const name=[...document.querySelectorAll('.history-filename')].find(e=>e.textContent.length>100);return name.title===name.textContent&&name.scrollWidth>name.clientWidth&&getComputedStyle(name).textOverflow==='ellipsis';})()",label+" filename truncation");
                    await Assert("(()=>{const toolbar=document.querySelector('.history-toolbar').getBoundingClientRect();return [...document.querySelectorAll('.history-view-controls .syphon-select-trigger,.history-pane-toggle')].every(e=>{const r=e.getBoundingClientRect();return r.left>=toolbar.left&&r.right<=toolbar.right+1;});})()",label+" toolbar bounds");
                    await Assert("document.querySelectorAll('.history-filter').length===4&&!!document.querySelector('#history-ratio')&&!document.querySelector('#history-density')",label+" summary filters and settings-only density");
                    if(density=="compact")await Assert("(()=>{const row=document.querySelector('.history-row'),height=row.getBoundingClientRect().height*1.25,gap=parseFloat(getComputedStyle(row).marginBottom)*1.25;return height>=54&&height<=60&&gap>=3&&gap<=4;})()",label+" compact row target");
                    if(width!=980)await ui.CaptureAsync(Path.Combine(output,label+".png"));
                }
            }
        }finally{window.Close();ui.Dispose();}
    }
}
