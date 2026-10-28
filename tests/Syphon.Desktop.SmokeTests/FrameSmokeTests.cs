using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using Syphon.Desktop;
namespace Syphon.Desktop.SmokeTests;
internal static class FrameSmokeTests
{
 [StructLayout(LayoutKind.Sequential)]private struct Rect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")]private static extern int GetWindowRgn(IntPtr h,IntPtr r);
 [DllImport("user32.dll")]private static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("gdi32.dll")]private static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
 [DllImport("gdi32.dll")]private static extern IntPtr CreateRoundRectRgn(int l,int t,int r,int b,int w,int h);
 [DllImport("gdi32.dll")]private static extern bool EqualRgn(IntPtr a,IntPtr b);
 [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr h); [DllImport("gdi32.dll")]private static extern int GetRgnBox(IntPtr h,out Rect bounds);
 internal static int Run(string[] args){
  var target=Path.GetFullPath(args.FirstOrDefault()??"artifacts/beta20-frame-check.json");Directory.CreateDirectory(Path.GetDirectoryName(target)!);
  var directory=Path.Combine(Path.GetTempPath(),"Syphon.Frame",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"download-preferences.json"),"{\"CloseToTray\":false,\"ShowFloatingWindow\":false}");
  var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var exit=1;
  app.Startup+=async(_,_)=>{var window=new MainWindow(directory,"Syphon.Frame."+Guid.NewGuid().ToString("N")){Left=-10000,Top=-10000,ShowInTaskbar=false};var results=new List<object>();try{
   window.Show();var deadline=DateTime.UtcNow.AddSeconds(30);while(!window.IsReady||window.HtmlSurface?.IsReady!=true){if(DateTime.UtcNow>deadline)throw new TimeoutException("UI did not initialize");await Task.Delay(50);}
   async Task<bool> Check(string name){await Task.Delay(250);var handle=new WindowInteropHelper(window).Handle;GetWindowRect(handle,out var rect);var actual=CreateRectRgn(0,0,0,0);var kind=GetWindowRgn(handle,actual);var radius=(int)Math.Round(32*VisualTreeHelper.GetDpi(window).DpiScaleX);var expected=CreateRoundRectRgn(0,0,rect.Right-rect.Left+1,rect.Bottom-rect.Top+1,radius*2,radius*2);var matches=window.WindowState==WindowState.Maximized?kind==0:kind!=0&&EqualRgn(actual,expected);
var content=(HwndHost)window.HtmlSurface!.Children[0];GetWindowRect(content.Handle,out var childRect);var childActual=CreateRectRgn(0,0,0,0);var childKind=GetWindowRgn(content.Handle,childActual);var childExpected=CreateRoundRectRgn(rect.Left-childRect.Left,rect.Top-childRect.Top,rect.Right-childRect.Left+1,rect.Bottom-childRect.Top+1,radius*2,radius*2);var fullChild=CreateRectRgn(0,0,childRect.Right-childRect.Left+1,childRect.Bottom-childRect.Top+1);var childMatches=window.WindowState==WindowState.Maximized?(childKind==0||EqualRgn(childActual,fullChild)):childKind!=0&&EqualRgn(childActual,childExpected);DeleteObject(fullChild);GetRgnBox(childActual,out var actualChildBounds);DeleteObject(childActual);DeleteObject(childExpected);matches&=childMatches;
results.Add(new{name,matches,contentRegionKind=childKind,contentMatches=childMatches,childWidth=childRect.Right-childRect.Left,childHeight=childRect.Bottom-childRect.Top,childClip=new[]{actualChildBounds.Left,actualChildBounds.Top,actualChildBounds.Right,actualChildBounds.Bottom},regionKind=kind,width=rect.Right-rect.Left,height=rect.Bottom-rect.Top,radius,html=await window.HtmlSurface!.EvaluateAsync("JSON.stringify({shell:getComputedStyle(document.querySelector('.shell')).borderRadius,margin:getComputedStyle(document.body).margin,stats:document.querySelectorAll('.fullscreen-summary').length})")});DeleteObject(actual);DeleteObject(expected);return matches;}
   var success=await Check("opened");using(var animation=new DownloadActivityIcon(window)){var stableIcon=window.Icon;animation.Update([new Syphon.Engine.StoredDownload{Url="https://example.test/video",Destination="C:\\fixture.mp4",PartPath="C:\\fixture.part",State=Syphon.Core.DownloadState.Downloading}]);success&=await Check("charging-1");success&=await Check("charging-2");success&=ReferenceEquals(stableIcon,window.Icon);}window.Width+=60;window.Height+=40;success&=await Check("resized");window.WindowState=WindowState.Maximized;success&=await Check("maximized");window.WindowState=WindowState.Normal;success&=await Check("restored");File.WriteAllText(target,JsonSerializer.Serialize(new{passed=success,results},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(File.ReadAllText(target));exit=success?0:1;
  }catch(Exception ex){Console.Error.WriteLine(ex);}finally{window.Close();app.Shutdown(exit);}};app.Run();return exit;
 }
}
