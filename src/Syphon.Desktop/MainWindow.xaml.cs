using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Syphon.Core;
using Syphon.Engine;
using Syphon.BrowserBridge;

namespace Syphon.Desktop;

public partial class MainWindow : Window
{
    private DownloadCoordinator? coordinator;
    private BrowserPipeServer? browserServer;
    private readonly string? browserPipeName;
    private readonly string browserDownloads;
    private readonly Task<DownloadCoordinator> initialization;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private bool closing;
    private bool allowClose;
    private bool refreshing;
    private Guid? selectedId;
    private string? commandError;
    internal Action<ProcessStartInfo> FolderProcessStarter { get; set; } = start => Process.Start(start);
    private string? initializationError;
    private readonly ObservableCollection<DownloadRow> rows = [];
    public bool IsReady => coordinator is not null && !closing;

    public MainWindow() : this(null) { }
    public MainWindow(string? dataDirectory, string? bridgePipeName = null, bool htmlUi = true)
    {
        InitializeComponent();
        useHtml = htmlUi;
        platformEnabled = dataDirectory is null;
        browserPipeName = bridgePipeName ?? (dataDirectory is null ? BrowserProtocol.PipeName : null);
        browserDownloads = dataDirectory is null ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") : Path.Combine(dataDirectory, "BrowserDownloads");
        TaskList.ItemsSource = rows;
        PreviewKeyDown += (_, e) =>
        {
            if (useHtml && e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { if (HtmlSurface?.IsReady == true) _ = HtmlSurface.EvaluateAsync("document.querySelector('[aria-label=\"New download\"]')?.click()"); e.Handled = true; }
            else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { UrlBox.Focus(); UrlBox.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.None) { TaskList.Focus(); e.Handled = true; }
        };
        DestinationBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "download.bin");
        var directory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Syphon");
        preferencesPath = Path.Combine(directory, "download-preferences.json");
        TypographyComparison.Apply(DownloadPreferences.Load(preferencesPath).TypographyPreset);
        ApplyPreferences();
        initialization = Task.Run(() => DownloadCoordinator.OpenAsync(directory, DownloadPreferences.Load(preferencesPath).Transfer));
        Loaded += async (_, _) =>
        {
            try
            {
                coordinator = await initialization;
                if (closing) return;
                DownloadButton.IsEnabled = true;
                refresh.Tick += (_, _) => RefreshTasks();
                refresh.Start();
                RefreshTasks();
                InitializePlatform();
                if (browserPipeName is not null) browserServer = new BrowserPipeServer(browserPipeName, HandleBrowserRequestAsync);
            }
            catch (Exception ex)
            {
                StatusText.Text = ex is InvalidDataException ? ex.Message : "Saved downloads could not be opened. Close any other Syphon window and try again.";
                DetailText.Text = ex.Message;
                initializationError = ex is InvalidDataException ? StatusText.Text : StatusText.Text + " If the problem persists, check access to Syphon’s saved download data and restart Syphon.";
                HtmlSurface?.Publish();
            }
        };
        if (useHtml) InstallHtml();
    }

