using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace Syphon.Desktop;

internal static class CustomWindowFrame
{
    private sealed class FrameState { public HwndHost? Content; public bool Pending, BorderPending; public int? BorderResult; }
    private static readonly ConditionalWeakTable<Window, FrameState> rounded = new();
    public static void Install(Window window, bool resizable)
    {
        window.Icon = (ImageSource)Application.Current.FindResource("SyphonWindowIcon");
        // Keep an opaque native HWND so the corrected direct WebView renderer is retained.
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
        window.UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(window, new WindowChrome {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(resizable ? 6 : 1),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(CornerFor(window)),
            UseAeroCaptionButtons = false
        });
        if (!rounded.TryGetValue(window, out _)) {
            rounded.Add(window, new FrameState());
            window.SourceInitialized += (_, _) => {
                HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook((IntPtr handle,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>{
                    if(message is 0x47 or 0x7d or 0x2e0)QueueContentClip(window);
                    if(message is 0x86 or 0x7d or 0x31a or 0x31e or 0x320)QueueBorderPolicy(window);
                    return IntPtr.Zero;
                });
                UpdateCorners(window);
            };
            window.SizeChanged += (_, _) => UpdateCorners(window);
            window.StateChanged += (_, _) => UpdateCorners(window);
            window.LocationChanged += (_, _) => QueueContentClip(window);
            window.Closed += (_, _) => { if(rounded.TryGetValue(window,out var state))state.Content=null; };
        }
        UpdateCorners(window);
    }
    internal static int? BorderPolicyResult(Window window)=>rounded.TryGetValue(window,out var state)?state.BorderResult:null;
    private static void ApplyBorderPolicy(Window window){
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==IntPtr.Zero||!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))return;
        var color=unchecked((int)0xfffffffe);
        var result=DwmSetWindowAttribute(handle,34,ref color,sizeof(int));
        if(rounded.TryGetValue(window,out var state))state.BorderResult=result;
    }
    private static void QueueBorderPolicy(Window window){
        if(!rounded.TryGetValue(window,out var state)||state.BorderPending||window.Dispatcher.HasShutdownStarted)return;
        state.BorderPending=true;window.Dispatcher.BeginInvoke(new Action(()=>{state.BorderPending=false;ApplyBorderPolicy(window);}),DispatcherPriority.ContextIdle);
    }

    private static void UpdateCorners(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        // Native clipping keeps the WebView HWND interactive; never use a layered window.
        var preference = window.WindowState == WindowState.Maximized ? 1 : 2;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) {
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
            // DWM offers only its small system radius. Match the larger UI outline
            // below, while retaining the opaque, interactive WebView HWND.
            ApplyBorderPolicy(window);
        }
        if (window.WindowState != WindowState.Normal) { SetWindowRgn(handle, IntPtr.Zero, true); QueueContentClip(window); return; }
        if (!GetWindowRect(handle, out var bounds)) return;
        var corner=CornerFor(window);
        var scale = window is MainWindow ? LocalHtmlSurface.MainInterfaceScale : LocalHtmlSurface.InterfaceScale;
        var radius = (int)Math.Round(corner * scale * VisualTreeHelper.GetDpi(window).DpiScaleX);
        var region = CreateRoundRectRgn(0, 0, bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1, radius * 2, radius * 2);
        ApplyRegion(handle,region);
        QueueContentClip(window);
    }
    private static double CornerFor(Window window)=>window is MainWindow?32:window is FloatingDownloadWindow?28:window is TrayPopupWindow?24:26;

    // WebView2 renders into its own child HWND; rounding only the parent does not
    // constrain that surface. Keep both shapes aligned after WPF lays out the host.
    internal static void RegisterContent(Window window,HwndHost content)
    {
        if(!rounded.TryGetValue(window,out var state))return;
        state.Content=content;
        content.SizeChanged+=(_,_)=>QueueContentClip(window);
        content.Loaded+=(_,_)=>QueueContentClip(window);
        content.LayoutUpdated+=(_,_)=>QueueContentClip(window);
        content.MessageHook+=(IntPtr handle,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>{
            if(message==0x47)QueueContentClip(window);
            return IntPtr.Zero;
        };
        QueueBorderPolicy(window);
        QueueContentClip(window);
    }
    private static void QueueContentClip(Window window)
    {
        if(!rounded.TryGetValue(window,out var state)||state.Content is null||state.Pending||window.Dispatcher.HasShutdownStarted)return;
        state.Pending=true;
        window.Dispatcher.BeginInvoke(new Action(()=>{state.Pending=false;if(state.Content is not null){ApplyOuterClip(window);ClipContent(window,state.Content);}}),DispatcherPriority.ContextIdle);
    }
    private static void ApplyRegion(IntPtr handle,IntPtr region){
        if(region==IntPtr.Zero)return;var existing=CreateRectRgn(0,0,0,0);
        try{if(GetWindowRgn(handle,existing)!=0&&EqualRgn(existing,region)){DeleteObject(region);return;}if(SetWindowRgn(handle,region,true)==0)DeleteObject(region);}
        finally{DeleteObject(existing);}
    }
    private static void ApplyOuterClip(Window window){
        var handle=new WindowInteropHelper(window).Handle;if(handle==IntPtr.Zero)return;
        if(window.WindowState!=WindowState.Normal){var existing=CreateRectRgn(0,0,0,0);try{if(GetWindowRgn(handle,existing)!=0)SetWindowRgn(handle,IntPtr.Zero,true);}finally{DeleteObject(existing);}return;}
        if(!GetWindowRect(handle,out var bounds))return;
        var corner=CornerFor(window);
        var scale=window is MainWindow?LocalHtmlSurface.MainInterfaceScale:LocalHtmlSurface.InterfaceScale;
        var radius=(int)Math.Round(corner*scale*VisualTreeHelper.GetDpi(window).DpiScaleX);
        ApplyRegion(handle,CreateRoundRectRgn(0,0,bounds.Right-bounds.Left+1,bounds.Bottom-bounds.Top+1,radius*2,radius*2));
    }
    private static void ClipContent(Window window,HwndHost content)
    {
        var handle=content.Handle;var parent=new WindowInteropHelper(window).Handle;
        if(handle==IntPtr.Zero||parent==IntPtr.Zero)return;
        var existing=CreateRectRgn(0,0,0,0);
        try {
            var kind=GetWindowRgn(handle,existing);
            if(window.WindowState!=WindowState.Normal){if(GetWindowRect(handle,out var full))ApplyRegion(handle,CreateRectRgn(0,0,full.Right-full.Left+1,full.Bottom-full.Top+1));return;}
            if(!GetWindowRect(parent,out var outer)||!GetWindowRect(handle,out var inner))return;
            var corner=CornerFor(window);
            var scale=window is MainWindow?LocalHtmlSurface.MainInterfaceScale:LocalHtmlSurface.InterfaceScale;
            var radius=(int)Math.Round(corner*scale*VisualTreeHelper.GetDpi(window).DpiScaleX);
            var clip=CreateRoundRectRgn(outer.Left-inner.Left,outer.Top-inner.Top,outer.Right-inner.Left+1,outer.Bottom-inner.Top+1,radius*2,radius*2);
            if(clip==IntPtr.Zero)return;
            if(kind!=0&&EqualRgn(existing,clip)){DeleteObject(clip);return;}
            if(SetWindowRgn(handle,clip,true)==0)DeleteObject(clip);
        } finally {if(existing!=IntPtr.Zero)DeleteObject(existing);}
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window,IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool EqualRgn(IntPtr left,IntPtr right);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr value);

    public static bool Handle(Window window, string? action)
    {
        switch (action) {
            case "window-minimize": SystemCommands.MinimizeWindow(window); return true;
            case "window-maximize":
                if (window.ResizeMode != ResizeMode.CanResize) return true;
                if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window);
                else SystemCommands.MaximizeWindow(window);
                return true;
            case "window-close": window.Close(); return true;
            default: return false;
        }
    }

    public static void RestoreSystemFrame(Window window)
    {
        WindowChrome.SetWindowChrome(window, null);
        window.WindowStyle = WindowStyle.SingleBorderWindow;
    }

    public static UIElement WrapNativeDialog(Window window, UIElement content)
    {
        Install(window, resizable: false);
        var root = new DockPanel { Margin = new Thickness(1) };
        var header = new DockPanel { Height = 49, Background = (Brush)Application.Current.FindResource("Surface"), LastChildFill = true };
        var close = new Button { Content = "×", ToolTip = "Close", Width = 42, Height = 42, Padding = new Thickness(0), Margin = new Thickness(5), FontSize = 20 };
        System.Windows.Automation.AutomationProperties.SetName(close, "Close window");
        close.Click += (_, _) => window.Close();
        var group = new Border { Background = (Brush)Application.Current.FindResource("Inset"), CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 4, 8, 4), Child = close };
        DockPanel.SetDock(group, Dock.Right); header.Children.Add(group);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        brand.Children.Add(new Image { Source = (ImageSource)Application.Current.FindResource("SyphonMark"), Width = 24, Height = 28, Margin = new Thickness(0, 0, 12, 0) });
        brand.Children.Add(new TextBlock { Text = window.Title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }); header.Children.Add(brand);
        header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) window.DragMove(); };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header); root.Children.Add(new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(200,SystemParameters.WorkArea.Height-130) }); return root;
    }
}
