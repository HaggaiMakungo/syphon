using System.IO;
using System.Reflection;
using System.Windows;
using Syphon.BrowserBridge;
using Syphon.Desktop;
using Syphon.TestServer;

namespace Syphon.Desktop.SmokeTests;
internal static class TrayStartupSmokeTests
{
    internal static int Run(){
        var root=Path.Combine(Path.GetTempPath(),"Syphon.TrayStartup",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"download-preferences.json"),"{\"ShowFloatingWindow\":false,\"CloseToTray\":false}");
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var exit=1;
        app.Startup+=async(_,_)=>{
            var pipe="Syphon.TrayStartup."+Guid.NewGuid().ToString("N");
            var window=new MainWindow(root,pipe){Left=-10000,Top=-10000};
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try{
                await using var server=await FixtureServer.StartAsync();
                await (Task)typeof(MainWindow).GetMethod("StartAsync",flags)!.Invoke(window,null)!;
                if(!window.IsReady||window.IsVisible)throw new Exception("Startup must initialize IPC without showing the manager.");
                var request=new BrowserRequest(1,"download",Guid.NewGuid(),new Uri(server.BaseAddress,"resume?size=65536").AbsoluteUri,ShowOptions:true);
                var pending=BrowserPipeClient.SendAsync(pipe,request,TimeSpan.FromSeconds(15),expectedServerExecutable:Environment.ProcessPath);
                var deadline=DateTime.UtcNow.AddSeconds(10);Window? dialog=null;
                while(dialog is null){dialog=app.Windows.Cast<Window>().FirstOrDefault(w=>w is DownloadOptionsDialog&&w.IsVisible);if(DateTime.UtcNow>deadline)throw new Exception("Download dialog did not open from the hidden manager.");await Task.Delay(30);}
                if(window.IsVisible||dialog.Owner==window)throw new Exception("New Download exposed or attached to the hidden manager.");
                var surface=((DownloadOptionsDialog)dialog).HtmlSurface!;
                deadline=DateTime.UtcNow.AddSeconds(15);while(!surface.IsReady){if(DateTime.UtcNow>deadline)throw new Exception("New Download UI did not initialize.");await Task.Delay(50);}
                await Task.Delay(200);
                if(await surface.EvaluateAsync("Math.abs(innerHeight-document.querySelector('.dialog-window').getBoundingClientRect().height)<=1.5")!="true")throw new Exception("New Download surface mismatch: "+await surface.EvaluateAsync("JSON.stringify({viewport:innerHeight,root:document.querySelector('.dialog-window').getBoundingClientRect().height,wrapper:document.querySelector('#app>div').getBoundingClientRect().height,radius:getComputedStyle(document.querySelector('.dialog-window')).borderRadius})"));
                dialog.Close();await pending;
                if(window.IsVisible)throw new Exception("Dismissing New Download restored the manager.");
                var accepted=await BrowserPipeClient.SendAsync(pipe,new BrowserRequest(1,"download",Guid.NewGuid(),new Uri(server.BaseAddress,"resume?size=65536").AbsoluteUri),TimeSpan.FromSeconds(5),expectedServerExecutable:Environment.ProcessPath);
                if(accepted.Status!="accepted"||window.IsVisible)throw new Exception("Automatic download exposed the manager.");
                var opened=await BrowserPipeClient.SendAsync(pipe,new BrowserRequest(1,"activate",Guid.NewGuid()),TimeSpan.FromSeconds(5),expectedServerExecutable:Environment.ProcessPath);
                if(opened.Status!="ready"||!window.IsVisible)throw new Exception("Explicit Open did not restore the manager.");
                deadline=DateTime.UtcNow.AddSeconds(15);while(window.HtmlSurface?.IsReady!=true){if(DateTime.UtcNow>deadline)throw new Exception("Explicitly opened manager did not initialize its UI.");await Task.Delay(50);}
                Console.WriteLine("PASS hidden startup: IPC ready, only New Download visible, dismissal and automatic downloads stay hidden, explicit Open restores manager.");exit=0;
            }catch(Exception ex){Console.Error.WriteLine(ex);}finally{window.Close();app.Shutdown(exit);}
        };app.Run();return exit;
    }
}
