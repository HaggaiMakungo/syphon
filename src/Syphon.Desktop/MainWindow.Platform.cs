using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Syphon.Core;
using Syphon.Engine;

namespace Syphon.Desktop;

public partial class MainWindow
{
    private bool platformEnabled;
    private bool platformReady;
    private bool wakeRequested;
    private readonly Dictionary<Guid, DownloadState> observedStates = new();
    private readonly Queue<string> recentErrors = new();
    private readonly QueueIdleAction idleAction = new();
    private Window? countdownWindow;
    private TextBlock? countdownText;
    private DateTimeOffset nextUpdateCheck;
    private ReleaseNotice? availableUpdate;
    private bool checkingUpdate;
    private readonly HashSet<Guid> finishingDownloads = new();
    private bool completedSinceRefresh;
    private readonly HashSet<Guid> cycleDownloads = new();

    private void InitializePlatform()
    {
        if (!platformEnabled) return;
        foreach (var task in coordinator!.GetTasks().Where(t => t.State == DownloadState.Completed && t.ReplacementTarget is not null)) _ = FinishDownloadAsync(task, false);
        try { WindowsIntegration.SetStartup(DownloadPreferences.Load(preferencesPath).StartWithWindows); }
        catch (Exception ex) { RecordPlatformError("Windows startup could not be updated: " + ex.Message); }
    }
    private void RecordPlatformError(string message)
    {
        recentErrors.Enqueue(message); while (recentErrors.Count > 30) recentErrors.Dequeue();
        commandError = message;
    }
    private void UpdatePlatform(StoredDownload[] tasks)
    {
        if (!platformEnabled) return;
        var preferences = DownloadPreferences.Load(preferencesPath);
        var completed = completedSinceRefresh; completedSinceRefresh = false;
        foreach (var task in tasks) {
            if (task.State is DownloadState.Starting or DownloadState.Downloading or DownloadState.Assembling or DownloadState.Publishing or DownloadState.Queued or DownloadState.RetryWaiting or DownloadState.Pausing) cycleDownloads.Add(task.Id);
            if (platformReady && task.State == DownloadState.Completed && observedStates.TryGetValue(task.Id, out var before) && before != DownloadState.Completed) {
                _ = FinishDownloadAsync(task, true);
            }
            if (task.State == DownloadState.Failed && observedStates.GetValueOrDefault(task.Id) != DownloadState.Failed && task.Message is { } error) {
                recentErrors.Enqueue(error); while (recentErrors.Count > 30) recentErrors.Dequeue();
            }
            observedStates[task.Id] = task.State;
        }
        platformReady = true;
        var busy = finishingDownloads.Count > 0 || tasks.Any(t => t.State is DownloadState.Starting or DownloadState.Downloading or DownloadState.Assembling or DownloadState.Publishing or DownloadState.Queued or DownloadState.RetryWaiting or DownloadState.Pausing);
        var awake = preferences.PreventSleep && busy;
        if (awake != wakeRequested) { WindowsIntegration.KeepAwake(awake); wakeRequested = awake; }
        var blocked = tasks.Any(t => cycleDownloads.Contains(t.Id) && (t.State is DownloadState.Paused or DownloadState.Failed or DownloadState.Cancelled || t.RestartRequired));
        if (preferences.CompletionAction == "None") { CancelIdleAction(); }
        else {
            var due = idleAction.Observe(busy, blocked, completed, DateTimeOffset.UtcNow, preferences.CompletionCountdownSeconds);
            if (!busy && blocked) cycleDownloads.Clear();
            if (due is null && countdownWindow is not null) CancelIdleAction();
            if (due is not null) {
                if (countdownWindow is null) ShowCountdown();
                var left = Math.Max(0, (int)Math.Ceiling((due.Value - DateTimeOffset.UtcNow).TotalSeconds));
                if (countdownText is not null) countdownText.Text = $"Queue finished. {preferences.CompletionAction} in {left} seconds.\nNew work cancels this action.";
                if (left == 0) {
                    CancelIdleAction();
                    try { WindowsIntegration.RunCompletionAction(preferences.CompletionAction, preferences.CompletionCommand); wakeRequested = false; }
                    catch (Exception ex) { RecordPlatformError(ex.Message); }
                }
            }
        }
        if (preferences.CheckForUpdates && !checkingUpdate && DateTimeOffset.UtcNow >= nextUpdateCheck) _ = CheckUpdatesAsync(false);
    }
    private async Task FinishDownloadAsync(StoredDownload task, bool announce)
    {
        if (!finishingDownloads.Add(task.Id)) return;
        try {
            var finished = await Task.Run(() => CompleteDuplicateReplacement(task));
            if (closing || !announce) return;
            completedSinceRefresh = true;
            if (DownloadPreferences.Load(preferencesPath).CompletionNotifications)
                tray?.Notify("Download complete", finished.Filename, () => { RestoreFromTray(); ShowCompletion(finished); });
            ShowCompletion(finished);
        } catch (Exception ex) { if (!closing) RecordPlatformError("Download saved, but replacement could not be finished: " + ex.Message); }
        finally { finishingDownloads.Remove(task.Id); }
    }
    private void ShowCountdown()
    {
        countdownWindow = DialogAppearance.Countdown(out var text);
        countdownText = text;
        countdownWindow.Owner = this;
        countdownWindow.Closed += (_, _) => { countdownWindow = null; idleAction.Cancel(); };
        countdownWindow.Show();
    }
    private void CancelIdleAction()
    {
        idleAction.Cancel(); var window = countdownWindow; countdownWindow = null; window?.Close();
    }
    private async Task CheckUpdatesAsync(bool manual)
    {
        if (checkingUpdate) return;
        checkingUpdate = true; nextUpdateCheck = DateTimeOffset.UtcNow.AddHours(12);
        try {
            availableUpdate = await ReleaseUpdates.CheckAsync(manual ? null : DownloadPreferences.Load(preferencesPath).SkippedRelease);
            if (closing) return;
            if (availableUpdate is not null) {
                if (manual) ShowUpdate();
                else tray?.Notify("Syphon update available", availableUpdate.Tag + " is ready. Click to review.", ShowUpdate);
            } else if (manual) DialogAppearance.Notice(this, "Syphon updates", "No newer compatible release was found.");
        } catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException) {
            RecordPlatformError("Update check failed: " + ex.Message);
            if (manual) DialogAppearance.Notice(this, "Syphon updates", "Syphon could not check for updates. Try again later.");
        } finally { checkingUpdate = false; HtmlSurface?.Publish(); }
    }
    private void ShowUpdate()
    {
        if (availableUpdate is not { } release) return;
        RestoreFromTray();
        var window = DialogAppearance.Update(release.Tag, release.Notes,
            () => Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true }),
            () => nextUpdateCheck = DateTimeOffset.UtcNow.AddHours(24),
            () => (DownloadPreferences.Load(preferencesPath) with { SkippedRelease = release.Tag }).Save(preferencesPath));
        window.Owner = this;
        window.Show();
    }
    private async void ExportDiagnostics(bool copy)
    {
        try {
            var versions = await DiagnosticsExport.ToolVersionsAsync();
            if (closing) return;
            var report = DiagnosticsExport.Create(preferencesPath, recentErrors, versions);
            if (copy) { Clipboard.SetText(report); return; }
            var dialog = new SaveFileDialog { Title = "Export diagnostics", FileName = "Syphon-diagnostics.txt", Filter = "Text file|*.txt" };
            if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, report);
        } catch (Exception ex) { RecordPlatformError("Diagnostics could not be exported: " + ex.Message); HtmlSurface?.Publish(); }
    }
}
