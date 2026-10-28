using System.IO;
using System.Windows;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;

namespace Syphon.Desktop.SmokeTests;
internal static class Beta20ActivitySmokeTests
{
    public static int Run()
    {
        var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;app.MainWindow=new Window();var result=1;
        app.Startup+=async(_,_)=>{
            try{
                var window=new Window {Left=-10000,Top=-10000,ShowInTaskbar=false};var count=0;System.Windows.Media.Imaging.BitmapSource? last=null;using var animation=new DownloadActivityIcon(window,frame=>{count++;last=frame;});window.Show();var stableIcon=window.Icon;
                if(last is not {} icon || icon.PixelWidth!=animation.PixelSize || icon.PixelHeight!=animation.PixelSize)
                    throw new InvalidOperationException("Activity icon did not use the native DPI pixel size.");
                var task=new StoredDownload { Url="https://example.test/file",Destination="C:\\fixture.bin",PartPath="C:\\fixture.part",State=DownloadState.Downloading,Total=100,Received=34 };
                animation.Update([task]);var first=last;await Task.Delay(250);
                var animationDeadline=DateTime.UtcNow.AddSeconds(2);while(SystemParameters.ClientAreaAnimation&&ReferenceEquals(first,last)&&DateTime.UtcNow<animationDeadline)await Task.Delay(25);
                if(animation.State!="downloading"||DownloadActivityIcon.CycleMilliseconds!=1000)throw new InvalidOperationException("Incorrect activity state/cadence.");
                if(SystemParameters.ClientAreaAnimation&&ReferenceEquals(first,last))throw new InvalidOperationException("Charging icon did not move.");
                if(!ReferenceEquals(stableIcon,window.Icon))throw new InvalidOperationException("Charging rewrote WPF's window icon and invalidated the frame.");
                await Task.Delay(1100);if(animation.State!="downloading")throw new InvalidOperationException("Charging stopped after one cycle.");
                animation.Update([task with { State=DownloadState.Paused }]);var paused=count;await Task.Delay(200);if(count!=paused)throw new InvalidOperationException("Paused icon kept animating.");
                animation.Update([task]);animation.Update([task with { State=DownloadState.Completed }]);await Task.Delay(1100);animation.Update([task with { State=DownloadState.Completed }]);var done=count;await Task.Delay(250);if(count!=done)throw new InvalidOperationException("Completion kept shimmering.");
                animation.Update([task with { Id=Guid.NewGuid() }]);animation.Dispose();var disposed=count;await Task.Delay(200);if(count!=disposed)throw new InvalidOperationException("Disposed icon kept updating.");
                window.Close();Console.WriteLine($"PASS Beta 20: {animation.PixelSize}px native DPI activity icon; repeats at 1000ms without WPF frame invalidation, pause settles, completion stops, disposal stops updates.");result=0;
            }catch(Exception ex){Console.Error.WriteLine(ex);File.WriteAllText("artifacts/beta20-activity-error.txt",ex.ToString());}
            finally{app.Shutdown();}
        };app.Run();return result;
    }
}
