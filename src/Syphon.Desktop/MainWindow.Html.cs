using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Syphon.Core;
using Syphon.Engine;

namespace Syphon.Desktop;

public partial class MainWindow
{
    private readonly bool useHtml;
    public LocalHtmlSurface? HtmlSurface { get; private set; }
    private bool expandedTask;
    private object? historyClearResult;
    private string? htmlError;
    private string htmlSettingsSection="Downloads";
    private Guid? htmlLinkTask;
    private CancellationTokenSource? htmlLinkCancellation;
    private object? htmlLinkResult;
    private Guid? htmlVerificationTask;
    private CancellationTokenSource? htmlVerificationCancellation;
    private Task<FileVerificationResult>? htmlVerificationWorker;
    private object? htmlVerificationResult;
    private Dictionary<string, string>? htmlTools;
    private void InstallHtml()
    {
        MinWidth = 660; MinHeight = 500;
        Width = Math.Min(760, SystemParameters.WorkArea.Width);
        Height = Math.Min(580, SystemParameters.WorkArea.Height);
        CustomWindowFrame.Install(this, resizable: true);
        WindowGeometryStore.Attach(this, Path.Combine(Path.GetDirectoryName(preferencesPath)!, "main-window.json"));
        HtmlSurface = new LocalHtmlSurface(this, (FrameworkElement)Content, HtmlModel, HtmlAction);
        StateChanged += (_, _) => HtmlSurface?.Publish();
    }
    internal static object TaskModel(StoredDownload t) {
        var row = new DownloadRow(t);
        return new { id = t.Id, filename = t.Filename, host = row.Host, sourceUrl = t.Url, fileAvailable = t.State == DownloadState.Completed && File.Exists(t.Destination), state = t.State.ToString(), hasTiming = t.HasTiming, status = row.StatusText, message = t.Message,
            diagnostic = t.Diagnostic, mediaFormat = t.MediaFormat, mediaDuration = DownloadDisplay.MediaDuration(t), mediaDurationSeconds = t.MediaDurationSeconds, attemptIdentity = DownloadStoreIdentity(t), missingTools = MissingMediaTools(t),
            total = t.Total, estimatedTotal = DownloadDisplay.Estimate(t), expectedSize = DownloadDisplay.ExpectedSize(t), estimateSource = t.SizeEstimateSource, received = t.Received, rate = Math.Max(0, t.BytesPerSecond), speed = row.SpeedText, remaining = row.RemainingText.Replace("About ", ""),
            percent = row.ProgressPercent, progress = row.ProgressText.Replace(" / ", " of "), connections = t.Connections,
            segments = t.Segments.Select(s => new { length = s.Length, committed = s.Committed }), canResume = row.CanResume, canPause = row.CanPause,
            fileType = row.FileType, size = row.SizeText, digest = row.DigestText, sha256 = t.Sha256, destination = t.Destination, canUpdateLink = t.MediaFormat is null && t.State is DownloadState.Paused or DownloadState.Failed or DownloadState.Cancelled,
            priority = t.Priority, queueOrder = t.QueueOrder, speedLimit = t.SpeedLimitBytesPerSecond, canLimitSpeed = t.MediaFormat is null && t.State != DownloadState.Completed,
            createdAt = t.CreatedAt, startedAt = t.StartedAt, finishedAt = t.FinishedAt, activeSeconds = t.ActiveSeconds, averageRate = t.AverageBytesPerSecond };
    }
    internal static string[] MissingMediaTools(StoredDownload task) {
        if (task.MediaFormat is null) return [];
        var required = PageVideoTools.RequiresExtractor(task) ? new[] { "yt-dlp", "node", "ffmpeg" } : new[] { "ffmpeg", "ffprobe" };
        return required.Where(name => { try { MediaTools.FindTool(name); return false; } catch (IOException) { return true; } }).ToArray();
    }
    private static string DownloadStoreIdentity(StoredDownload t) {
        var url = new Uri(t.Url);
        var source = url.AbsoluteUri;
        try { if (PageVideoTools.IsKnownVideoPage(url)) source = PageVideoTools.CanonicalUrl(url).AbsoluteUri; } catch (ArgumentException) { }
        var identity = JsonSerializer.Serialize(new { source, t.MediaFormat, t.VideoStream, t.AudioStream, t.YoutubeFormat, t.QualityHeight });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
    }
    private object HtmlModel() {
        var preferences = DownloadPreferences.Load(preferencesPath); var limits = coordinator?.Options ?? preferences.Transfer ?? new TransferOptions();
        // Rows, current speed, floating controller and chart endpoint share one refresh snapshot.
        var tasks = displayTasks;
        var currentRate = tasks.Where(t => t.State == DownloadState.Downloading).Sum(t => Math.Max(0, t.BytesPerSecond));
        var sampledAt = DateTimeOffset.UtcNow;
        return new { kind = "main", isMaximized = WindowState == WindowState.Maximized, page = currentPage == "History" ? "history" : currentPage == "Settings" ? "settings" : "dashboard",
            tasks = tasks.Select(TaskModel).ToArray(), settingsSection = htmlSettingsSection, selectedId, expanded = expandedTask, currentRate, sampledAt,
            samples = managerSamples.Append(currentRate).ToArray(),
            sampleTimes = managerSampleTimes.Append(sampledAt).ToArray(),
            ready = IsReady, initializationError,
            historyClearResult, quickDownload = htmlQuickDownload,
            batch = new { id = batchId, folder = batchFolder, rows = batchRows.Select(r => new { id = r.Id, url = r.Url, filename = r.Filename, bytes = r.Bytes, message = r.Message, valid = r.Valid, added = r.Added }).ToArray() },
            preferences = new { folder = preferences.Folder ?? browserDownloads, preferences.DefaultSingleConnection, preferences.ShowFloatingWindow, preferences.FloatingOnTop, preferences.HistoryDensity, preferences.HistoryGroupAttempts, preferences.TypographyPreset, preferences.CloseToTray, preferences.MinimizeToTray },
            limits = new { perFile = limits.MaximumConnectionsPerFile, perSite = limits.MaximumConnectionsPerOrigin, concurrent = limits.MaximumConcurrentDownloads, retries = limits.MaximumRetries, thresholdMiB = limits.SegmentationThreshold / 1048576, globalKiB = limits.GlobalBytesPerSecond / 1024 },
            schedule = new { enabled = limits.Schedule?.Enabled ?? false, start = ScheduleTime(limits.Schedule?.StartMinute ?? 0), stop = ScheduleTime(limits.Schedule?.StopMinute ?? 360), status = coordinator?.ScheduleStatus ?? "Schedule off" },
            settingsPending = coordinator?.SettingsPending ?? false,
            maxConnections = limits.MaximumConnectionsPerFile * limits.MaximumConcurrentDownloads,
            tools = htmlTools ??= new Dictionary<string, string> { ["FFmpeg"] = ToolStatus("ffmpeg"), ["FFprobe"] = ToolStatus("ffprobe"), ["Video extractor"] = ToolStatus("yt-dlp"), ["JavaScript runtime"] = ToolStatus("node") },
            verifyingFile = htmlVerificationTask, verificationResult = htmlVerificationResult, updatingLink = htmlLinkTask, linkResult = htmlLinkResult, error = htmlError ?? commandError };
    }
    private object? htmlQuickDownload;
    private static string ToolStatus(string name) { try { _ = MediaTools.FindTool(name); return "Installed"; } catch (IOException) { return "Needs setup"; } }
    private static string ScheduleTime(int minute) => $"{minute / 60:00}:{minute % 60:00}";
    private static int ScheduleMinute(string? value) => TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out var time) ? time.Hour * 60 + time.Minute : throw new ArgumentException("Choose valid start and stop times.");
    private async Task HtmlAction(JsonElement message)
    {
        var action = message.GetProperty("action").GetString();
        try {
            htmlError = null;
            if (CustomWindowFrame.Handle(this, action)) return;
            if (action == "navigate") { if(message.TryGetProperty("section",out var setting)&&setting.GetString()=="Network")htmlSettingsSection="Network"; Navigate(message.GetProperty("page").GetString() switch { "history" => "History", "settings" => "Settings", _ => "Downloads" }); HtmlSurface?.Publish(); return; }
            if (!IsReady || coordinator is null) return;
            if(action=="browser-setup"){new BrowserConnectionDialog(this).Show();return;}
            if (action == "update-settings") {
                var settings = new UpdateSettingsWindow(preferencesPath, () => { ApplyPreferences(); InitializePlatform(); }, () => _ = CheckUpdatesAsync(true), ExportDiagnostics) { Owner = this };
                settings.Show(); return;
            }
            if (action?.StartsWith("batch-", StringComparison.Ordinal) == true) { await HandleBatchAsync(action, message); return; }
            if (action == "new") { UrlBox.Text = message.TryGetProperty("url", out var url) ? url.GetString() ?? "" : ""; NewDownload_Click(this, new()); return; }
            if (action == "quick-download") {
                var requestId = message.GetProperty("requestId").GetGuid();
                try {
                    if (pendingOptions.Count > 0 || manualDialog?.IsVisible == true) throw new InvalidOperationException("Finish the open download options window first.");
                    var preferences = DownloadPreferences.Load(preferencesPath);
                    var accepted = CreateManualDownload(requestId, message.GetProperty("url").GetString() ?? "", preferences.Folder ?? browserDownloads, single: preferences.DefaultSingleConnection);
                    htmlQuickDownload = new { requestId, taskId = (Guid?)accepted.TaskId, error = (string?)null };
                    RevealDownload(accepted.TaskId);
                } catch (Exception ex) { htmlQuickDownload = new { requestId, taskId = (Guid?)null, error = ex.Message }; }
                HtmlSurface?.Publish(); return;
            }
            if (action == "history-density") {
                var density = message.GetProperty("density").GetString();
                if (density is not ("comfortable" or "compact")) throw new ArgumentException("Choose Comfortable or Compact history density.");
                (DownloadPreferences.Load(preferencesPath) with { HistoryDensity = density }).Save(preferencesPath);
                HtmlSurface?.Publish(); return;
            }
            if (action == "typography") {
                var preset = message.GetProperty("preset").GetString();
                if (!TypographyComparison.IsValid(preset)) throw new ArgumentException("Choose a supported interface typography preset.");
                (DownloadPreferences.Load(preferencesPath) with { TypographyPreset = preset! }).Save(preferencesPath);
                TypographyComparison.Apply(preset!); HtmlSurface?.Publish(); return;
            }
            if (action == "settings") {
                var value = message.GetProperty("preferences"); var path = value.GetProperty("folder").GetString()?.Trim() ?? "";
                if (!Path.IsPathFullyQualified(path) || File.Exists(path)) throw new ArgumentException("Choose a full folder path.");
                var density = value.TryGetProperty("historyDensity", out var densityValue) ? densityValue.GetString() : DownloadPreferences.Load(preferencesPath).HistoryDensity;
                if (density is not ("comfortable" or "compact")) throw new ArgumentException("Choose Comfortable or Compact history density.");
                var transfer = coordinator.Options;
                if (message.TryGetProperty("limits", out var limits)) {
                    transfer = transfer with { MaximumConnectionsPerFile = limits.GetProperty("perFile").GetInt32(), MaximumConnectionsPerOrigin = limits.GetProperty("perSite").GetInt32(),
                        MaximumConcurrentDownloads = limits.GetProperty("concurrent").GetInt32(), MaximumRetries = limits.GetProperty("retries").GetInt32(),
                        SegmentationThreshold = checked(limits.GetProperty("thresholdMiB").GetInt64() * 1048576) };
                    if (limits.TryGetProperty("globalKiB", out var rate)) transfer = transfer with { GlobalBytesPerSecond = checked(rate.GetInt64() * 1024) };
                    if (transfer.SegmentationThreshold is < 1048576 or > 1073741824) throw new ArgumentException("Choose a segmentation threshold between 1 and 1024 MiB.");
                    transfer.Validate();
                }
                if (message.TryGetProperty("schedule", out var schedule)) {
                    transfer = transfer with { Schedule = new QueueSchedule(schedule.GetProperty("enabled").GetBoolean(), ScheduleMinute(schedule.GetProperty("start").GetString()), ScheduleMinute(schedule.GetProperty("stop").GetString())) };
                    transfer.Validate();
                }
                (DownloadPreferences.Load(preferencesPath) with { CloseToTray = value.TryGetProperty("closeToTray", out var closeTray) && closeTray.GetBoolean(), MinimizeToTray = value.TryGetProperty("minimizeToTray", out var minimizeTray) && minimizeTray.GetBoolean(), Folder = Path.GetFullPath(path), DefaultSingleConnection = value.GetProperty("defaultSingleConnection").GetBoolean(),
                    ShowFloatingWindow = value.GetProperty("showFloatingWindow").GetBoolean(), FloatingOnTop = value.GetProperty("floatingOnTop").GetBoolean(), Transfer = transfer, HistoryDensity = density,
                    HistoryGroupAttempts = value.TryGetProperty("historyGroupAttempts", out var groupAttempts) ? groupAttempts.GetBoolean() : DownloadPreferences.Load(preferencesPath).HistoryGroupAttempts }).Save(preferencesPath);
                ApplyPreferences(); await coordinator.RefreshScheduleAsync(); HtmlSurface?.Publish(); return;
            }
            if (action == "browse-settings") { var picker = new OpenFolderDialog { Title = "Default save location" }; if (picker.ShowDialog(this) == true) { (DownloadPreferences.Load(preferencesPath) with { Folder = picker.FolderName }).Save(preferencesPath); ApplyPreferences(); } HtmlSurface?.Publish(); return; }
            if (action == "pause-all") { await coordinator.PauseAllAsync(); RefreshTasks(); return; }
            if (action == "resume-all") { coordinator.ResumeAll(); RefreshTasks(); return; }
            if (action == "cancel-verification") { htmlVerificationCancellation?.Cancel(); return; }
            if (action == "cancel-link") { htmlLinkCancellation?.Cancel(); return; }
            if (action == "bandwidth") {
                var transfer = coordinator.Options with { GlobalBytesPerSecond = checked(message.GetProperty("kibPerSecond").GetInt64() * 1024) };
                transfer.Validate(); (DownloadPreferences.Load(preferencesPath) with { Transfer = transfer }).Save(preferencesPath);
                ApplyPreferences(); HtmlSurface?.Publish(); return;
            }
            if (action == "clear-history") {
                var operationId = message.GetProperty("operationId").GetGuid();
                try {
                    var values = message.GetProperty("ids");
                    if (values.GetArrayLength() > 1000) throw new ArgumentException("Clear at most 1,000 entries at a time.");
                    var ids = values.EnumerateArray().Select(v => v.GetGuid()).ToArray();
                    var count = coordinator.ClearHistory(ids);
                    if (selectedId is Guid selected && ids.Contains(selected)) selectedId = null;
                    historyClearResult = new { operationId, success = true, count };
                } catch (Exception ex) { historyClearResult = new { operationId, success = false, message = ex.Message }; }
                RefreshTasks(); return;
            }
            if (!message.TryGetProperty("id", out var idValue) || !Guid.TryParse(idValue.GetString(), out var id) || coordinator.GetTasks().All(t => t.Id != id)) return;
            if (action == "verify-file") {
                if (htmlVerificationTask is not null || closing) return;
                var operationId = message.GetProperty("operationId").GetGuid();
                var snapshot = coordinator.Get(id);
                var expected = message.TryGetProperty("expected", out var checksum) ? checksum.GetString() : null;
                htmlVerificationTask = id; htmlVerificationResult = null;
                using var cancellation = new CancellationTokenSource(); htmlVerificationCancellation = cancellation; HtmlSurface?.Publish();
                try {
                    htmlVerificationWorker = Task.Run(() => DownloadFileVerifier.VerifyAsync(snapshot, expected, cancellation.Token));
                    var result = await htmlVerificationWorker;
                    htmlVerificationResult = new { id, operationId, success = true, sha256 = result.Sha256, length = result.Length,
                        matchesRecorded = result.MatchesRecorded, matchesExpected = result.MatchesExpected };
                } catch (Exception ex) {
                    var explanation = ex switch {
                        OperationCanceledException => "File check cancelled.",
                        FileNotFoundException or DirectoryNotFoundException => "The downloaded file could not be found at its saved location.",
                        UnauthorizedAccessException => "Syphon does not have permission to read this file.",
                        ArgumentException or InvalidOperationException => ex.Message,
                        IOException => "The file could not be read. It may be open for editing in another app.",
                        _ => "The file could not be checked."
                    };
                    htmlVerificationResult = new { id, operationId, success = false, message = explanation };
                } finally { htmlVerificationWorker = null; htmlVerificationCancellation = null; htmlVerificationTask = null; if (!closing) HtmlSurface?.Publish(); }
                return;
            }
            if (action == "update-link") {
                if (htmlLinkTask is not null) return;
                var operationId = message.TryGetProperty("operationId", out var operation) && Guid.TryParse(operation.GetString(), out var supplied) ? supplied : Guid.NewGuid();
                htmlLinkTask = id; htmlLinkResult = null;
                using var cancellation = new CancellationTokenSource(); htmlLinkCancellation = cancellation; HtmlSurface?.Publish();
                try {
                    if (!Uri.TryCreate(message.GetProperty("url").GetString(), UriKind.Absolute, out var link)) throw new ArgumentException("Paste a fresh HTTP or HTTPS file link.");
                    await coordinator.ReplaceLinkAsync(id, link, cancellation.Token);
                    htmlLinkResult = new { id, operationId, success = true, message = "Link updated. Resume the download when ready." };
                } catch (Exception ex) {
                    htmlLinkResult = new { id, operationId, success = false, message = ex is OperationCanceledException ? "Link check cancelled. Your existing link and progress were kept." : ex is IOException or ArgumentException or InvalidOperationException or DownloadException ? ex.Message : "The link could not be checked. Your existing link and progress were kept." };
                } finally { htmlLinkCancellation = null; htmlLinkTask = null; RefreshTasks(); }
                return;
            }
            selectedId = id; TaskList.SelectedItem = rows.FirstOrDefault(r => r.Snapshot.Id == id);
            switch (action) {
                case "select": expandedTask = message.TryGetProperty("expanded", out var expanded) && expanded.GetBoolean(); break;
                case "recheck-download-tools": htmlTools = null; break;
                case "pause": await coordinator.PauseAsync(id); break;
                case "cancel": await coordinator.PauseAsync(id, true); break;
                case "resume": coordinator.Resume(id); break;
                case "retry-download":
                    var retryTask = coordinator.Get(id);
                    if (!new DownloadRow(retryTask).CanResume) throw new InvalidOperationException("This transfer cannot be retried right now.");
                    var missingTools = MissingMediaTools(retryTask);
                    if (missingTools.Length > 0) throw new IOException("Install the required media components before retrying: " + string.Join(", ", missingTools));
                    coordinator.Resume(id); break;
                case "priority": coordinator.SetPriority(id, message.GetProperty("priority").GetInt32()); break;
                case "speed-limit": coordinator.SetSpeedLimit(id, checked(message.GetProperty("kibPerSecond").GetInt64() * 1024)); break;
                case "move-up": coordinator.MoveInQueue(id, -1); break;
                case "move-down": coordinator.MoveInQueue(id, 1); break;
                case "floating": UpdateFloating(true); break;
                case "folder": OpenFolder_Click(this, new()); break;
                case "open-file":
                    var file = coordinator.Get(id);
                    if (file.State != DownloadState.Completed || !File.Exists(file.Destination)) throw new IOException("The completed file is no longer available at its saved location.");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file.Destination) { UseShellExecute = true }); break;
                case "copy-checksum":
                    var digest = coordinator.GetTasks().Single(t => t.Id == id).Sha256;
                    if (string.IsNullOrWhiteSpace(digest)) throw new InvalidOperationException("No checksum was recorded for this download.");
                    Clipboard.SetText(digest); break;
                case "copy-url":
                    Clipboard.SetText(coordinator.Get(id).Url); break;
                case "copy-path":
                    Clipboard.SetText(coordinator.Get(id).Destination); break;
                case "restart": _ = Dispatcher.BeginInvoke(new Action(()=>{selectedId=id;Restart_Click(this,new());})); return;
                case "remove": _ = Dispatcher.BeginInvoke(new Action(()=>{selectedId=id;Remove_Click(this,new());})); return;
            }
            RefreshTasks();
        } catch (Exception ex) { htmlError = ex.Message; HtmlSurface?.Publish(); }
    }
}
