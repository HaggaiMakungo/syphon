using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Syphon.Core;
using Syphon.Engine;

namespace Syphon.Desktop;

public partial class MainWindow
{
    private string currentPage = "Downloads";
    private DownloadOptionsDialog? manualDialog;
    private FloatingDownloadWindow? floatingWindow;
    private Guid? requestedFloatingTask;
    private readonly HashSet<Guid> dismissedFloating = [];
    private readonly Queue<double> managerSamples = new();
    private readonly Queue<DateTimeOffset> managerSampleTimes = new();
    private DateTimeOffset lastManagerSample;
    private StoredDownload[] displayTasks = [];
    private void UpdateManagerChart(StoredDownload[] tasks)
    {
        if (DateTimeOffset.UtcNow - lastManagerSample < TimeSpan.FromMilliseconds(500)) return;
        var active = tasks.Any(t => t.State is DownloadState.Downloading or DownloadState.Starting);
        if (!active && managerSamples.Count == 0) return;
        managerSamples.Enqueue(tasks.Where(t => t.State == DownloadState.Downloading).Sum(t => Math.Max(0, t.BytesPerSecond)));
        lastManagerSample = DateTimeOffset.UtcNow;
        managerSampleTimes.Enqueue(lastManagerSample);
        if (managerSamples.Count > 60) { managerSamples.Dequeue(); managerSampleTimes.Dequeue(); }
        ManagerSpeedChart.Update(managerSamples);
    }

