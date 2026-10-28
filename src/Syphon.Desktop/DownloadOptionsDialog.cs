using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Syphon.Engine;

namespace Syphon.Desktop;

public sealed partial class DownloadOptionsDialog : Window
{
    private readonly TextBox filename, folder, source;
    private readonly CheckBox remember, single;
    private readonly TextBlock error;
    public string? MediaFormat {get;private set;}
    public bool FilenameEdited {get;private set;}
    public string Filename => filename.Text.Trim();
    public string Folder => Path.GetFullPath(folder.Text.Trim());
    public bool RememberFolder => remember.IsChecked == true;
    public bool StartImmediately { get; private set; }
    public int Priority { get; private set; }
    public string SourceUrl => source.Text.Trim();
    public bool SingleConnection => single.IsChecked == true;
    public event Action? Confirmed;

    public DownloadOptionsDialog(string title, string suggestedFilename, string initialFolder, string? quality, long? estimatedBytes, bool rememberFolder,
        string? sourceUrl = null, bool allowUrlEditing = false, bool singleConnection = false, bool htmlUi = true)
    {
        Title = "Download options"; Width = 660; SizeToContent = SizeToContent.Height; Topmost = true;
        Icon = (ImageSource)FindResource("SyphonWindowIcon");
        ResizeMode = ResizeMode.CanResize; MinWidth = 400; MinHeight = 360; MaxHeight = SystemParameters.WorkArea.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Surface"); Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        NameScope.SetNameScope(this, new NameScope());
        var panel = new StackPanel { Margin = new Thickness(32) };
        panel.Children.Add(new Image { Source = (ImageSource)FindResource("SyphonMark"), Width = 40, Height = 34, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new TextBlock { Text = "New download", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 14) });
        source = new TextBox { Name = "DownloadSourceUrl", Text = sourceUrl ?? "", IsReadOnly = !allowUrlEditing, MaxLength = 8192, ToolTip = "HTTP / HTTPS link", Visibility = sourceUrl is null && !allowUrlEditing ? Visibility.Collapsed : Visibility.Visible };
        if (source.Visibility == Visibility.Visible) { AddLabel("_URL · HTTP / HTTPS", source); panel.Children.Add(source); }
        var videoTitle = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 64, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = title };
        var fileInfo = new StackPanel { Margin = new Thickness(0, 16, 0, 4) }; fileInfo.Children.Add(videoTitle);
        var detail = new TextBlock { Name = "DownloadDetails", Text = (quality ?? "Original file") + "  ·  " + FormatSize(estimatedBytes), Foreground = (Brush)FindResource("Sub"), Margin = new Thickness(0, 6, 0, 18) };
        fileInfo.Children.Add(detail); panel.Children.Add(MaterialPanel.Inset(fileInfo, new Thickness(18, 0, 18, 0))); RegisterName(detail.Name, detail);
        filename = new TextBox { Name = "DownloadFilename", Text = suggestedFilename, MaxLength = 120 };
        folder = new TextBox { Name = "DownloadFolder", Text = initialFolder };
        AddLabel("File _name", filename); panel.Children.Add(filename);
        AddLabel("Save _to", folder);
        var folderRow = new Grid(); folderRow.ColumnDefinitions.Add(new ColumnDefinition()); folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.Children.Add(folder);
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => {
            var picker = new OpenFolderDialog { Title = "Choose a download folder", Multiselect = false };
            try { if (Directory.Exists(Folder)) picker.InitialDirectory = Folder; } catch (Exception) { }
            if (picker.ShowDialog(this) == true) folder.Text = picker.FolderName;
        };
        Grid.SetColumn(browse, 1); folderRow.Children.Add(browse); panel.Children.Add(folderRow);
        remember = new CheckBox { Name = "RememberDownloadFolder", Content = "Remember this folder", IsChecked = rememberFolder, Margin = new Thickness(0, 14, 0, 12), Foreground = Foreground };
        var rememberCard = MaterialPanel.Raised(remember, new Thickness(16, 0, 16, 0)); rememberCard.Margin = new Thickness(0, 18, 0, 12); panel.Children.Add(rememberCard);
        single = new CheckBox { Name = "SingleDownloadConnection", Content = "Single connection", IsChecked = singleConnection, Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Use one connection for ordinary file downloads. Otherwise eligible files use up to four. Media tools manage their own connections." };
        var singleCard = MaterialPanel.Raised(single, new Thickness(16, 8, 16, 0)); singleCard.Margin = new Thickness(0, 0, 0, 18); panel.Children.Add(singleCard);
        panel.Children.Add(new TextBlock { Text = "Add to queue saves it for later. Select Resume in Syphon to start it.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = detail.Foreground });
        error = new TextBlock { Name = "DownloadOptionsError", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(232, 130, 130)), Margin = new Thickness(0, 10, 0, 8) };
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite); panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Name = "CancelDownloadOptions", Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();
        var queue = new Button { Name = "QueueDownload", Content = "Add to queue", Margin = new Thickness(8, 0, 0, 0) };
        var download = new Button { Name = "ConfirmDownload", Content = "Download now", IsDefault = true, Margin = new Thickness(8, 0, 0, 0), Background = (Brush)FindResource("Accent"), Foreground = Brushes.White, Style = (Style)FindResource("PrimaryButton") };
        queue.Click += (_, _) => Submit(false); download.Click += (_, _) => Submit(true);
        buttons.Children.Add(cancel); buttons.Children.Add(queue); buttons.Children.Add(download); panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        foreach (var control in new FrameworkElement[] { filename, folder, source, single, remember, error, cancel, queue, download }) RegisterName(control.Name, control);
        AutomationProperties.SetName(filename, "Filename"); AutomationProperties.SetName(folder, "Save to folder");
        Loaded += (_, _) => { if (allowUrlEditing) source.Focus(); else { filename.Focus(); filename.Select(0, Path.GetFileNameWithoutExtension(filename.Text).Length); } };
        source.LostFocus += (_, _) => {
            if (allowUrlEditing && filename.Text == "download.bin" && Uri.TryCreate(SourceUrl, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
                filename.Text = DownloadCoordinator.SuggestedBrowserFilename(url);
        };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };

        if (htmlUi) InstallHtml(title, quality, estimatedBytes, allowUrlEditing);

        void AddLabel(string text, TextBox target) => panel.Children.Add(new Label { Content = text, Target = target, Padding = new Thickness(0, 10, 0, 5) });
    }

    private void Submit(bool immediately)
    {
        try {
            DownloadCoordinator.ValidateBrowserFilename(Filename);
            if (string.IsNullOrWhiteSpace(folder.Text) || !Path.IsPathFullyQualified(folder.Text.Trim())) throw new ArgumentException("Choose a full download folder path.");
            _ = Folder;
            if (File.Exists(Folder)) throw new ArgumentException("Choose a folder, rather than an existing file.");
            StartImmediately = immediately; Confirmed?.Invoke();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { ShowError(ex.Message); }
    }
    public void ShowError(string message) { error.Text = message; HtmlSurface?.Publish(); }
    private static string FormatSize(long? bytes) {
        if (bytes is not > 0) return "Size unavailable";
        var units = new[] { "B", "KB", "MB", "GB", "TB" }; var index = Math.Min(4, (int)(Math.Log(bytes.Value) / Math.Log(1000)));
        return $"About {bytes.Value / Math.Pow(1000, index):0.#} {units[index]}";
    }
}