    private async Task<BrowserReply> HandleBrowserRequestAsync(BrowserRequest request, CancellationToken token)
    {
        if (request.Action == "download" && request.ShowOptions) return await ShowDownloadOptionsAsync(request, token);
        if (request.Action == "media-inspect")
        {
            try
            {
                var info = await MediaTools.InspectAsync(new Uri(request.Url!), token);
                return new BrowserReply(1, request.RequestId, "ready", null, info.Message, Media: info);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or ArgumentException or System.Xml.XmlException or System.Text.Json.JsonException)
            { return new BrowserReply(1, request.RequestId, "rejected", null, ex is IOException ? ex.Message : "This media source could not be inspected."); }
        }
        return await Dispatcher.InvokeAsync(() =>
        {
            if (!IsReady || coordinator is null) return new BrowserReply(1, request.RequestId, "unavailable", null, "Syphon is closing. Open it and retry the same request.");
            if (request.Action == "activate") { RestoreFromTray(); return new BrowserReply(1, request.RequestId, "ready", null, "Syphon is ready."); }
            if (request.Action == "ping") return new BrowserReply(1, request.RequestId, "ready", null, "Syphon is ready.");
            try
            {
                var preferences = DownloadPreferences.Load(preferencesPath);
                BrowserAcceptance? duplicate = null;
                if (request.Action == "download" && coordinator.BrowserStatus(request.RequestId) is null) {
                    var incoming = new Uri(request.Url!);
                    var format = request.Format ?? (YoutubeTools.IsYoutubeVideoTarget(incoming) ? "mp4" : null);
                    var filename = DownloadCoordinator.SuggestedBrowserFilename(incoming, format, request.Title);
                    if (TryExistingDownload(request.Url!, preferences.Folder ?? browserDownloads, filename, true, out var existing, format))
                        duplicate = coordinator.UseExistingBrowserDownload(request.RequestId, existing!.TaskId, incoming, request.Format, request.VideoStream, request.AudioStream, request.YoutubeFormat, request.QualityHeight);
                }
                var accepted = request.Action == "status" ? coordinator.BrowserStatus(request.RequestId)
                    : duplicate ?? coordinator.AcceptBrowserDownload(request.RequestId, new Uri(request.Url!), preferences.Folder ?? browserDownloads, request.Format, request.VideoStream, request.AudioStream, request.YoutubeFormat, request.Title,
                        startImmediately: !HasPendingDuplicateReplacement, singleConnection: preferences.DefaultSingleConnection, context: request.Context, qualityHeight: request.QualityHeight, categorize: true);
                if (accepted?.Task is { } replacement && HasPendingDuplicateReplacement) RegisterDuplicateReplacement(replacement);
                if (accepted is null) return new BrowserReply(1, request.RequestId, "not_found", null, "This request is not in Syphon. Retry the same request to add it.");
                if (accepted.Removed) return new BrowserReply(1, request.RequestId, "removed", accepted.TaskId, "This request was already handled and removed. Choose Syphon this again for a new download.");
                if (!accepted.Duplicate) RevealDownload(accepted.TaskId);
                else { selectedId = accepted.TaskId; commandError = null; RefreshTasks(); }
                if (!accepted.Duplicate && ShowInTaskbar) { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
                return new BrowserReply(1, request.RequestId, "accepted", accepted.TaskId,
                    accepted.Duplicate ? "Already in Syphon. Check the task in the app." : "Added to Syphon. Progress and errors appear in the desktop app.", accepted.Task!.State.ToString());
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { return new BrowserReply(1, request.RequestId, "cancelled", null, "Download cancelled. No new download was added."); }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or DownloadException)
            { return new BrowserReply(1, request.RequestId, "rejected", null, ex.Message); }
            catch (Exception)
            { return new BrowserReply(1, request.RequestId, "unavailable", null, "Syphon could not save the task. Check free space and retry the same request."); }
        }, DispatcherPriority.Normal, token);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Save download", FileName = Path.GetFileName(DestinationBox.Text) };
        if (dialog.ShowDialog(this) == true) DestinationBox.Text = dialog.FileName;
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (!IsReady) return;
        try
        {
            commandError = null;
            if (!Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out var url))
                throw new ArgumentException("Enter an HTTP or HTTPS download link.");
            var task = coordinator!.Add(url, DestinationBox.Text.Trim(), SingleConnectionChoice.IsChecked == true);
            selectedId = task.Id;
            coordinator.Resume(task.Id);
            RefreshTasks();
        }
        catch (Exception ex)
        { commandError = ex.Message; StatusText.Text = ex.Message; }
    }

    private void RefreshTasks()
    {
        if (coordinator is null || closing) return;
        var tasks = coordinator.GetTasks();
        displayTasks = tasks;
        var previousSelection = TaskList.SelectedItem;
        refreshing = true;
        foreach (var stale in rows.Where(r => tasks.All(t => t.Id != r.Snapshot.Id)).ToArray()) rows.Remove(stale);
        foreach (var task in tasks)
        {
            var row = rows.FirstOrDefault(r => r.Snapshot.Id == task.Id);
            if (row is null) rows.Add(new(task)); else row.Update(task);
        }
        System.Windows.Data.CollectionViewSource.GetDefaultView(rows).Refresh();
        TaskList.SelectedItem = rows.FirstOrDefault(r => r.Snapshot.Id == selectedId) ?? rows.LastOrDefault();
        selectedId = (TaskList.SelectedItem as DownloadRow)?.Snapshot.Id;
        refreshing = false;
        if (!ReferenceEquals(previousSelection, TaskList.SelectedItem) && TaskList.SelectedItem is not null)
            TaskList.ScrollIntoView(TaskList.SelectedItem);
        ShowSelected();
        UpdateSummary();
        UpdateFloating(snapshot: tasks);
        UpdateManagerChart(tasks);
        UpdatePlatform(tasks);
        HtmlSurface?.Publish();
    }