    private void CardAction_CanExecute(object sender, System.Windows.Input.CanExecuteRoutedEventArgs e)
    {
        if (e.Parameter is DownloadRow row) e.CanExecute = e.Command == System.Windows.Input.ApplicationCommands.Stop ? row.CanPause : row.CanAct;
        e.Handled = true;
    }
    private void CardAction_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not DownloadRow row) return;
        TaskList.SelectedItem = row; selectedId = row.Snapshot.Id;
        if (e.Command == System.Windows.Input.ApplicationCommands.Stop) Cancel_Click(sender, e);
        else if (row.CanResume) Resume_Click(sender, e); else Pause_Click(sender, e);
        e.Handled = true;
    }
    private void DownloadsNav_Click(object sender, RoutedEventArgs e) => Navigate("Downloads");
    private void HistoryNav_Click(object sender, RoutedEventArgs e) => Navigate("History");
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => Navigate("Settings");
    private void Navigate(string page)
    {
        currentPage = page; PageTitle.Text = page;
        DownloadsView.Visibility = page == "Settings" ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Visibility = FileTypeFilter.Visibility = page == "Settings" ? Visibility.Collapsed : Visibility.Visible;
        if (page == "Settings") SettingsView.Content = new SettingsPage(preferencesPath, browserDownloads, ApplyPreferences);
        var history = page == "History";
        TransferStats.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        FloatingWindowButton.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        ManagerChartPanel.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        DownloadsView.RowDefinitions[0].Height = new GridLength(history ? 58 : 98);
        TaskList.Tag = page; TaskList.HeadersVisibility = history ? DataGridHeadersVisibility.Column : DataGridHeadersVisibility.None;
        QuickAddExpander.Visibility = page == "Downloads" ? Visibility.Visible : Visibility.Collapsed;
        ProgressColumn.Visibility = TimeColumn.Visibility = ConnectionsColumn.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        HistorySizeColumn.Visibility = HistoryHashColumn.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        DownloadsNav.Background = page == "Downloads" ? (Brush)FindResource("Inset") : (Brush)FindResource("Card");
        HistoryNav.Background = history ? (Brush)FindResource("Inset") : (Brush)FindResource("Card");
        SettingsNav.Background = page == "Settings" ? (Brush)FindResource("Inset") : (Brush)FindResource("Card");
        ApplyViewFilter();
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (TaskList is not null) ApplyViewFilter(); }
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) { if (TaskList is not null) ApplyViewFilter(); }
    private void ApplyViewFilter()
    {
        var view = CollectionViewSource.GetDefaultView(rows);
        var text = SearchBox.Text.Trim(); var type = (FileTypeFilter.SelectedItem as ComboBoxItem)?.Content as string;
        view.Filter = item => item is DownloadRow row && (currentPage != "History" || row.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled)
            && (string.IsNullOrEmpty(text) || row.Filename.Contains(text, StringComparison.OrdinalIgnoreCase) || row.Host.Contains(text, StringComparison.OrdinalIgnoreCase))
            && (type is null or "All types" || row.FileType == type);
        UpdateSummary();
    }
    private void UpdateSummary()
    {
        TotalSpeedText.Text = DownloadDisplay.Bytes(rows.Where(r => r.State == DownloadState.Downloading).Sum(r => r.Snapshot.BytesPerSecond)) + "/s";
        QueueCountText.Text = $"{rows.Count(r => r.State is DownloadState.Downloading or DownloadState.Starting)} active · {rows.Count(r => r.State == DownloadState.Queued)} waiting";
        SavedSizeText.Text = DownloadDisplay.Bytes(rows.Where(r => r.State == DownloadState.Completed).Sum(r => (double)(r.Snapshot.Total ?? r.Snapshot.Received)));
        ConnectionCountText.Text = rows.Where(r => r.State == DownloadState.Downloading).Sum(r => r.Snapshot.Connections).ToString();
        var visible = CollectionViewSource.GetDefaultView(rows).Cast<DownloadRow>().ToArray();
        SummaryText.Text = currentPage == "History" ? $"{visible.Length} files  ·  {DownloadDisplay.Bytes(visible.Sum(r => (double)(r.Snapshot.Total ?? r.Snapshot.Received)))} saved  ·  SHA-256 recorded on completion"
            : $"{rows.Count} files  ·  {rows.Count(r => r.State is DownloadState.Downloading or DownloadState.Starting)} active  ·  {DownloadDisplay.Bytes(rows.Sum(r => r.Snapshot.BytesPerSecond))}/s";
    }
    private void ApplyPreferences()
    {
        var preferences = DownloadPreferences.Load(preferencesPath);
        coordinator?.Configure(preferences.Transfer ?? new());
        SingleConnectionChoice.IsChecked = preferences.DefaultSingleConnection;
        if (Path.GetFileName(DestinationBox.Text) == "download.bin") DestinationBox.Text = Path.Combine(preferences.Folder ?? browserDownloads, "download.bin");
        if (!preferences.ShowFloatingWindow) floatingWindow?.Hide();
        if (floatingWindow is not null) floatingWindow.Topmost = preferences.FloatingOnTop;
    }
    private void NewDownload_Click(object sender, RoutedEventArgs e)
    {
        if (!IsReady || coordinator is null) return;
        if (pendingOptions.Count > 0) { pendingOptions.Values.First().Dialog.Activate(); return; }
        if (manualDialog?.IsVisible == true) { manualDialog.Activate(); return; }
        var preferences = DownloadPreferences.Load(preferencesPath);
        var dialog = new DownloadOptionsDialog("Add a link to start a new download", "download.bin", preferences.Folder ?? browserDownloads, "Automatic", null, preferences.Folder is not null,
            UrlBox.Text.Trim(), true, preferences.DefaultSingleConnection, useHtml) { Owner = this };
        manualDialog = dialog;
        if (!ShowInTaskbar) { dialog.ShowInTaskbar = false; dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = Left; dialog.Top = Top; }
        var requestId = Guid.NewGuid();
        dialog.Confirmed += () => {
            try {
                var accepted = CreateManualDownload(requestId, dialog.SourceUrl, dialog.Folder, dialog.Filename, dialog.StartImmediately, dialog.SingleConnection, dialog.Priority);
                try { (DownloadPreferences.Load(preferencesPath) with { Folder = dialog.RememberFolder ? dialog.Folder : null }).Save(preferencesPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                RevealDownload(accepted.TaskId); dialog.Close();
            } catch (Exception ex) { dialog.ShowError(ex.Message); }
        };
        dialog.Closed += (_, _) => manualDialog = null; dialog.Show();
    }
    private BrowserAcceptance CreateManualDownload(Guid requestId, string source, string folder, string? filename = null, bool immediately = true, bool single = false, int priority = 0)
    {
        if (!Uri.TryCreate(source.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https")) throw new ArgumentException("Enter an HTTP or HTTPS download link.");
        if (!Path.IsPathFullyQualified(folder) || File.Exists(folder)) throw new ArgumentException("Choose a full download folder path.");
        var format = YoutubeTools.IsYoutubeVideoTarget(url) ? "mp4" : null;
        filename ??= DownloadCoordinator.SuggestedBrowserFilename(url, format);
        DownloadCoordinator.ValidateBrowserFilename(filename, format);
        return coordinator!.AcceptBrowserDownload(requestId, url, folder, filename: filename, startImmediately: immediately, singleConnection: single, priority: priority, categorize: UsesCategoryFolders(folder));
    }
    private bool UsesCategoryFolders(string folder)
    {
        var root = DownloadPreferences.Load(preferencesPath).Folder ?? browserDownloads;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase);
    }
    private void Floating_Click(object sender, RoutedEventArgs e) => UpdateFloating(true);
    private void RevealDownload(Guid id)
    {
        Navigate("Downloads"); SearchBox.Text = ""; FileTypeFilter.SelectedIndex = 0;
        selectedId = id; commandError = null; RefreshTasks();
    }
    private void UpdateFloating(bool requested = false, StoredDownload[]? snapshot = null)
    {
        if (!ShowInTaskbar && !requested && floatingWindow?.IsVisible != true || closing || coordinator is null) return;
        var preferences = DownloadPreferences.Load(preferencesPath);
        var tasks = snapshot ?? coordinator.GetTasks();
        if (requested) requestedFloatingTask = selectedId;
        var task = tasks.FirstOrDefault(t => t.Id == requestedFloatingTask)
            ?? tasks.FirstOrDefault(t => t.State is DownloadState.Downloading or DownloadState.Starting or DownloadState.RetryWaiting or DownloadState.Publishing or DownloadState.Assembling)
            ?? tasks.FirstOrDefault(t => t.Id == floatingWindow?.TaskId && t.State == DownloadState.Paused && floatingWindow.IsVisible)
            ?? (requested ? tasks.FirstOrDefault(t => t.Id == selectedId) ?? tasks.LastOrDefault() : null);
        if (task is null) { floatingWindow?.Hide(); return; }
        if (!requested && ((!preferences.ShowFloatingWindow && floatingWindow?.IsVisible != true) || dismissedFloating.Contains(task.Id))) return;
        if (requested) dismissedFloating.Remove(task.Id);
        if (floatingWindow is null) {
            floatingWindow = new FloatingDownloadWindow(async id => { await coordinator.PauseAsync(id); RefreshTasks(); }, id => { coordinator.Resume(id); RefreshTasks(); }, async id => { await coordinator.PauseAsync(id, true); RefreshTasks(); },
                () => { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Show(); Activate(); }, id => { dismissedFloating.Add(id); requestedFloatingTask = null; }, useHtml);
            floatingWindow.Closed += (_, _) => { floatingWindow = null; requestedFloatingTask = null; };
            if (!ShowInTaskbar) { floatingWindow.Left = Left; floatingWindow.Top = Top; }
        }
        floatingWindow.Topmost = preferences.FloatingOnTop;
        floatingWindow.Update(task, tasks);
        if (!floatingWindow.IsVisible) floatingWindow.Show();
        if (requested && floatingWindow.WindowState == WindowState.Minimized) floatingWindow.WindowState = WindowState.Normal;
    }
}
