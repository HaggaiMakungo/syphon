using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Syphon.Engine;

namespace Syphon.Desktop;

public sealed class SettingsPage : UserControl
{
    private readonly StackPanel cards = new();
    private readonly Dictionary<string, List<UIElement>> sections = new();
    private readonly List<Button> navigation = [];
    public SettingsPage(string preferencesPath, string defaultFolder, Action changed)
    {
        NameScope.SetNameScope(this, new NameScope());
        var preferences = DownloadPreferences.Load(preferencesPath); var limits = preferences.Transfer ?? new TransferOptions();
        var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new StackPanel { Margin = new Thickness(14, 22, 14, 0) }; root.Children.Add(sidebar);
        var body = new StackPanel { Margin = new Thickness(26, 22, 26, 24), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); root.Children.Add(scroll);
        var heading = new TextBlock { Name = "SettingsHeading", Text = "Downloads", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) }; body.Children.Add(heading); body.Children.Add(cards);
        var folder = new TextBox { Name = "SettingsFolder", Text = preferences.Folder ?? defaultFolder, MinWidth = 310 };
        var browse = new Button { Content = "Browse", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => { var picker = new OpenFolderDialog { Title = "Default save location" }; if (Directory.Exists(folder.Text)) picker.InitialDirectory = folder.Text; if (picker.ShowDialog(Window.GetWindow(this)) == true) folder.Text = picker.FolderName; };
        var folderGrid = new Grid(); folderGrid.ColumnDefinitions.Add(new ColumnDefinition()); folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); folderGrid.Children.Add(folder); Grid.SetColumn(browse, 1); folderGrid.Children.Add(browse);
        var single = new CheckBox { Name = "SettingsSingleConnection", Content = "Use a single connection for new downloads", IsChecked = preferences.DefaultSingleConnection };
        var floating = new CheckBox { Name = "SettingsFloating", Content = "Show the floating download window", IsChecked = preferences.ShowFloatingWindow };
        var onTop = new CheckBox { Name = "SettingsOnTop", Content = "Keep the floating window on top", IsChecked = preferences.FloatingOnTop };
        var closeToTray = new CheckBox { Name = "SettingsCloseToTray", Content = "Close to the system tray", IsChecked = preferences.CloseToTray };
        var minimizeToTray = new CheckBox { Name = "SettingsMinimizeToTray", Content = "Minimize to the system tray", IsChecked = preferences.MinimizeToTray };
        TextBox Number(string name, int value) { var input = new TextBox { Name = name, Text = value.ToString(), Width = 70 }; RegisterName(name, input); return input; }
        var perFile = Number("SettingsPerFile", limits.MaximumConnectionsPerFile);
        var perSite = Number("SettingsPerSite", limits.MaximumConnectionsPerOrigin);
        var threshold = Number("SettingsThreshold", (int)(limits.SegmentationThreshold / 1048576));
        var concurrent = Number("SettingsConcurrent", limits.MaximumConcurrentDownloads);
        var retries = Number("SettingsRetries", limits.MaximumRetries);
        var globalSpeed = Number("SettingsGlobalSpeed", (int)(limits.GlobalBytesPerSecond / 1024));
        var schedule = limits.Schedule ?? new QueueSchedule();
        var scheduled = new CheckBox { Name = "SettingsScheduleEnabled", Content = "Enable daily schedule", IsChecked = schedule.Enabled };
        var startTime = new TextBox { Name = "SettingsScheduleStart", Text = $"{schedule.StartMinute / 60:00}:{schedule.StartMinute % 60:00}", Width = 70 };
        var stopTime = new TextBox { Name = "SettingsScheduleStop", Text = $"{schedule.StopMinute / 60:00}:{schedule.StopMinute % 60:00}", Width = 70 };
        foreach (var control in new FrameworkElement[] { scheduled, startTime, stopTime }) RegisterName(control.Name, control);

