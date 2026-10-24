using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Syphon.Core;
using Syphon.Desktop;
using Syphon.Engine;
namespace Syphon.Desktop.SmokeTests;
internal static class DialogVisualTests
{
 private static IEnumerable<DependencyObject> Descendants(DependencyObject parent){for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}}
 public static int Run(string[] args){var app=new App();app.InitializeComponent();app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
 var output=Path.GetFullPath(args.FirstOrDefault()??"artifacts/first-update-dialogs");Directory.CreateDirectory(output);var temp=Path.Combine(Path.GetTempPath(),"Syphon.DialogVisual",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
 try{
 void Capture(Window window,string name){window.Left=-10000;window.Top=-10000;window.ShowInTaskbar=false;window.Show();window.UpdateLayout();
        if(name.EndsWith("-bottom")){var scroll=Descendants(window).OfType<ScrollViewer>().First();scroll.ScrollToEnd();window.UpdateLayout();}
        if(window.WindowStyle!=WindowStyle.None)throw new Exception(name+" has native chrome");if(((SolidColorBrush)window.Background).Color!=Color.FromRgb(8,8,11))throw new Exception(name+" differs from the shared surface palette");var image=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(output,name+".png")))png.Save(stream);if(name=="settings"){var scroll=Descendants(window).OfType<ScrollViewer>().First();scroll.ScrollToEnd();window.UpdateLayout();var bottom=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bottom.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bottom));using var bottomStream=File.Create(Path.Combine(output,"settings-bottom.png"));encoder.Save(bottomStream);if(scroll.VerticalOffset<=0)throw new Exception("Settings did not scroll to the Save controls.");}window.Close();}
 var settingsType=typeof(CompletionWindow).Assembly.GetType("Syphon.Desktop.UpdateSettingsWindow")!;var settings=(Window)Activator.CreateInstance(settingsType,BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{Path.Combine(temp,"preferences.json"),(Action)(()=>{}),(Action)(()=>{}),(Action<bool>)(_=>{})},null)!;Capture(settings,"settings");
 var duplicate=new DuplicateDownloadDialog("Example download.zip",true);Capture(duplicate,"duplicate");
 var file=Path.Combine(temp,"Example download.zip");File.WriteAllText(file,"fixture");Capture(new CompletionWindow(new StoredDownload {Url="https://example.test/download",Destination=file,PartPath=file+".part",State=DownloadState.Completed}),"completion");
 var countdown=DialogAppearance.Countdown(out var text);text.Text="Queue finished. Sleep in 30 seconds.\nNew work cancels this action.";Capture(countdown,"countdown");
 Capture(DialogAppearance.Update("0.2.0-beta.1","A new version of Syphon is ready.\n\n• Shared download windows\n• Improved recovery and file actions",()=>{},()=>{},()=>{}),"update");
 Console.WriteLine("PASS dialog visuals: settings, duplicate, completion, countdown and update share custom chrome and palette; no external actions executed.");return 0;
 }finally{Directory.Delete(temp,true);app.Shutdown();}}
}
