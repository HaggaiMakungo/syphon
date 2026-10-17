using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Syphon.Desktop;

internal static class CustomWindowFrame
{
    private static readonly ConditionalWeakTable<Window, object> rounded = new();
    public static void Install(Window window, bool resizable)
    {
        window.Icon = (ImageSource)Application.Current.FindResource("SyphonWindowIcon");
        // Keep an opaque native HWND so the corrected direct WebView renderer is retained.
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
        window.UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(window, new WindowChrome {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(resizable ? 6 : 1),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(16),
            UseAeroCaptionButtons = false
        });
        if (!rounded.TryGetValue(window, out _)) {
            rounded.Add(window, new object());
            window.SourceInitialized += (_, _) => UpdateCorners(window);
            window.SizeChanged += (_, _) => UpdateCorners(window);
            window.StateChanged += (_, _) => UpdateCorners(window);
        }
        UpdateCorners(window);
    }

    private static void UpdateCorners(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        // Native clipping keeps the WebView HWND interactive; never use a layered window.
        var preference = window.WindowState == WindowState.Maximized ? 1 : 2;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int)) == 0) return;
        if (window.WindowState != WindowState.Normal) { SetWindowRgn(handle, IntPtr.Zero, true); return; }
        if (!GetWindowRect(handle, out var bounds)) return;
        var radius = (int)Math.Round(16 * VisualTreeHelper.GetDpi(window).DpiScaleX);
        var region = CreateRoundRectRgn(0, 0, bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1, radius * 2, radius * 2);
        if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0) DeleteObject(region);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
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
        var header = new DockPanel { Height = 49 * LocalHtmlSurface.InterfaceScale, Background = (Brush)Application.Current.FindResource("Surface"), LastChildFill = true };
        var close = new Button { Content = "×", ToolTip = "Close", Width = 42, Height = 42, Padding = new Thickness(0), Margin = new Thickness(5), FontSize = 20 };
        System.Windows.Automation.AutomationProperties.SetName(close, "Close window");
        close.Click += (_, _) => window.Close();
        var group = new Border { Background = (Brush)Application.Current.FindResource("Inset"), CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 4, 8, 4), Child = close };
        DockPanel.SetDock(group, Dock.Right); header.Children.Add(group);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        brand.Children.Add(new Image { Source = (ImageSource)Application.Current.FindResource("SyphonMark"), Width = 24, Height = 28, Margin = new Thickness(0, 0, 12, 0) });
        brand.Children.Add(new TextBlock { Text = window.Title, FontSize = 16, VerticalAlignment = VerticalAlignment.Center }); header.Children.Add(brand);
        header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) window.DragMove(); };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header); root.Children.Add(content); return root;
    }
}
