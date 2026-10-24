using System.Windows;
using System.Windows.Controls;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;
namespace Syphon.Desktop.SmokeTests;
internal static class SharedFloatingSmokeTests
{
    public static int Run() {
        var app = new App(); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var tasks = Enumerable.Range(0, 6).Select(i => new StoredDownload { Id=Guid.NewGuid(), Url="https://example.test/file", Destination=System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"float-{i}.bin"), PartPath="unused.part", State=DownloadState.Downloading, Received=25, Total=100 }).ToArray();
        var window = new FloatingDownloadWindow(_=>Task.CompletedTask, _=>{}, _=>Task.CompletedTask, ()=>{}, _=>{}, false);
        try {
            window.Update(tasks[0], tasks);
            var rows = window.FindName("SharedDownloadRows") as Panel;
            if(rows?.Children.Count != 6) throw new Exception("Shared floating window must retain six independently controlled rows.");
            if(window.ResizeMode!=ResizeMode.CanResize) throw new Exception("Floating window cannot be resized.");
            if(!window.Title.Contains("6")) throw new Exception("Active download count is missing from window title.");
            var scroll = window.FindName("SharedDownloadScroll") as ScrollViewer;
            if(scroll is null || scroll.MaxHeight > 4*112) throw new Exception("Compact window must scroll after four downloads.");
            var fitted=new WindowGeometryStore(-10000,10000,10000,double.NaN).Fit(new Rect(0,0,1920,1080));
            if(fitted.Left<0||fitted.Top<0||fitted.Width>1920||fitted.Top+fitted.Height>1080)throw new Exception("Saved geometry was not clamped to a visible work area.");
            var file=System.IO.Path.GetTempFileName();
            try{var data=CompletionWindow.CreateFileDrop(file);if(data.GetData(DataFormats.FileDrop) is not string[] paths||paths.Length!=1||paths[0]!=System.IO.Path.GetFullPath(file)||data.GetDataPresent(DataFormats.Text))throw new Exception("Completion drag is not an actual file drop.");var completion=new CompletionWindow(tasks[0] with {State=DownloadState.Completed,Destination=file});completion.Show();completion.UpdateLayout();
                var output=System.IO.Path.GetFullPath("artifacts/first-update-floating");System.IO.Directory.CreateDirectory(output);
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)completion.ActualWidth,(int)completion.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(completion);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=System.IO.File.Create(System.IO.Path.Combine(output,"completion.png")))png.Save(stream);completion.Close();}finally{System.IO.File.Delete(file);}
            try{CompletionWindow.CreateFileDrop(file);throw new Exception("Missing drag file was accepted.");}catch(System.IO.FileNotFoundException){}
            Console.WriteLine("PASS shared floating: six independent rows, compact overflow, resize, active count."); return 0;
        } finally {window.Close();app.Shutdown();}
    }
}


