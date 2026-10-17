using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Syphon.Core;
using Syphon.Engine;

namespace Syphon.Desktop;

public sealed partial class FloatingDownloadWindow : Window
{
    // ── Neuro palette (WPF fallback) ──────────────────────────────────────────
    private static readonly SolidColorBrush BgSurface  = SB(0x1E, 0x1A, 0x2E);
    private static readonly SolidColorBrush BgRaised   = SB(0x23, 0x1F, 0x33);
    private static readonly SolidColorBrush BgInset    = SB(0x16, 0x12, 0x22);
    private static readonly SolidColorBrush ColAccent  = SB(0x83, 0x50, 0xC4);
    private static readonly SolidColorBrush ColAccentText = SB(0xC4, 0x9A, 0xF5);
    private static readonly SolidColorBrush ColBright  = SB(0xEC, 0xE9, 0xF2);
    private static readonly SolidColorBrush ColSub     = SB(0xB9, 0xAA, 0xCD);
    private static readonly SolidColorBrush ColText    = SB(0xEC, 0xE9, 0xF2);
    private static readonly SolidColorBrush ColMuted   = SB(0x92, 0x84, 0xAA);
    private static readonly SolidColorBrush ColDanger  = SB(0x9E, 0x4A, 0x4A);
    private static readonly SolidColorBrush WinBorder  = new(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));

    // ── Task callbacks (shared with Html partial) ────────────────────────────
    private Func<Guid, Task>? _pauseTask;
    private Action<Guid>?     _resumeTask;
    private Func<Guid, Task>? _cancelTask;

    // ── Native WPF fallback controls ─────────────────────────────────────────
    private readonly TextBlock filename, host, speed, progress, remaining, detail, queued;
    private readonly ProgressBar bar;
    private readonly SegmentStrip segments;
    private readonly RechartsSpeedChart chart;
    private readonly Button pause, cancel;
    private readonly StackPanel body;

    private readonly Queue<double> speeds = new();
    private readonly Queue<DateTimeOffset> speedTimes = new();
    private DateTimeOffset lastSample;
    private StoredDownload? current;
    public Guid? TaskId => current?.Id;

    public FloatingDownloadWindow(
        Func<Guid, Task> pauseTask,
        Action<Guid>     resumeTask,
        Func<Guid, Task> cancelTask,
        Action           openManager,
        Action<Guid>     dismissed,
        bool             htmlUi = true)
    {
        _pauseTask  = pauseTask;
        _resumeTask = resumeTask;
        _cancelTask = cancelTask;
        Closing += (_, _) => { if (IsVisible && TaskId is Guid id) dismissed(id); };

        Title = "Syphon — Active download";
        Icon = (ImageSource)FindResource("SyphonWindowIcon");
        Width = 320; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        // Layered windows can display WebView pixels while their native hit-test
        // surface remains transparent. Use the same opaque HWND as the manager
        // so real mouse input reaches the browser instead of the window beneath.
        WindowStyle = WindowStyle.None; AllowsTransparency = false;
        Background = BgSurface; ShowInTaskbar = false; ShowActivated = false;
        CustomWindowFrame.Install(this, resizable: false);
        NameScope.SetNameScope(this, new NameScope());

        // ── Root frame ───────────────────────────────────────────────────────
        var root  = new StackPanel();
        var frame = new Border
        {
            Background      = BgSurface,
            BorderBrush     = WinBorder,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(16),
            Child           = root,
        };
        Content = frame;

        // ── Title bar ────────────────────────────────────────────────────────
        var title = new DockPanel { Background = BgRaised, Height = 40 };
        title.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
                DragMove();
        };

        var close = new Button
        {
            Name = "FloatingClose", Content = "✕",
            ToolTip = "Close window — downloads continue",
            Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 8, 8, 8),
            Background = BgSurface, Foreground = ColSub, BorderThickness = new Thickness(0),
        };
        close.Click += (_, _) => { if (TaskId is Guid id) dismissed(id); Hide(); };
        DockPanel.SetDock(close, Dock.Right);
        title.Children.Add(close);

        var minimize = new Button
        {
            Content = "−",
            ToolTip = "Collapse / expand",
            Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 8, 4, 8),
            Background = BgSurface, Foreground = ColSub, BorderThickness = new Thickness(0),
        };
        DockPanel.SetDock(minimize, Dock.Right);
        title.Children.Add(minimize);

        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        brand.Children.Add(new Image { Source = (ImageSource)FindResource("SyphonMark"), Width = 26, Height = 23, Margin = new Thickness(0, 0, 7, 0) });
        brand.Children.Add(new TextBlock
        {
            Text = "Syphon", FontSize = 11, FontWeight = FontWeights.Medium,
            Foreground = ColSub, VerticalAlignment = VerticalAlignment.Center,
        });
        title.Children.Add(brand);
        root.Children.Add(title);

        // ── Body ─────────────────────────────────────────────────────────────
        body = new StackPanel { Margin = new Thickness(14, 14, 14, 12) };
        root.Children.Add(body);

        // File row
        filename = T("", 11, ColBright);
        filename.FontWeight = FontWeights.Medium;
        filename.TextTrimming = TextTrimming.CharacterEllipsis;

        host = T("", 10, ColSub);
        host.Margin = new Thickness(0, 2, 0, 0);

        var nameStack = new StackPanel();
        nameStack.Children.Add(filename);
        nameStack.Children.Add(host);

        var iconWell = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(10),
            Background = BgInset, Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "↓", FontSize = 14, Foreground = ColAccentText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            },
        };
        var fileRow = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(iconWell, Dock.Left);
        fileRow.Children.Add(iconWell);
        fileRow.Children.Add(nameStack);
        body.Children.Add(fileRow);

        // Speed chart (inset well)
        chart = new RechartsSpeedChart { Height = 80 };
        var chartWell = new Border
        {
            Background = BgInset, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 8, 8, 4), Margin = new Thickness(0, 0, 0, 11),
            Child = chart,
        };
        body.Children.Add(chartWell);
        RegisterName("FloatingSpeedChart", chart);
        Closed += (_, _) => chart.Dispose();

        // Progress bar
        bar = new ProgressBar
        {
            Height = 6, Maximum = 100, Margin = new Thickness(0, 0, 0, 9),
            Background = BgInset, Foreground = ColAccentText,
        };
        body.Children.Add(bar);

        // Segment strip
        segments = new SegmentStrip { Margin = new Thickness(0, 0, 0, 9) };
        body.Children.Add(segments);

        // Stats grid
        var stats = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        for (int i = 0; i < 3; i++) stats.ColumnDefinitions.Add(new ColumnDefinition());
        speed    = Stat("Speed",     0, stats);
        progress = Stat("Progress",  1, stats);
        remaining = Stat("Remaining", 2, stats);
        body.Children.Add(stats);

        // Detail
        detail = T("", 10, ColSub);
        detail.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(detail);

        // Action buttons
        var actions = new Grid();
        for (int i = 0; i < 3; i++) actions.ColumnDefinitions.Add(new ColumnDefinition());

        pause = Btn("FloatingPause", "Pause",   ColText,   0);
        var mgr  = Btn(null,          "Manager", ColText,   1);
        cancel   = Btn("FloatingCancel", "Cancel", ColDanger, 2);

        mgr.Click += (_, _) => openManager();
        pause.Click += async (_, _) =>
        {
            if (current is null) return;
            try
            {
                if (current.State is DownloadState.Paused or DownloadState.Cancelled or DownloadState.Failed)
                    _resumeTask!(current.Id);
                else
                    await _pauseTask!(current.Id);
            }
            catch (Exception ex) { detail.Text = ex.Message; }
        };
        cancel.Click += async (_, _) =>
        {
            if (current is null) return;
            try { await _cancelTask!(current.Id); }
            catch (Exception ex) { detail.Text = ex.Message; }
        };

        body.Children.Add(actions);

        // Queue footer
        queued = T("", 10, ColMuted);
        queued.TextTrimming = TextTrimming.CharacterEllipsis;
        root.Children.Add(new Border
        {
            Background      = BgInset,
            BorderBrush     = new SolidColorBrush(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius    = new CornerRadius(0, 0, 15, 15),
            Child           = new Border { Padding = new Thickness(14, 9, 14, 9), Child = queued },
        });

        minimize.Click += (_, _) =>
        {
            body.Visibility = body.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            queued.Text = body.Visibility == Visibility.Collapsed
                ? $"{filename.Text}  ·  {speed.Text}"
                : "";
        };

        RegisterName(pause.Name,  pause);
        RegisterName(cancel.Name, cancel);
        RegisterName(close.Name,  close);

        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top  = SystemParameters.WorkArea.Bottom - 560;
        SizeChanged += (_, _) =>
        {
            if (Top >= 0 && Top + ActualHeight > SystemParameters.WorkArea.Bottom - 12)
                Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - ActualHeight - 24);
        };

        if (htmlUi) InstallHtml(openManager, dismissed);

        // ── Local helpers ────────────────────────────────────────────────────
        TextBlock Stat(string label, int col, Grid grid)
        {
            var lbl = T(label, 9, ColMuted);
            var val = T("—", 14, col == 1 ? ColAccent : ColBright);
            val.FontWeight = FontWeights.Medium;
            val.Margin = new Thickness(0, 3, 0, 0);
            if (col == 2) { lbl.TextAlignment = TextAlignment.Right; val.TextAlignment = TextAlignment.Right; }
            if (col == 1) { lbl.TextAlignment = TextAlignment.Center; val.TextAlignment = TextAlignment.Center; }
            var panel = new StackPanel();
            panel.Children.Add(lbl); panel.Children.Add(val);
            var card = new Border
            {
                Background   = col == 1 ? BgInset : BgRaised,
                CornerRadius = new CornerRadius(8),
                Padding      = new Thickness(10, 8, 10, 8),
                Child        = panel,
                Margin       = new Thickness(col == 0 ? 0 : 3, 0, col == 2 ? 0 : 3, 0),
            };
            Grid.SetColumn(card, col);
            grid.Children.Add(card);
            return val;
        }

        Button Btn(string? name, string text, SolidColorBrush fg, int col)
        {
            var b = new Button
            {
                Content = text, FontSize = 11,
                Padding = new Thickness(5, 9, 5, 9),
                Margin  = new Thickness(col == 0 ? 0 : 3, 0, col == 2 ? 0 : 3, 0),
                Background = BgRaised, Foreground = fg, BorderThickness = new Thickness(0),
            };
            if (name is not null) { b.Name = name; }
            Grid.SetColumn(b, col);
            actions.Children.Add(b);
            return b;
        }
    }

    // ── Update (called by coordinator) ────────────────────────────────────────
    public void Update(StoredDownload task, StoredDownload[] tasks)
    {
        if (current?.Id != task.Id) { speeds.Clear(); speedTimes.Clear(); lastSample = default; }
        current = task;
        filename.Text    = task.Filename; filename.ToolTip = task.Filename;
        host.Text        = new Uri(task.Url).Host;
        segments.Snapshot = task;

        speed.Text     = DownloadDisplay.Speed(task);
        progress.Text  = task.Total is > 0 ? $"{Math.Clamp(task.Received * 100.0 / task.Total.Value, 0, 100):0.#}%" : "—";
        remaining.Text = DownloadDisplay.Remaining(task);

        bar.IsIndeterminate = task.Total is null && task.State is DownloadState.Downloading or DownloadState.Starting;
        bar.Value = task.Total is > 0 ? Math.Clamp(task.Received * 100.0 / task.Total.Value, 0, 100) : 0;

        detail.Text = DownloadDisplay.Progress(task) + "  ·  " +
                      (task.Connections > 1 ? $"{task.Connections} connections" : DownloadDisplay.Status(task));

        var resume = task.State is DownloadState.Paused or DownloadState.Cancelled or DownloadState.Failed;
        pause.Content  = resume ? "Resume" : "Pause";
        pause.IsEnabled = task.State != DownloadState.Completed &&
                          !task.RestartRequired &&
                          task.State != DownloadState.Pausing;
        cancel.IsEnabled = task.State is DownloadState.Downloading or DownloadState.Starting or
                           DownloadState.Queued or DownloadState.RetryWaiting or
                           DownloadState.Assembling or DownloadState.Publishing;

        var next = tasks.FirstOrDefault(t =>
            t.Id != task.Id && t.State is DownloadState.Queued or DownloadState.Paused);
        queued.Text = body.Visibility == Visibility.Collapsed
            ? $"{task.Filename}  ·  {speed.Text}"
            : next is null ? "" : $"Next: {next.Filename}";

        _htmlTasks = tasks;

        if (DateTimeOffset.UtcNow - lastSample >= TimeSpan.FromMilliseconds(500))
        {
            speeds.Enqueue(Math.Max(0, task.BytesPerSecond));
            speedTimes.Enqueue(DateTimeOffset.UtcNow);
            while (speeds.Count > 120) { speeds.Dequeue(); speedTimes.Dequeue(); }
            lastSample = DateTimeOffset.UtcNow;
            chart.Update(speeds);
        }

        PublishHtml();
    }

    // ── Statics ───────────────────────────────────────────────────────────────
    private static SolidColorBrush SB(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
    private static TextBlock T(string text, double size, Brush fg) =>
        new() { Text = text, FontSize = size, Foreground = fg };
}
