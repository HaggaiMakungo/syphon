using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Wpf;
using Syphon.Desktop;

namespace Syphon.Desktop.SmokeTests;

internal static class ObsidianVisualTests
{
    public static int Run(string[] args) {
        var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/obsidian");Directory.CreateDirectory(output);
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var code=1;
        app.Startup+=async(_,_)=>{try{await CheckAsync(output);Console.WriteLine("PASS Stealth Hybrid: four window sizes; empty/single/multiple/paused/queued/failed/completed states; History selection; dropdown checkmark and keyboard; focused search; semantic colours; contrast; reduced motion.");code=0;}catch(Exception ex){Console.Error.WriteLine(ex);}finally{app.Shutdown(code);}};
        app.Run();return code;
    }
    private static JsonObject Record(string state,int index) => (JsonObject)JsonSerializer.SerializeToNode(new {
        id=Guid.NewGuid(),filename=index==0?"Project archive — "+new string('w',160)+".zip":$"Transfer {index+1:00}.zip",host="downloads.example.test",sourceUrl="https://downloads.example.test/transfer/"+index,
        destination=@"C:\Downloads\transfer.zip",state,status=state,message=state=="Failed"?"The source server refused the request.":"",diagnostic=state=="Failed"?"HTTP 403":"",
        received=state=="Completed"?8388608:2097152,total=8388608,percent=state=="Completed"?100:25,rate=state=="Downloading"?2097152:0,speed=state=="Downloading"?"2 MB/s":"—",remaining=state=="Downloading"?"3s":"—",
        progress=state=="Completed"?"8 MB of 8 MB":"2 MB of 8 MB",connections=state=="Downloading"?4:0,canPause=state is "Downloading" or "Queued",canResume=state is "Paused" or "Failed" or "Cancelled",fileType="Archives",fileAvailable=false,
        finishedAt=DateTimeOffset.Now.AddDays(-(index%2)),createdAt=DateTimeOffset.Now.AddHours(-1),sha256=state=="Completed"?new string('b',64):null
    })!;
    private static async Task CheckAsync(string output) {
        var records=new JsonArray();
        var model=new JsonObject { ["kind"]="main",["page"]="dashboard",["ready"]=true,["tasks"]=records,["currentRate"]=0,["preferences"]=new JsonObject{["historyDensity"]="comfortable"},["limits"]=new JsonObject{["globalKiB"]=0},["schedule"]=new JsonObject() };
        var window=new Window { Width=1280,Height=720,Left=-10000,Top=-10000,ShowInTaskbar=false,ResizeMode=ResizeMode.CanResize };
        LocalHtmlSurface? ui=null;
        ui=new LocalHtmlSurface(window,new Grid(),()=>model,m=>{if(m.GetProperty("action").GetString()=="select")model["selectedId"]=m.GetProperty("id").GetString();ui!.Publish();return Task.CompletedTask;});
        var checks=new List<string>();
        async Task Until(string expression){var end=DateTime.UtcNow.AddSeconds(15);while(await ui.EvaluateAsync(expression)!="true"){if(DateTime.UtcNow>end)throw new Exception(expression+" "+await ui.EvaluateAsync("window.__syphonError||''"));await Task.Delay(25);}}
        async Task Assert(string expression,string label){if(await ui.EvaluateAsync(expression)!="true")throw new Exception(label+": "+await ui.EvaluateAsync("window.__syphonError||document.body.innerText.slice(0,500)"));checks.Add(label);}
        async Task Capture(string name){await Task.Delay(80);await ui.CaptureAsync(Path.Combine(output,name+".png"));}
        const string Bounds="document.documentElement.scrollWidth<=innerWidth&&[...document.querySelectorAll('.download-card,.history-row')].every(r=>r.scrollWidth<=r.clientWidth+1)";
        try {
            window.Show();var end=DateTime.UtcNow.AddSeconds(20);while(!ui.IsReady){if(ui.InitializationError!=null||DateTime.UtcNow>end)throw new Exception(ui.InitializationError??"Startup timeout");await Task.Delay(30);}ui.Publish();await Until("!!document.querySelector('#empty-new-download')");
            var browser=ui.Children.OfType<WebView2>().Single();
            await Assert("parseFloat(getComputedStyle(document.querySelector('#summary-today')).fontSize)<parseFloat(getComputedStyle(document.querySelector('#summary-speed')).fontSize)","secondary statistics are smaller than live metrics");
            await Assert("getComputedStyle(document.querySelector('.summary-label')).color!==getComputedStyle(document.querySelector('#summary-speed')).color","metric labels have distinct emphasis");
            await Assert("document.querySelector('.activity-title').textContent==='Transfer activity'&&!!document.querySelector('.activity-switch')","graph has stable section heading and visible toggle");

            await Until("[...document.querySelectorAll('.window-brand img')].length===2&&[...document.querySelectorAll('.window-brand img')].every(i=>i.complete&&i.naturalWidth>0)");
            await Assert("document.querySelector('.window-wordmark').alt==='Syphon'&&!document.querySelector('.window-mark b')","supplied emblem and wordmark load in desktop chrome");
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled","{\"enabled\":true}");
            await Assert("(()=>{const s=getComputedStyle(document.documentElement);return s.getPropertyValue('--ov-glow-strength').trim()==='0.5'&&s.getPropertyValue('--ov-panel-brightness').trim()==='0'&&s.getPropertyValue('--ov-border-intensity').trim()==='0'&&s.getPropertyValue('--ov-panel').trim()==='#050508';})()","approved 50/0/0 Stealth Hybrid material");
            await Assert("(()=>{const s=getComputedStyle(document.querySelector('.download-workspace'));return s.backgroundImage.includes('90% 125%')&&s.backgroundImage.includes('0% 125%')&&s.borderTopColor==='rgb(18, 18, 22)';})()","subsurface light stays below the face with neutral structural borders");
            await Assert("(()=>{const slots=[...document.querySelectorAll('.summary-metric,.summary-detail')].map(e=>e.getBoundingClientRect());return slots.length===5&&slots.every(s=>Math.abs(s.top-slots[0].top)<1);})()","five overview metrics share one desktop row");
            await Assert("getComputedStyle(document.querySelector('.window-chrome')).getPropertyValue('app-region')==='drag'&&[...document.querySelectorAll('.window-chrome button')].every(b=>getComputedStyle(b).getPropertyValue('app-region')==='no-drag')","titlebar drag and button exclusion regions retained");
            foreach(var size in new[]{(1280,720),(1440,900),(1650,927),(1920,1080),(760,540),(900,600)}) {
                window.Width=size.Item1;window.Height=size.Item2;var prefix=$"{size.Item1}x{size.Item2}";
                model["page"]="dashboard";records.Clear();model["currentRate"]=0;ui.Publish();await Until("!document.querySelector('#download-empty').hidden");
                await Assert(Bounds,prefix+" empty bounds");
                await Assert("(()=>{const button=document.querySelector('#empty-new-download'),r=button.getBoundingClientRect();return r.height>=33&&r.height<=35&&getComputedStyle(button).flexGrow==='0';})()",prefix+" empty CTA never grows vertically");
                await Capture(prefix+"-main-empty");
                records.Add(Record("Downloading",0));model["currentRate"]=2097152;model["samples"]=new JsonArray(1048576,3145728,2097152);model["sampleTimes"]=new JsonArray(DateTimeOffset.UtcNow.AddSeconds(-2).ToString("O"),DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"),DateTimeOffset.UtcNow.ToString("O"));ui.Publish();await Until("document.querySelectorAll('.download-card').length===1");
                await Assert(Bounds,prefix+" single bounds");await Assert("(()=>{const n=document.querySelector('.transfer-filename');return n.title===n.textContent&&getComputedStyle(n).textOverflow==='ellipsis';})()",prefix+" filename truncation");await Capture(prefix+"-main-active");
                foreach(var state in new[]{"Downloading","Paused","Queued","Failed","Completed","Cancelled"})records.Add(Record(state,records.Count));
                model["currentRate"]=4194304;ui.Publish();await Until("document.querySelectorAll('.download-card').length===4");await Assert(Bounds,prefix+" mixed bounds");await Capture(prefix+"-main-mixed");
                await ui.EvaluateAsync("document.querySelector('#tasks').scrollTop=document.querySelector('#tasks').scrollHeight");await Capture(prefix+"-main-paused-queued");await ui.EvaluateAsync("document.querySelector('#tasks').scrollTop=0");
                await ui.EvaluateAsync("document.querySelector('#bandwidth-preset-trigger').click()");await Until("!!document.querySelector('.syphon-select-menu:popover-open')");
                await Assert("document.querySelector('.syphon-select-menu:popover-open [aria-selected=true] .syphon-select-check').textContent==='✓'",prefix+" dropdown checkmark");
                await Assert("[...document.querySelectorAll('.syphon-select-menu:popover-open .syphon-select-option>span:first-child')].every(s=>s.scrollWidth<=s.clientWidth+1)",prefix+" dropdown labels fit with checkmark");
                await Assert("(()=>{const r=document.querySelector('.syphon-select-menu:popover-open').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth+1&&r.top>=0&&r.bottom<=innerHeight+1;})()",prefix+" dropdown bounds");await Capture(prefix+"-dropdown");
                await ui.EvaluateAsync("document.querySelector('#bandwidth-preset-trigger').dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowDown',bubbles:true}));document.querySelector('#bandwidth-preset-trigger').dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}));");
                await Assert("!document.querySelector('.syphon-select-menu:popover-open')",prefix+" dropdown keyboard escape");
                model["page"]="history";ui.Publish();await Until("document.querySelectorAll('.history-row').length===3");await Assert(Bounds,prefix+" history bounds");await Capture(prefix+"-history");
                await Assert("new Set([...document.querySelectorAll('.history-row')].map(e=>getComputedStyle(e,':before').backgroundColor)).size===3",prefix+" distinct semantic outcomes");
                await ui.EvaluateAsync("document.querySelector('.history-filter[data-status=Failed]').click()");await Until("document.querySelectorAll('.history-row').length===1");
                await Assert("document.querySelector('#history-records').textContent==='3'&&document.querySelector('.history-row').dataset.outcome==='Failed'",prefix+" failure shortcut retains global totals");
                await ui.EvaluateAsync("document.querySelector('.history-filter[data-status=all]').click()");await Until("document.querySelectorAll('.history-row').length===3");
                await ui.EvaluateAsync("document.querySelector('.history-row').click()");await Until("!document.querySelector('#history-inspector').hidden");await Assert(Bounds,prefix+" history pane bounds");await Capture(prefix+"-history-selected");
                await ui.EvaluateAsync("document.querySelector('#clear-downloads').focus();document.querySelector('#clear-downloads').click()");await Until("!!document.querySelector('#clear-downloads-dialog')");
                await Assert("document.querySelector('input[value=completed]').checked&&!document.querySelector('#confirm-clear-downloads').disabled&&document.querySelector('#clear-downloads-status').textContent.includes('1 entry')",prefix+" clear dialog default");
                await Capture(prefix+"-clear-downloads");
                await ui.EvaluateAsync("document.querySelector('input[value=custom]').click()");
                await Assert("!document.querySelector('#clear-custom-list').hidden&&document.querySelector('#confirm-clear-downloads').disabled",prefix+" custom clear starts empty");
                await ui.EvaluateAsync("document.querySelector('#clear-custom-list input').click()");
                await Assert("!document.querySelector('#confirm-clear-downloads').disabled&&document.querySelector('#clear-custom-list').children.length===3",prefix+" custom history selection");
                await Capture(prefix+"-clear-custom");
                await ui.EvaluateAsync("document.querySelector('#clear-downloads-dialog').dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");
                await Assert("!document.querySelector('#clear-downloads-dialog')&&document.activeElement.id==='clear-downloads'",prefix+" clear dialog Escape restores focus");
                await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"keyDown\",\"key\":\"Tab\",\"code\":\"Tab\",\"windowsVirtualKeyCode\":9}");
                await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent","{\"type\":\"keyUp\",\"key\":\"Tab\",\"code\":\"Tab\",\"windowsVirtualKeyCode\":9}");
                await ui.EvaluateAsync("document.querySelector('#search').focus()");await Assert("document.querySelector('#search').matches(':focus-visible')&&parseFloat(getComputedStyle(document.querySelector('#search')).outlineWidth)>=1.5",prefix+" input keyboard focus");await Capture(prefix+"-input-focus");
            }
            await Assert("(()=>{const css=getComputedStyle(document.documentElement),rgb=v=>v.match(/[0-9a-f]{2}/gi).map(x=>parseInt(x,16)/255).map(x=>x<=.04045?x/12.92:((x+.055)/1.055)**2.4),lum=v=>rgb(v.replace('#','')).reduce((a,x,i)=>a+x*[.2126,.7152,.0722][i],0),bg=lum(css.getPropertyValue('--ov-panel').trim());return ['--ov-text-primary','--ov-text-secondary','--ov-text-muted'].every(k=>(lum(css.getPropertyValue(k).trim())+.05)/(bg+.05)>=4.5)&&['--ov-primary-top','--ov-primary-bottom','--ov-primary-hover','--ov-primary-pressed'].every(k=>(lum(css.getPropertyValue('--ov-text-primary').trim())+.05)/(lum(css.getPropertyValue(k).trim())+.05)>=4.5);})()","WCAG text-on-panel and primary-button contrast >=4.5:1");
            await browser.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[{\"name\":\"prefers-reduced-motion\",\"value\":\"reduce\"}]}");
            await Assert("matchMedia('(prefers-reduced-motion: reduce)').matches&&getComputedStyle(document.querySelector('#history-pane-toggle')).transitionDuration==='0s'","reduced motion transitions disabled");
            await ui.EvaluateAsync("document.querySelector('#history-sort-trigger').click()");
            await Assert("document.querySelector('.syphon-select-menu:popover-open').getAnimations().length===0","reduced motion disables dropdown unfold animation");
            await File.WriteAllTextAsync(Path.Combine(output,"validation.json"),JsonSerializer.Serialize(new{sizes=new[]{"1280x720","1440x900","1650x927","1920x1080"},coordinateSystem="WPF device-independent window units; PNG pixel size follows Windows display scale",checks},new JsonSerializerOptions{WriteIndented=true}));
        }finally{window.Close();ui.Dispose();}
        var removal=new RemoveDownloadDialog("unfinished-project.zip",true){WindowStartupLocation=WindowStartupLocation.Manual,Left=-10000,Top=-10000,ShowInTaskbar=false};
        try {
            removal.Show();await Task.Delay(100);removal.UpdateLayout();
            if(removal.DeletePartial||((SolidColorBrush)removal.Background).Color!=Color.FromRgb(8,8,11))throw new Exception("Native removal dialog changed its default choice or shell palette.");
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(removal.ActualWidth),(int)Math.Ceiling(removal.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(removal);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,"native-remove-dialog.png"));encoder.Save(file);
        }finally{removal.Close();}
    }
}