        sections["Downloads"] = [Card("Default save location", folderGrid), Card("Connection preference", single, Note("Automatic mode uses up to four connections when the file supports it.")),
            Card("Connections per file", Editor("Maximum connections (1–4)", perFile), Editor("Maximum per site (1–8)", perSite), Editor("Minimum size for segmentation (1–1024 MiB)", threshold)),
            Card("Retry behaviour", Editor("Maximum retries (0–10; 0 disables)", retries), Row("Respect Retry-After", "Up to 5 minutes")),
            Card("Verification", Row("Compute SHA-256 on completion", "Always enabled"), Note("A recorded digest checks saved bytes. Authenticity requires a trusted digest to compare against."))];
        sections["Network"] = [Card("File download speed", Editor("Global limit (KB/s; 0 = Unlimited)", globalSpeed), Note("Shared by ordinary file downloads and their segments. Media tools manage their own bandwidth.")), Card("Connection limits", Row("Connections per site", limits.MaximumConnectionsPerOrigin.ToString()), Row("Connect timeout", "10 seconds"), Row("Response timeout", "20 seconds"), Row("Idle timeout", "30 seconds"), Note("These are the current engine limits."))];
        sections["Queue"] = [Card("Download queue", Editor("Simultaneous downloads (1–8)", concurrent), Note("Download now joins the active queue. Add to queue holds a task until you select Resume."), Note("Closing Syphon saves ordinary download progress. Media processing starts over when resumed.")), Card("Daily schedule", scheduled, Editor("Start at (HH:mm)", startTime), Editor("Stop at (HH:mm)", stopTime), Note("Uses computer local time; keep Syphon open and the computer awake. Starts paused/queued jobs; failed/cancelled jobs stay stopped. Overnight windows are supported."))];
        sections["Browser extension"] = [Card("Syphon this", Note("Enable video detection and automatic capture in the Chrome or Edge extension."), Note("Playing videos are detected automatically. Selecting a quality opens download options."), Note("Reload the extension and refresh video tabs after an extension update."))];
        sections["Media"] = [Card("Media tools", ToolRow("FFmpeg", "ffmpeg"), ToolRow("FFprobe", "ffprobe"), ToolRow("YouTube extractor", "yt-dlp"), ToolRow("JavaScript runtime", "node")), Card("Supported media", Note("Direct video and audio, unprotected on-demand HLS/DASH, YouTube video links, and MP3 conversion."), Note("Live recording and protected playback downloads are unavailable."))];
        sections["Appearance"] = [Card("System tray", closeToTray, minimizeToTray, Note("Keep downloads running when the manager is hidden. Use Exit in the tray menu to quit.")),Card("Theme", Row("Colour palette", "Dark purple")), Card("Floating download window", floating, onTop, Note("Closing the floating window leaves downloads running in the manager."))];
        sections["About"] = [Card("Syphon", Note("A desktop download manager with resumable file transfers, browser integration and media downloads."), Note("UI based on your supplied Syphon mockups."))];
        var result = new TextBlock { Name = "SettingsResult", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 12), Foreground = Brush("Sub") };
        var save = new Button { Name = "SaveSettings", Content = "Save changes", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left };
        save.Click += (_, _) => {
            try {
                if (string.IsNullOrWhiteSpace(folder.Text) || !Path.IsPathFullyQualified(folder.Text.Trim())) throw new ArgumentException("Choose a full folder path.");
                var destination = Path.GetFullPath(folder.Text.Trim()); if (File.Exists(destination)) throw new ArgumentException("Choose a folder, rather than a file.");
                int Read(TextBox input) => int.TryParse(input.Text, out var n) ? n : throw new ArgumentException("Enter whole numbers for download limits.");
                var size = Read(threshold); if (size is < 1 or > 1024) throw new ArgumentException("Segmentation threshold must be 1–1024 MiB.");
                int Minute(string value) => TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time)
                    ? time.Hour * 60 + time.Minute : throw new ArgumentException("Enter schedule times as HH:mm.");
                var transfer = limits with { MaximumConnectionsPerFile = Read(perFile), MaximumConnectionsPerOrigin = Read(perSite),
                    MaximumConcurrentDownloads = Read(concurrent), MaximumRetries = Read(retries), SegmentationThreshold = size * 1048576L,
                    GlobalBytesPerSecond = checked(Read(globalSpeed) * 1024L),
                    Schedule = new QueueSchedule(scheduled.IsChecked == true, Minute(startTime.Text), Minute(stopTime.Text)) };
                transfer.Validate();
                (DownloadPreferences.Load(preferencesPath) with { CloseToTray = closeToTray.IsChecked == true, MinimizeToTray = minimizeToTray.IsChecked == true, Transfer = transfer, Folder = destination, DefaultSingleConnection = single.IsChecked == true, ShowFloatingWindow = floating.IsChecked == true, FloatingOnTop = onTop.IsChecked == true }).Save(preferencesPath);
                changed(); result.Text = "Settings saved. Connection changes apply after running transfers finish.";
            } catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException) { result.Text = ex.Message; }
        };
        body.Children.Add(result); body.Children.Add(save);
        foreach (var title in sections.Keys) {
            var button = new Button { Content = title, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 5) };
            button.Click += (_, _) => ShowSection(title); sidebar.Children.Add(button); navigation.Add(button);
        }
        foreach (var item in new FrameworkElement[] { folder, single, floating, onTop, closeToTray, minimizeToTray, result, save, heading }) RegisterName(item.Name, item);
        Content = root; ShowSection("Downloads");
        void ShowSection(string title) { cards.Children.Clear(); foreach (var card in sections[title]) cards.Children.Add(card); heading.Text = title; foreach (var button in navigation) button.Background = (string)button.Content == title ? Brush("Inset") : Brushes.Transparent; }
    }
    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Brush("Sub"), Margin = new Thickness(0, 5, 0, 5), MaxWidth = 610 };
    private static Grid Card(string title, params UIElement[] children) {
        var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = title, Foreground = Brush("Sub"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) }); foreach (var child in children) panel.Children.Add(child);
        var card = MaterialPanel.Raised(panel, new Thickness(22)); card.Margin = new Thickness(0, 0, 0, 20); card.MinWidth = 540; return card;
    }
    private static Grid Row(string title, string value) { var row = new Grid { Margin = new Thickness(0, 12, 0, 12) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.Children.Add(new TextBlock { Text = title }); var text = new TextBlock { Text = value, Foreground = Brush("Bright"), Margin = new Thickness(24, 0, 0, 0) }; Grid.SetColumn(text, 1); row.Children.Add(text); return row; }
    private static Grid Editor(string title, FrameworkElement input) {
        var row = new Grid { Margin = new Thickness(0, 10, 0, 10) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }); Grid.SetColumn(input, 1); row.Children.Add(input); return row;
    }
    private static Grid ToolRow(string title, string tool) { try { var path = MediaTools.FindTool(tool); var row = Row(title, "Installed"); row.ToolTip = path; return row; } catch (IOException) { return Row(title, "Needs setup"); } }
}
