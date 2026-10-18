using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Syphon.Desktop;

namespace Syphon.Desktop.SmokeTests;

internal static class RedesignSmokeTests
{
    public static int Run(string[] args) {
        var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/redesign");Directory.CreateDirectory(output);
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var code=1;
        app.Startup+=async(_,_)=>{try{await CheckAsync(output);Console.WriteLine("PASS redesign: canonical CTA, graph disclosure, history inspector actions and selection, settings stepper bounds, advanced dialog disclosure and sizing.");code=0;}catch(Exception ex){Console.Error.WriteLine(ex);}finally{app.Shutdown(code);}};app.Run();return code;
    }
    private static async Task CheckAsync(string output) {
        var icon=(System.Windows.Media.Imaging.BitmapSource)Application.Current.FindResource("SyphonWindowIcon");
        if(icon.PixelWidth!=256||icon.PixelHeight!=256)throw new Exception("Window icon must use the full 256px source rather than a 16px ICO frame.");
        var preferences=new JsonObject { ["folder"]=Path.GetTempPath() };
        var limits=new JsonObject { ["perFile"]=4,["perSite"]=4,["thresholdMiB"]=16,["retries"]=5 };
        var records=new JsonArray();var model=new JsonObject { ["kind"]="main",["page"]="dashboard",["ready"]=true,["tasks"]=records,["preferences"]=preferences,["limits"]=limits,["schedule"]=new JsonObject() };
        var window=new Window { Width=1120,Height=820,Left=-10000,Top=-10000,ShowInTaskbar=false };
        LocalHtmlSurface? ui=null;ui=new LocalHtmlSurface(window,new Grid(),()=>model,message=>{
            var action=message.GetProperty("action").GetString();
            if(action=="navigate")model["page"]=message.GetProperty("page").GetString();
            if(action=="select")model["selectedId"]=message.GetProperty("id").GetString();
            if(action=="settings")limits=(JsonObject)JsonNode.Parse(message.GetProperty("limits").GetRawText())!;
            ui!.Publish();return Task.CompletedTask;
        });
        async Task Until(string predicate){var end=DateTime.UtcNow.AddSeconds(15);while(await ui.EvaluateAsync(predicate)!="true"){if(DateTime.UtcNow>end)throw new Exception(predicate+" "+await ui.EvaluateAsync("window.__syphonError||''"));await Task.Delay(30);}}
        async Task Assert(string predicate){if(await ui.EvaluateAsync(predicate)!="true")throw new Exception("Redesign assertion: "+predicate);}
        try {
            window.Show();var end=DateTime.UtcNow.AddSeconds(20);while(!ui.IsReady){if(ui.InitializationError!=null||DateTime.UtcNow>end)throw new Exception(ui.InitializationError??"Startup timeout");await Task.Delay(30);}ui.Publish();await Until("!!document.querySelector('#empty-new-download')");
            await Assert("document.querySelector('#new-download').hidden&&!document.querySelector('#download-empty').hidden&&!document.querySelector('#quick-url')&&!document.querySelector('.workspace-drop-hint')&&document.querySelectorAll('#download-empty button').length===1");
            await ui.EvaluateAsync("document.querySelector('#activity-toggle').click()");await Assert("document.querySelector('#activity-toggle').getAttribute('aria-expanded')==='true'&&document.querySelector('.activity-title').textContent==='Transfer activity'");
            await ui.EvaluateAsync("document.querySelector('#activity-toggle').click()");
            await ui.CaptureAsync(Path.Combine(output,"downloads.png"));
            await ui.EvaluateAsync("document.querySelector('#activity-toggle').click()");
            var record=JsonSerializer.SerializeToNode(new {id=Guid.NewGuid(),filename="Installer.exe",host="downloads.example.test",sourceUrl="https://downloads.example.test/a-long-path/installer.exe?token=fixture",destination=@"C:\Downloads\Installer.exe",state="Completed",status="Completed",received=1048576,total=1048576,percent=100,connections=0,fileType="Other",finishedAt=DateTimeOffset.Now,sha256=new string('a',64)})!;
            records.Add(record);
            record["state"]="Downloading";record["status"]="Downloading";record["speed"]="2 MB/s";record["remaining"]="3s";record["progress"]="2 MB of 8 MB";record["total"]=8388608;record["received"]=2097152;record["percent"]=25;
            record["rate"]=2097152;model["currentRate"]=2097152;model["samples"]=new JsonArray(8388608,16777216);model["sampleTimes"]=new JsonArray(DateTimeOffset.UtcNow.AddSeconds(-2).ToString("O"),DateTimeOffset.UtcNow.AddSeconds(-1).ToString("O"));model["sampledAt"]=DateTimeOffset.UtcNow.ToString("O");
            ui.Publish();await Until("document.querySelector('#summary-speed').dataset.rate==='2097152'");
            await Assert("getComputedStyle(document.documentElement).getPropertyValue('--ov-canvas').trim()==='#070709'&&getComputedStyle(document.querySelector('.download-card')).backgroundImage.includes('radial-gradient')&&getComputedStyle(document.querySelector('.download-workspace')).backgroundColor==='rgb(5, 5, 8)'");
            await Assert("document.querySelector('#summary-speed').textContent==='2 MB/s'&&document.querySelector('#manager-speed-chart').dataset.lastRate==='2097152'");
            await Assert("parseFloat(getComputedStyle(document.querySelector('.transfer-progress')).height)>=8&&getComputedStyle(document.querySelector('.transfer-progress')).backgroundColor==='rgb(25, 21, 31)'");
            await ui.CaptureAsync(Path.Combine(output,"downloads-active.png"));
            record["rate"]=0;model["currentRate"]=0;model["sampledAt"]=DateTimeOffset.UtcNow.ToString("O");ui.Publish();
            await Until("document.querySelector('#summary-speed').dataset.rate==='0'");
            await Assert("document.querySelector('#manager-speed-chart').dataset.lastRate==='0'&&document.querySelector('#manager-graph-state').textContent.includes('Now: 0 B/s')");
            record["state"]="Completed";record["status"]="Completed";record["total"]=1048576;record["received"]=1048576;record["percent"]=100;model["page"]="history";ui.Publish();await Until("!!document.querySelector('.history-row')");
            await Assert("!document.querySelector('.history-row [role=progressbar]')");
            await ui.EvaluateAsync("document.querySelector('.history-row').click()");await Until("!document.querySelector('#history-inspector').hidden&&document.querySelector('.history-pane-actions')!==null");
            await Assert("document.querySelector('.history-pane-actions').getBoundingClientRect().bottom<=document.querySelector('.history-inspector dl').getBoundingClientRect().top&&getComputedStyle(document.querySelector('.history-pane-url button')).textOverflow==='ellipsis'");
            await ui.CaptureAsync(Path.Combine(output,"history.png"));
            await ui.EvaluateAsync("document.querySelector('#history-pane-toggle').click()");await Assert("![...document.querySelectorAll('.history-row')].some(r=>r.dataset.selected==='true')");
            model["page"]="settings";ui.Publish();await Until("!!document.querySelector('.number-stepper')");await Assert("!document.querySelector('#new-download')");
            await ui.EvaluateAsync("document.querySelector('[aria-label=\"Decrease Maximum connections\"]').click()");await Assert("document.querySelector('#setting-perFile').value==='3'");
            await ui.EvaluateAsync("for(let i=0;i<8;i++)document.querySelector('[aria-label=\"Decrease Maximum connections\"]').click()");await Assert("document.querySelector('#setting-perFile').value==='1'");
            await ui.CaptureAsync(Path.Combine(output,"settings.png"));
            // Run the shipped extension in a real browser DOM with fixture-only media and messaging.
            await ui.EvaluateAsync("""
                window.mediaRequests=[];window.fixtureSources=[{kind:'video',url:'https://example.test/720.mp4',label:'720p',qualityHeight:720,supported:true,estimatedBytes:1048576},{kind:'video',url:'https://example.test/2160.mp4',label:'4K',qualityHeight:2160,supported:true},{kind:'subtitle',url:'https://example.test/en.vtt',label:'English subtitles',supported:true}];
                chrome.runtime={getURL:path=>'https://syphon-ui.invalid/'+path,onMessage:{addListener(){},removeListener(){}},async sendMessage(m){if(m.type==='syphon-media-download'){mediaRequests.push(m);return {status:'accepted',message:'Added to Syphon.'};}return {sources:[]};}};
                chrome.storage={onChanged:{addListener(){},removeListener(){}},local:{async get(){return {detectVideos:true,preferredVideoQuality:720};},async set(){}}};
                window.SyphonMedia={collect(){return {options:fixtureSources.map(s=>({...s}))};},youtubeUrl(){return null;},resolve(v){return v;},sizeLabel(n){return n?'1 MB':'';}};
                const attach=Element.prototype.attachShadow;Element.prototype.attachShadow=function(o){const root=attach.call(this,o);if(this.hasAttribute('data-syphon-video-button'))window.extensionRoot=root;return root;};
                const video=document.createElement('video');video.style.cssText='position:fixed;left:100px;top:100px;width:640px;height:400px;background:#111';document.body.append(video);
                """);
            await ui.EvaluateAsync(File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"extensions/syphon/video-button.js")));
            await Until("!!window.extensionRoot?.querySelector('.trigger')");
            await Until("extensionRoot.querySelector('.trigger-mark').complete&&extensionRoot.querySelector('.trigger-mark').naturalWidth===64");
            await Assert("extensionRoot.querySelector('.trigger').textContent==='Syphon this'&&extensionRoot.querySelector('.trigger-mark').getAttribute('aria-hidden')==='true'&&extensionRoot.querySelector('.trigger').getAttribute('aria-expanded')==='false'");
            await Assert("(()=>{const s=getComputedStyle(extensionRoot.querySelector('.trigger'));return s.backgroundColor==='rgb(8, 8, 11)'&&s.backgroundImage.includes('radial-gradient')&&parseFloat(s.height)>=38;})()");
            await ui.CaptureAsync(Path.Combine(output,"extension-video-trigger.png"));
            await ui.EvaluateAsync("extensionRoot.querySelector('.trigger').click()");await Until("extensionRoot.querySelectorAll('.option').length===4");
            await Assert("extensionRoot.querySelector('.confirm').disabled&&mediaRequests.length===0");
            await Assert("!extensionRoot.querySelector('.trigger').hidden&&!extensionRoot.querySelector('.panel').textContent.includes('Browser stream detected')&&!extensionRoot.querySelector('.panel').textContent.includes('Select a quality')");
            await Assert("extensionRoot.querySelector('.trigger-mark').naturalWidth===64&&extensionRoot.querySelector('.trigger-mark').getBoundingClientRect().width===22");
            await ui.EvaluateAsync("extensionRoot.querySelector('.option').click()");
            await Assert("!extensionRoot.querySelector('.confirm').disabled&&mediaRequests.length===0&&extensionRoot.querySelector('.option').getAttribute('aria-pressed')==='true'");
            await Assert("extensionRoot.querySelector('.confirm').getBoundingClientRect().top>extensionRoot.querySelector('.option:last-of-type').getBoundingClientRect().top");
            await ui.CaptureAsync(Path.Combine(output,"extension-popup.png"));
            await ui.EvaluateAsync("extensionRoot.querySelector('.confirm').click()");await Until("mediaRequests.length===1");
            await Assert("mediaRequests[0].url==='https://example.test/720.mp4'&&mediaRequests[0].showOptions===true");
            await ui.EvaluateAsync("[...extensionRoot.querySelectorAll('.option')].find(b=>b.textContent==='Convert audio to MP3').click();extensionRoot.querySelector('.confirm').click()");await Until("mediaRequests.length===2");
            await Assert("mediaRequests[1].format==='mp3'");
            await ui.EvaluateAsync("syphonVideoController.stop()");
            var popup=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"extensions/syphon/popup.html"));
            var popupMain=popup[popup.IndexOf("<main>")..(popup.IndexOf("</main>")+7)];
            // This fixture is served from the desktop asset host, whose emblem is boxed.
            // Use its transparent v3 export to represent the extension's own header asset.
            popupMain=popupMain.Replace("src=\"brand/emblem.png\"", "src=\"brand/icon-64.png\"");
            var popupCss=File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"ui/desktop/theme-tokens.css"))+File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"extensions/syphon/popup.css")).Replace("fonts/", "");
            await ui.EvaluateAsync($"document.head.replaceChildren();document.body.innerHTML={JsonSerializer.Serialize(popupMain)};const style=document.createElement('style');style.textContent={JsonSerializer.Serialize(popupCss)};document.head.append(style);document.querySelector('#status').textContent='Added to Syphon. Your download is starting.';document.querySelector('#check').hidden=false;document.querySelector('#clear').hidden=false;");
            window.Width=500;window.Height=730;await Task.Delay(150);
            await Assert("document.querySelectorAll('input[type=checkbox]').length===3&&getComputedStyle(document.querySelector('#status')).color==='rgb(238, 238, 244)'");
            await ui.EvaluateAsync("chrome.runtime.id='fixture';chrome.permissions={async contains(){return true;}};chrome.storage.local.get=async key=>key==='lastHandoff'?{lastHandoff:{request:{action:'status',requestId:'fixture'},response:{status:'accepted',message:'Added to Syphon. Your download is starting.'}}}:{captureDownloads:true,detectVideos:true,captureStatus:'Sent to Syphon. The browser download was cancelled after Syphon saved the task. Progress and errors appear in the app.'};");
            var popupScript=System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(Path.Combine(Environment.CurrentDirectory,"extensions/syphon/popup.js")),@"^import .*;\r?$","",System.Text.RegularExpressions.RegexOptions.Multiline);
            await ui.EvaluateAsync("(async()=>{const VIDEO_PERMISSION={},SIGNIN_PERMISSION={};"+popupScript+"})()");
            await Until("!document.querySelector('#capture-details').hidden&&document.querySelector('#handoff-title').textContent==='Added to Syphon'");
            await Until("[...document.querySelectorAll('.brand img')].every(i=>i.complete&&i.naturalWidth>0)");
            await Assert("!document.querySelector('#capture-details').open&&document.querySelector('#capture-status').getBoundingClientRect().height===0");
            await Assert("document.querySelector('#connection-label').textContent==='App replied'&&document.querySelector('#capture-downloads').checked&&document.querySelector('#detect-videos').checked&&!document.querySelector('#browser-signin').checked");
            await Assert("document.documentElement.scrollWidth<=document.documentElement.clientWidth&&document.querySelectorAll('.feature-row').length===3&&[...document.querySelectorAll('.feature-switch')].every(e=>!e.disabled)");
            await ui.CaptureAsync(Path.Combine(output,"extension-menu.png"));
            window.Width=420;await Task.Delay(150);
            await Assert("document.documentElement.scrollWidth<=document.documentElement.clientWidth&&[...document.querySelectorAll('.feature-row')].every(e=>e.scrollWidth<=e.clientWidth+1)");
            await ui.CaptureAsync(Path.Combine(output,"extension-menu-narrow.png"));
            await ui.EvaluateAsync("document.querySelector('#browser-signin').closest('.feature').querySelector('details').open=true;document.querySelector('#capture-details').open=true");
            await Assert("document.querySelector('#signin-help').getBoundingClientRect().height>0&&document.querySelector('#capture-status').getBoundingClientRect().height>0&&document.documentElement.scrollWidth<=innerWidth");
            await ui.CaptureAsync(Path.Combine(output,"extension-menu-disclosures.png"));
        }finally{window.Close();ui.Dispose();}
        var dialog=new DownloadOptionsDialog("Fixture","installer.exe",Path.GetTempPath(),null,1048576,false,"https://example.test/installer.exe",true){Left=-10000,Top=-10000,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        try {
            if(!dialog.Topmost)throw new Exception("HTML New Download must stay above other windows.");
            dialog.Show();var form=dialog.HtmlSurface!;var end=DateTime.UtcNow.AddSeconds(20);while(!form.IsReady){if(form.InitializationError!=null||DateTime.UtcNow>end)throw new Exception(form.InitializationError??"Dialog timeout");await Task.Delay(30);}form.Publish();
            await Task.Delay(400);
            if(await form.EvaluateAsync("!!document.querySelector('#confirm-download')&&!document.querySelector('.download-advanced').open&&document.querySelector('#connections').offsetParent===null")!="true")throw new Exception("Advanced options must start collapsed: "+await form.EvaluateAsync("window.__syphonError||''"));
            if(await form.EvaluateAsync("document.querySelector('.resolved-file').textContent.includes('Original file')&&!document.body.textContent.includes('5.2 GB')&&!document.body.textContent.includes('Â')")!="true")throw new Exception("Resolved file details were replaced by template text.");
            await form.CaptureAsync(Path.Combine(output,"new-download.png"));
            await form.EvaluateAsync("document.querySelector('.download-advanced').open=true");await Task.Delay(250);
            if(await form.EvaluateAsync("document.querySelector('#connections').parentElement.querySelector('.syphon-select-trigger').offsetParent!==null&&document.documentElement.scrollWidth<=innerWidth")!="true")throw new Exception("Expanded options are inaccessible or overflow: "+await form.EvaluateAsync("JSON.stringify({error:window.__syphonError,width:innerWidth,scroll:document.documentElement.scrollWidth})"));
            await form.CaptureAsync(Path.Combine(output,"new-download-advanced.png"));
        }finally{dialog.Close();}
    }
}
