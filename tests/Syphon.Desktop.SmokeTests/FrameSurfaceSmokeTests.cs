using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Syphon.Desktop;
using Syphon.Engine;
using Syphon.Core;
namespace Syphon.Desktop.SmokeTests;
internal static class FrameSurfaceSmokeTests {
 [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out int value,int size);
 internal static int Run(string[] args){var live=args.Contains("--live");
  var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var result=1;
  app.Startup+=async(_,_)=>{
   var root=Path.Combine(Path.GetTempPath(),"Syphon.FrameSurfaces",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"download-preferences.json"),"{\"CloseToTray\":false,\"ShowFloatingWindow\":false}");
   var main=new MainWindow(root){Left=-10000,Top=-10000,ShowInTaskbar=false,ShowActivated=false};var checks=new List<object>();var opened=new List<Window>();
   Window Create(string type,params object[] args)=>(Window)Activator.CreateInstance(typeof(MainWindow).Assembly.GetType("Syphon.Desktop."+type)!,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,args,null)!;
   try{
    main.Show();var until=DateTime.UtcNow.AddSeconds(25);while(main.HtmlSurface?.IsReady!=true){if(DateTime.UtcNow>until)throw new Exception("Frame surface fixture did not initialize.");await Task.Delay(50);}
    var task=new StoredDownload{State=DownloadState.Paused,Url="https://example.test/video.mp4",Destination=Path.Combine(root,"video.mp4"),PartPath=Path.Combine(root,"video.part")};
    var floating=new FloatingDownloadWindow(_=>Task.CompletedTask,_=>{},_=>Task.CompletedTask,()=>{},_=>{});floating.Update(task,[task]);
    var windows=new Window[]{main,floating,new DownloadOptionsDialog("Video","video.mp4",root,null,null,false),new RemoveDownloadDialog("video.mp4",true),Create("RestartDownloadDialog",main,"video.mp4"),Create("BrowserConnectionDialog",main),Create("TrayPopupWindow",(Action<string>)(_=>{}),(Func<object>)(()=>new{kind="tray",active=0,paused=0,rate=0}))};
    foreach(var window in windows){
     if(window!=main){window.ShowInTaskbar=false;window.ShowActivated=false;window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-10000;window.Top=-10000;if(live&&window.GetType().Name is "FloatingDownloadWindow" or "TrayPopupWindow"){window.Left=SystemParameters.WorkArea.Right-window.Width-32;window.Top=80;window.Title="Syphon — "+window.GetType().Name+" corner check";}opened.Add(window);window.Show();}
     await Task.Delay(200);var policy=typeof(MainWindow).Assembly.GetType("Syphon.Desktop.CustomWindowFrame")!.GetMethod("BorderPolicyResult",BindingFlags.Static|BindingFlags.NonPublic)!;
     var resultValue=policy.Invoke(null,[window]);var hr=resultValue is int value?value:-1;
     var passed=!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)||hr==0;
     checks.Add(new{window=window.GetType().Name,passed,requestedBorder="DWMWA_COLOR_NONE",result=hr});if(!passed)throw new Exception(window.GetType().Name+$" did not apply border suppression: HRESULT={hr:x8}.");
     if(live&&window.GetType().Name is "FloatingDownloadWindow" or "TrayPopupWindow")await Task.Delay(30000);if(window!=main)window.Close();
    }
    Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/outer-frame-surfaces.json",JsonSerializer.Serialize(new{passed=true,checks},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("PASS native border suppression across main, floating, New Download, Remove, Restart, browser setup and tray windows.");result=0;
   }catch(Exception ex){Console.Error.WriteLine(ex);}finally{foreach(var w in opened)if(w.IsVisible)w.Close();main.Close();app.Shutdown(result);}
  };app.Run();return result;
 }
}
