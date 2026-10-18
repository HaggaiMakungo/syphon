using System.IO;
using System.Windows;
using Syphon.Desktop;
using Syphon.Engine;
using Syphon.Core;
using Syphon.TestServer;
using Syphon.BrowserBridge;
namespace Syphon.Desktop.SmokeTests;
internal static class TraySmokeTests
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "Syphon.Tray.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var preferences=typeof(MainWindow).Assembly.GetType("Syphon.Desktop.DownloadPreferences")!;
        var load=preferences.GetMethod("Load")!;
        var defaults=load.Invoke(null,[Path.Combine(root,"missing.json")])!;
        if(!(bool)preferences.GetProperty("CloseToTray")!.GetValue(defaults)! || (bool)preferences.GetProperty("MinimizeToTray")!.GetValue(defaults)!) throw new Exception("Close must default to tray; minimize must keep its ordinary default.");
        var legacyPath=Path.Combine(root,"legacy.json"); File.WriteAllText(legacyPath,"{}");
        if(!(bool)preferences.GetProperty("CloseToTray")!.GetValue(load.Invoke(null,[legacyPath]))!) throw new Exception("Preferences without a tray choice must use the new close default.");
        File.WriteAllText(legacyPath,"{\"CloseToTray\":false}");
        if((bool)preferences.GetProperty("CloseToTray")!.GetValue(load.Invoke(null,[legacyPath]))!) throw new Exception("An explicit saved close preference must be respected.");
        File.WriteAllText(Path.Combine(root, "download-preferences.json"), "{\"CloseToTray\":true,\"MinimizeToTray\":true}");
        var persisted=load.Invoke(null,[Path.Combine(root,"download-preferences.json")])!;
        if(!(bool)preferences.GetProperty("CloseToTray")!.GetValue(persisted)! || !(bool)preferences.GetProperty("MinimizeToTray")!.GetValue(persisted)!) throw new Exception("Tray preferences did not persist.");
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown; app.MainWindow = new Window();
        var code = 1;
        app.Startup += async (_, _) => {
            var pipe="Syphon.Tray.Tests."+Guid.NewGuid().ToString("N");
            var window = new MainWindow(root, pipe, htmlUi:false) { Left=-10000, Top=-10000, ShowInTaskbar=false };
            try {
                await using var server = await FixtureServer.StartAsync();
                window.Show();
                var deadline=DateTime.UtcNow.AddSeconds(20);
                while(!window.IsReady && DateTime.UtcNow<deadline) await Task.Delay(30);
                var enable=typeof(MainWindow).GetMethod("EnableTray", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                if(enable is null) throw new Exception("Tray lifecycle is missing.");
                enable.Invoke(window, null);
                var coordinator=(DownloadCoordinator)typeof(MainWindow).GetField("coordinator", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                var task=coordinator.Add(new Uri(server.BaseAddress,"resume?size=8388608"),Path.Combine(root,"hidden.bin")); coordinator.Resume(task.Id);
                window.Close(); await Task.Delay(100);
                if(window.IsVisible || !window.IsReady) throw new Exception("Close to tray must hide the window and keep its coordinator alive.");
                await coordinator.WaitForIdleAsync(task.Id);
                if(coordinator.Get(task.Id).State!=DownloadState.Completed || !File.Exists(Path.Combine(root,"hidden.bin"))) throw new Exception("Hidden download did not finish.");
                var reply=await BrowserPipeClient.SendAsync(pipe,new BrowserRequest(1,"activate",Guid.NewGuid()),TimeSpan.FromSeconds(3),expectedServerExecutable:Environment.ProcessPath);
                if(reply.Status!="ready")throw new Exception("Hidden browser IPC was not ready.");
                if(!window.IsVisible) throw new Exception("Open must restore hidden window.");
                window.WindowState=WindowState.Minimized;
                if(window.IsVisible) throw new Exception("Minimize to tray must hide the manager.");
                var tray=typeof(MainWindow).GetField("tray",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                tray.GetType().GetMethod("RegistrationCompleted",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(tray,[false]);
                if(!window.IsVisible || window.WindowState==WindowState.Minimized) throw new Exception("Failed tray re-registration must restore the manager.");
                typeof(MainWindow).GetMethod("RestoreFromTray",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,null);
                var queued=coordinator.Add(new Uri(server.BaseAddress,"resume?size=8388608"),Path.Combine(root,"paused.bin"));
                var command=typeof(MainWindow).GetMethod("TrayCommand",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                command.Invoke(window,["resume"]); command.Invoke(window,["pause"]);
                deadline=DateTime.UtcNow.AddSeconds(10);
                while(coordinator.Get(queued.Id).State!=DownloadState.Paused && DateTime.UtcNow<deadline) await Task.Delay(30);
                if(coordinator.Get(queued.Id).State!=DownloadState.Paused) throw new Exception("Pause all tray command did not pause the resumed task.");
                command.Invoke(window,["resume"]); await coordinator.WaitForIdleAsync(queued.Id);
                if(coordinator.Get(queued.Id).State!=DownloadState.Completed) throw new Exception("Resume all tray command did not resume the task.");
                var closed=new TaskCompletionSource(); window.Closed+=(_,_)=>closed.TrySetResult();
                typeof(MainWindow).GetMethod("ExitFromTray", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,null);
                deadline=DateTime.UtcNow.AddSeconds(10);
                while(window.IsReady && DateTime.UtcNow<deadline) await Task.Delay(30);
                if(window.IsReady) throw new Exception("Exit must shut down the coordinator.");
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if(typeof(MainWindow).GetField("tray", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window) is not null) throw new Exception("Exit left its tray icon alive.");
                Console.WriteLine("PASS tray lifecycle: close hides, hidden download finishes, restore opens, exit disposes."); code=0;
            } catch(Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(); }
        };
        app.Run();
        try { Directory.Delete(root,true); } catch(IOException) { }
        return code;
    }
}
