using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using Syphon.Core;
using Syphon.Engine;

namespace Syphon.Desktop;

/// <summary>A charging activity signal; deliberately independent of byte percentage.</summary>
public sealed class DownloadActivityIcon : IDisposable
{
    private readonly Window window;
    private readonly Dictionary<BitmapSource,IntPtr> nativeIcons = [];
    private IntPtr originalSmall,originalLarge;
    private bool nativePublished;
    private BitmapSource? nativeCurrent;
    private readonly Action<BitmapSource>? trayUpdate;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly BitmapSource[] charging;
    private readonly BitmapSource[] shimmer;
    private readonly BitmapSource idle;
    private readonly BitmapSource paused;
    private readonly BitmapSource complete;
    private readonly Dictionary<Guid, DownloadState> previous = [];
    private readonly System.Diagnostics.Stopwatch clock = new();
    private bool initialized, disposed;
    private BitmapSource? current;
    public string State { get; private set; } = "idle";
    public const int CycleMilliseconds = 1000;
    public int PixelSize { get; }
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    public DownloadActivityIcon(Window window, Action<BitmapSource>? trayUpdate = null)
    {
        this.window = window; this.trayUpdate = trayUpdate;
        window.SourceInitialized += NativeWindowReady;
        var dpi = GetDpiForSystem();
        PixelSize = Math.Max(32, GetSystemMetricsForDpi(11, dpi));
        var size = PixelSize;
        var stride = size * 4;
        // Animate the original artwork itself, retaining its antialiased alpha.
        var artwork = new BitmapImage();
        artwork.BeginInit();
        artwork.UriSource = new Uri("pack://application:,,,/Syphon;component/Assets/Brand/syphon_window_icon.png");
        artwork.DecodePixelWidth = size;
        artwork.CacheOption = BitmapCacheOption.OnLoad;
        artwork.EndInit();
        var original = new FormatConvertedBitmap(artwork, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[size * size * 4];
        original.CopyPixels(pixels, stride, 0);
        BitmapSource Frame(int fill, bool dim = false, int shine = -99)
        {
            var bytes = (byte[])pixels.Clone();
            for (var y=0;y<size;y++) for (var x=0;x<size;x++)
            {
                var at=(y*size+x)*4;
                if(bytes[at+3]==0)continue;
                var brightness=Math.Max(bytes[at],Math.Max(bytes[at+1],bytes[at+2]));
                if(dim){
                    var gray=(byte)Math.Round(bytes[at]*.114+bytes[at+1]*.587+bytes[at+2]*.299);
                    bytes[at]=bytes[at+1]=bytes[at+2]=(byte)(gray*.72);
                }else{
                    var factor=y<size-fill?.42:1.0;
                    var highlight=brightness>50?Math.Max(0,1-Math.Abs(x-y/2.0-shine)/2.5)*.65:0;
                    for(var channel=0;channel<3;channel++)bytes[at+channel]=(byte)Math.Round(bytes[at+channel]*factor+(255-bytes[at+channel]*factor)*highlight);
                }
            }
            var bitmap=BitmapSource.Create(size,size,96,96,PixelFormats.Bgra32,null,bytes,stride);bitmap.Freeze();return bitmap;
        }
        charging=Enumerable.Range(0,20).Select(i=>Frame(Math.Min(size,(int)Math.Round(i/17.0*size)))).ToArray();
        shimmer=Enumerable.Range(0,17).Select(i=>Frame(size,shine:(int)Math.Round((i*3-16)*size/32.0))).ToArray();
        idle=Frame(size);complete=Frame(size);paused=Frame(0,dim:true);
        timer.Tick+=Tick;SetFrame(idle);
    }
    public void Update(IEnumerable<StoredDownload> source)
    {
        if(disposed)return;
        var tasks=source.ToArray();
        var running=tasks.Any(t=>t.State is DownloadState.Starting or DownloadState.Downloading or DownloadState.Assembling or DownloadState.Publishing or DownloadState.RetryWaiting or DownloadState.Pausing);
        var justCompleted=initialized&&tasks.Any(t=>t.State==DownloadState.Completed&&previous.TryGetValue(t.Id,out var before)&&before!=DownloadState.Completed);
        previous.Clear();foreach(var task in tasks)previous[task.Id]=task.State;initialized=true;
        if(running){if(State!="downloading"){State="downloading";clock.Restart();}if(SystemParameters.ClientAreaAnimation){timer.Start();Tick(null,EventArgs.Empty);}else{timer.Stop();SetFrame(complete);}return;}
        if(State=="complete"&&timer.IsEnabled)return;
        if(justCompleted&&!tasks.Any(t=>t.State is DownloadState.Paused or DownloadState.Queued)){
            State="complete";clock.Restart();if(SystemParameters.ClientAreaAnimation){timer.Start();Tick(null,EventArgs.Empty);}else{timer.Stop();SetFrame(complete);}return;
        }
        timer.Stop();State=tasks.Any(t=>t.State is DownloadState.Paused or DownloadState.Queued)?"paused":"idle";SetFrame(State=="paused"?paused:idle);
    }
    private void Tick(object? sender,EventArgs args)
    {
        if(!SystemParameters.ClientAreaAnimation){timer.Stop();SetFrame(complete);return;}
        if(State=="downloading")SetFrame(charging[(int)(clock.ElapsedMilliseconds%CycleMilliseconds)/50]);
        else if(State=="complete"){
            var index=(int)(clock.ElapsedMilliseconds/50);if(index>=shimmer.Length){timer.Stop();SetFrame(complete);}else SetFrame(shimmer[index]);
        }
    }
    private void NativeWindowReady(object? sender,EventArgs e){if(current is not null)PublishNativeIcon(current);}
    private IntPtr NativeIcon(BitmapSource frame){
        if(nativeIcons.TryGetValue(frame,out var icon))return icon;
        using var png=new MemoryStream();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));encoder.Save(png);var bytes=png.ToArray();
        icon=CreateIconFromResourceEx(bytes,(uint)bytes.Length,true,0x30000,PixelSize,PixelSize,0);if(icon!=IntPtr.Zero)nativeIcons.Add(frame,icon);return icon;
    }
    private void PublishNativeIcon(BitmapSource frame){
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
        if(ReferenceEquals(nativeCurrent,frame))return;
        var icon=NativeIcon(frame);if(icon==IntPtr.Zero)return;
        // ICON_SMALL is the native caption icon. Changing it every frame can
        // repaint the window's nonclient surface. Keep that slot steady and
        // animate only ICON_BIG, used by the taskbar and Alt+Tab, plus the tray.
        if(!nativePublished){var small=NativeIcon(complete);if(small!=IntPtr.Zero)originalSmall=SendMessage(handle,0x80,IntPtr.Zero,small);}
        var oldLarge=SendMessage(handle,0x80,(IntPtr)1,icon);
        nativeCurrent=frame;
        if(!nativePublished){originalLarge=oldLarge;nativePublished=true;}
    }
    private void SetFrame(BitmapSource frame){if(ReferenceEquals(current,frame))return;current=frame;PublishNativeIcon(frame);trayUpdate?.Invoke(frame);}
    public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Tick-=Tick;clock.Stop();window.SourceInitialized-=NativeWindowReady;var handle=new WindowInteropHelper(window).Handle;if(nativePublished&&handle!=IntPtr.Zero){SendMessage(handle,0x80,IntPtr.Zero,originalSmall);SendMessage(handle,0x80,(IntPtr)1,originalLarge);}foreach(var icon in nativeIcons.Values)DestroyIcon(icon);nativeIcons.Clear();}
    [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]private static extern IntPtr CreateIconFromResourceEx(byte[] bytes,uint size,bool icon,uint version,int width,int height,uint flags);
    [DllImport("user32.dll")]private static extern bool DestroyIcon(IntPtr icon);
}
