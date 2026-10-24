using System.IO;
using System.Text.Json;
using System.Windows;
namespace Syphon.Desktop;
public sealed record WindowGeometryStore(double Left,double Top,double Width,double Height)
{
    public WindowGeometryStore Fit(Rect area,double minWidth=360,double minHeight=240)
    {
        var w=double.IsFinite(Width)?Math.Clamp(Width,Math.Min(minWidth,area.Width),area.Width):Math.Min(480,area.Width);
        var h=double.IsFinite(Height)?Math.Clamp(Height,Math.Min(minHeight,area.Height),area.Height):Math.Min(560,area.Height);
        return new(double.IsFinite(Left)?Math.Clamp(Left,area.Left,area.Right-w):area.Right-w,
            double.IsFinite(Top)?Math.Clamp(Top,area.Top,area.Bottom-h):area.Bottom-h,w,h);
    }
    public static void Attach(Window window,string path)
    {
        try{if(File.Exists(path)&&JsonSerializer.Deserialize<WindowGeometryStore>(File.ReadAllText(path)) is {} saved){var g=saved.Fit(SystemParameters.WorkArea,window.MinWidth,window.MinHeight);window.Left=g.Left;window.Top=g.Top;window.Width=g.Width;window.Height=g.Height;}}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException){}
        void Save(){if(window.WindowState!=WindowState.Normal||!double.IsFinite(window.Left)||!double.IsFinite(window.Top))return;try{Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);File.WriteAllText(path,JsonSerializer.Serialize(new WindowGeometryStore(window.Left,window.Top,window.Width,window.Height)));}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){}}
        window.Closed+=(_,_)=>Save();window.IsVisibleChanged+=(_,_)=>{if(!window.IsVisible)Save();};
    }
}
