using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.IO;
namespace Syphon.Desktop;

// A dedicated message window keeps the icon alive when the manager is hidden.
internal sealed class SystemTray : IDisposable
{
    private readonly HwndSource source;
    private readonly Action<string> command;
    private readonly Func<object> model;
    private TrayPopupWindow? popup;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly nint icon;
    private readonly bool ownsIcon;
    private nint activityIcon;
    private bool disposed;
    internal bool IsRegistered { get; private set; }
    internal SystemTray(Action<string> command,Func<object> model)
    {
        this.command = command;
        this.model=model;
        source = new HwndSource(new HwndSourceParameters("Syphon tray") { Width=0, Height=0, WindowStyle=0 });
        source.AddHook(Message);
        icon = ExtractIcon(0, Environment.ProcessPath!, 0);
        ownsIcon = icon != 0 && icon != 1;
        if (!ownsIcon) icon = LoadIcon(0, (nint)32512);
        Add();
    }
    private NotifyData Data() => new() { Size=(uint)Marshal.SizeOf<NotifyData>(), Window=source.Handle, Id=1, Flags=7, Callback=0x8001, Icon=activityIcon!=0?activityIcon:icon, Tip="Syphon", Info="", Title="" };
    internal void UpdateIcon(BitmapSource frame)
    {
        if(disposed)return;
        using var png=new MemoryStream();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));encoder.Save(png);
        var bytes=png.ToArray();var replacement=CreateIconFromResourceEx(bytes,(uint)bytes.Length,true,0x30000,32,32,0);
        if(replacement==0)return;
        var previous=activityIcon;activityIcon=replacement;var data=Data();Shell_NotifyIcon(1,ref data);if(previous!=0)DestroyIcon(previous);
    }
    private void Add() { var data=Data(); RegistrationCompleted(Shell_NotifyIcon(0, ref data)); }
    private void RegistrationCompleted(bool registered)
    {
        IsRegistered=registered;
        // Explorer can restart while the manager is hidden. Keep it reachable
        // if Windows cannot restore the notification icon.
        if (!registered && !disposed) command("open");
    }
    private nint Message(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)message==taskbarCreated && !disposed) Add();
        if(message==0x8001) {
            if ((int)lParam==0x203) command("open");
            if ((int)lParam==0x205 || (int)lParam==0x7b) {
                SetForegroundWindow(source.Handle);
                source.Dispatcher.BeginInvoke(()=>{popup?.Close();popup=new TrayPopupWindow(command,model);popup.Closed+=(_,_)=>popup=null;popup.Show();popup.Activate();});
            }
            handled=true;
        }
        return 0;
    }
    public void Dispose()
    {
        if(disposed)return; disposed=true;
        var data=Data(); Shell_NotifyIcon(2,ref data); IsRegistered=false;
        popup?.Close();source.Dispose(); if(ownsIcon) DestroyIcon(icon);if(activityIcon!=0)DestroyIcon(activityIcon);
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    private struct NotifyData {
        public uint Size; public nint Window; public uint Id; public uint Flags; public uint Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)] public string Tip;
        public uint State; public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message,ref NotifyData data);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern nint ExtractIcon(nint instance,string file,uint index);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreateIconFromResourceEx(byte[] bytes,uint size,[MarshalAs(UnmanagedType.Bool)]bool icon,uint version,int width,int height,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint LoadIcon(nint instance,nint name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window,uint message,nint wParam,nint lParam);
    internal void Refresh()=>popup?.Refresh();
}
