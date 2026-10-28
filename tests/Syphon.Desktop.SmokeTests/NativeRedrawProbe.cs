using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Syphon.Desktop;
using Syphon.Core;
using Syphon.Engine;
using Syphon.TestServer;
namespace Syphon.Desktop.SmokeTests;
internal static class NativeRedrawProbe {
 internal static int Run(string[] args){
  var live=args.Contains("--live");var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var code=1;
  app.Startup+=async(_,_)=>{
   var root=Path.Combine(Path.GetTempPath(),"Syphon.Redraw",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"download-preferences.json"),"{\"ShowFloatingWindow\":false,\"CloseToTray\":false}");
   var window=new MainWindow(root){Left=live?80:-10000,Top=live?80:-10000,ShowInTaskbar=live,Title="Syphon — isolated activity check"};
   try{
    await using var server=await FixtureServer.StartAsync();window.Show();while(window.HtmlSurface?.IsReady!=true||!window.IsReady)await Task.Delay(50);await Task.Delay(1000);
    var counts=new Dictionary<string,int>();var names=new Dictionary<int,string>{{0x80,"setIcon"},{0x85,"nonClientPaint"},{0x83,"nonClientCalculate"},{0x47,"windowPosition"},{0x14,"eraseBackground"},{0xf,"paint"}};
    var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
    source.AddHook((IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled)=>{if(names.TryGetValue(m,out var n))counts[n]=counts.GetValueOrDefault(n)+1;if(m==0x80){var slot=w==IntPtr.Zero?"captionIcon":"taskbarIcon";counts[slot]=counts.GetValueOrDefault(slot)+1;}return IntPtr.Zero;});
    await Task.Delay(2000);var idle=new Dictionary<string,int>(counts);counts.Clear();
    var coordinator=(DownloadCoordinator)typeof(MainWindow).GetField("coordinator",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
    var task=coordinator.Add(new Uri(server.BaseAddress,"resume?size=16777216"),Path.Combine(root,"Isolated test download.bin"));coordinator.SetSpeedLimit(task.Id,262144);coordinator.Resume(task.Id);counts.Clear();
    await Task.Delay(2000);var taskbarWrites=counts.GetValueOrDefault("taskbarIcon");var captionWrites=counts.GetValueOrDefault("captionIcon");if(taskbarWrites<15||captionWrites!=0)throw new Exception($"Icon isolation failed: taskbar={taskbarWrites}, caption={captionWrites}");
    var active=new Dictionary<string,int>(counts);if(live)await Task.Delay(30000);
    var chart=(RechartsSpeedChart)typeof(MainWindow).GetField("ManagerSpeedChart",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;var report=new{idle,activity=active,nativeChartVisible=chart.IsVisible,nativeChartReady=chart.IsChartReady,htmlReady=window.HtmlSurface.IsReady};Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/native-redraw-probe.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));code=0;
   }catch(Exception ex){Console.Error.WriteLine(ex);}finally{window.Close();app.Shutdown(code);}
  };app.Run();return code;
 }
}
