using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
namespace Syphon.Desktop;

// A dedicated message window keeps the icon alive when the manager is hidden.
internal sealed class SystemTray : IDisposable
{
    private readonly HwndSource source;
    private readonly Action<string> command;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly nint icon;
    private readonly bool ownsIcon;
    private bool disposed;
    internal bool IsRegistered { get; private set; }
    internal SystemTray(Action<string> command)
    {
        this.command = command;
        source = new HwndSource(new HwndSourceParameters("Syphon tray") { Width=0, Height=0, WindowStyle=0 });
        source.AddHook(Message);
        icon = ExtractIcon(0, Environment.ProcessPath!, 0);
        ownsIcon = icon != 0 && icon != 1;
        if (!ownsIcon) icon = LoadIcon(0, (nint)32512);
        Add();
    }
    private NotifyData Data() => new() { Size=(uint)Marshal.SizeOf<NotifyData>(), Window=source.Handle, Id=1, Flags=7, Callback=0x8001, Icon=icon, Tip="Syphon", Info="", Title="" };
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
                var menu=new ContextMenu();
                foreach(var (label, action) in new[]{("Open Syphon","open"),("Pause all","pause"),("Resume all","resume"),("Exit","exit")}) {
                    var item=new MenuItem { Header=label }; item.Click+=(_,_)=>command(action); menu.Items.Add(item);
                }
                menu.Closed+=(_,_)=>PostMessage(source.Handle,0,0,0);
                menu.IsOpen=true;
            }
            handled=true;
        }
        return 0;
    }
    public void Dispose()
    {
        if(disposed)return; disposed=true;
        var data=Data(); Shell_NotifyIcon(2,ref data); IsRegistered=false;
        source.Dispose(); if(ownsIcon) DestroyIcon(icon);
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
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint LoadIcon(nint instance,nint name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window,uint message,nint wParam,nint lParam);
}