    private void TaskList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing) return;
        selectedId = (TaskList.SelectedItem as DownloadRow)?.Snapshot.Id;
        commandError = null;
        ShowSelected();
    }

    private void ShowSelected()
    {
        var task = selectedId is Guid id ? coordinator?.GetTasks().FirstOrDefault(t => t.Id == id) : null;
        var active = task?.State is DownloadState.Queued or DownloadState.RetryWaiting or DownloadState.Starting or DownloadState.Downloading or DownloadState.Assembling or DownloadState.Publishing or DownloadState.Pausing;
        PauseButton.IsEnabled = active && task?.State != DownloadState.Pausing;
        CancelButton.IsEnabled = task is not null && active;
        ResumeButton.IsEnabled = task is not null && !active && task.State != DownloadState.Completed && !task.RestartRequired;
        RestartButton.IsEnabled = task is not null && !active && task.State != DownloadState.Completed;
        OpenFolderButton.IsEnabled = task?.State == DownloadState.Completed;
        RemoveButton.IsEnabled = task is not null;
        TransferBar.IsIndeterminate = task?.State is DownloadState.Starting || task?.State == DownloadState.Downloading && task.Total is null;
        TransferBar.Value = task?.Total is > 0 ? Math.Clamp(task.Received * 100.0 / task.Total.Value, 0, 100)
            : task?.State == DownloadState.Completed ? 100 : 0;
        StatusText.Text = commandError ?? task?.Message ?? "Ready when you are.";
        if (commandError is null && task?.State == DownloadState.RetryWaiting && task.RetryAt is DateTimeOffset retryAt)
            StatusText.Text = $"Retrying in {Math.Max(0, Math.Ceiling((retryAt - DateTimeOffset.UtcNow).TotalSeconds)):0}s · Attempt {task.RetryCount} of 5. You can pause or cancel.";
        DetailText.Text = task is null ? rows.Count == 0 ? "Choose New download to add your first link." : "Select a download to see its details."
            : task.State == DownloadState.Completed ? $"{task.Destination}\nSHA-256: {task.Sha256}"
            : task.State == DownloadState.Downloading
                ? $"{DownloadDisplay.Progress(task)} · {DownloadDisplay.Speed(task)} · {(DownloadDisplay.Remaining(task) == "—" ? "Time remaining unknown" : DownloadDisplay.Remaining(task) + " left")}\n{task.Destination}"
                : $"{DownloadDisplay.Progress(task)}\n{task.Destination}";
        DiagnosticExpander.Visibility = string.IsNullOrWhiteSpace(task?.Diagnostic) ? Visibility.Collapsed : Visibility.Visible;
        DiagnosticText.Text = task?.Diagnostic ?? "";
    }

    private async void Pause_Click(object sender, RoutedEventArgs e) => await StopSelected(false);
    private async void Cancel_Click(object sender, RoutedEventArgs e) => await StopSelected(true);
    private async Task StopSelected(bool cancel)
    {
        if (selectedId is not Guid id || coordinator is null) return;
        try { commandError = null; await coordinator.PauseAsync(id, cancel); RefreshTasks(); }
        catch (Exception ex) { commandError = $"Progress could not be saved: {ex.Message}"; StatusText.Text = commandError; }
    }
    private void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (selectedId is not Guid id || coordinator is null) return;
        try { commandError = null; coordinator.Resume(id); RefreshTasks(); }
        catch (Exception ex) { commandError = ex.Message; StatusText.Text = ex.Message; }
    }
    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (selectedId is not Guid id || coordinator is null) return;
        if (MessageBox.Show(this, "Discard this download’s unfinished data and start again? Existing completed files will be preserved.",
            "Restart download", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { commandError = null; coordinator.Restart(id, SingleConnectionChoice.IsChecked == true); RefreshTasks(); }
        catch (Exception ex) { commandError = ex.Message; StatusText.Text = ex.Message; }
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (selectedId is not Guid id || coordinator is null) return;
        try {
            commandError = null;
            var destination = coordinator.Get(id).Destination; var folder = Path.GetDirectoryName(destination);
            if (!Directory.Exists(folder)) throw new IOException("The download folder is no longer available at its saved location.");
            FolderProcessStarter(new ProcessStartInfo(folder) { UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException) { commandError = ex is Win32Exception ? "The folder could not be opened." : ex.Message; StatusText.Text = commandError; HtmlSurface?.Publish(); }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (selectedId is not Guid id || coordinator is null) return;
        var task = coordinator.Get(id);
        var dialog = new RemoveDownloadDialog(task.Filename, task.State != DownloadState.Completed) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            commandError = null;
            await coordinator.RemoveAsync(id, dialog.DeletePartial);
            selectedId = null;
            RefreshTasks();
        }
        catch (Exception ex) { commandError = ex.Message; StatusText.Text = ex.Message; }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (!explicitExit && !closing && tray?.IsRegistered == true && DownloadPreferences.Load(preferencesPath).CloseToTray) { e.Cancel = true; Hide(); return; }
        e.Cancel = true;
        if (closing) return;
        closing = true;
        CancelIdleAction();
        if (wakeRequested) { WindowsIntegration.KeepAwake(false); wakeRequested = false; }
        tray?.Dispose(); tray = null;
        htmlVerificationCancellation?.Cancel();
        batchCancellation?.Cancel();
        ManagerSpeedChart.Dispose();
        HtmlSurface?.Dispose();
        manualDialog?.Close();
        floatingWindow?.Close();
        foreach (var pending in pendingOptions.Values.ToArray()) pending.Dialog.Close();
        refresh.Stop();
        IsEnabled = false;
        StatusText.Text = "Saving download progress before closing…";
        try
        {
            if (htmlVerificationWorker is { } verification) {
                try { await verification; } catch (Exception) { /* The UI operation reports verification errors. */ }
            }
            if (batchPreviewWork is not null) { try { await batchPreviewWork; } catch (OperationCanceledException) { } }
            if (browserServer is not null) await browserServer.DisposeAsync();
            await (await initialization).DisposeAsync();
        }
        catch (Exception ex) { StatusText.Text = $"Some progress could not be saved: {ex.Message}"; }
        allowClose = true;
        // Disposal may complete synchronously; defer Close until this Closing event unwinds.
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }
}
