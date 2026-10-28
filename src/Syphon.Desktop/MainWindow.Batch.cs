using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Syphon.Engine;

namespace Syphon.Desktop;

public partial class MainWindow
{
    private sealed class BatchRow
    {
        public required Guid Id { get; init; }
        public required string Url { get; init; }
        public required string Filename { get; set; }
        public long? Bytes { get; set; }
        public string Message { get; set; } = "Checking details… You can queue this link now.";
        public bool Valid { get; init; }
        public bool Added { get; set; }
    }
    private Guid? batchId;
    private BatchRow[] batchRows = [];
    private string? batchFolder;
    private CancellationTokenSource? batchCancellation;
    private Task? batchPreviewWork;

    private async Task HandleBatchAsync(string action, JsonElement message)
    {
        if (action == "batch-close") { batchCancellation?.Cancel(); batchId = null; batchRows = []; return; }
        if (action == "batch-browse") {
            var dialog = new OpenFolderDialog { Title = "Save batch downloads to" };
            if (dialog.ShowDialog(this) == true) batchFolder = dialog.FolderName;
            HtmlSurface?.Publish(); return;
        }
        if (action == "batch-preview") {
            var raw = message.GetProperty("text").GetString() ?? "";
            if (raw.Length > 24000) throw new ArgumentException("Paste up to 100 links and 24,000 characters at a time.");
            var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length > 100) throw new ArgumentException("Add at most 100 links per batch.");
            batchCancellation?.Cancel(); batchId = message.GetProperty("batchId").GetGuid(); batchFolder = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            batchRows = lines.Select(line => {
                Uri? url = null; bool valid = false; string note;
                try { url = new Uri(line, UriKind.Absolute); if (url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo) || line.Length > 8192) throw new ArgumentException(); _ = DownloadCoordinator.SuggestedBrowserFilename(url); valid = seen.Add(url.AbsoluteUri); note = valid ? "Checking details… You can queue now." : "Duplicate link — skipped."; }
                catch (Exception ex) when (ex is ArgumentException or UriFormatException or Syphon.Core.DownloadException) { note = "Use a full HTTP or HTTPS download link."; }
                return new BatchRow { Id = Guid.NewGuid(), Url = valid ? url!.AbsoluteUri : line, Valid = valid,
                    Filename = valid ? DownloadCoordinator.SuggestedBrowserFilename(url!, MediaTools.IsManifestUrl(url!.AbsoluteUri) ? "mp4" : null) : "", Message = note };
            }).ToArray();
            HtmlSurface?.Publish();
            var cancellation = new CancellationTokenSource(); batchCancellation = cancellation;
            batchPreviewWork = PreviewBatchAsync(batchRows, batchId.Value, cancellation);
            return;
        }
        if (action != "batch-add" || batchId != message.GetProperty("batchId").GetGuid() || coordinator is null) return;
        var folder = message.GetProperty("folder").GetString()?.Trim() ?? "";
        if (!Path.IsPathFullyQualified(folder) || File.Exists(folder)) throw new ArgumentException("Choose a full download folder path.");
        folder = Path.GetFullPath(folder);
        var immediately = message.GetProperty("immediately").GetBoolean();
        var preferences = DownloadPreferences.Load(preferencesPath);
        foreach (var row in batchRows.Where(r => r.Valid && !r.Added)) {
            try {
                // Each row keeps its request ID for safe retry after a partial batch failure.
                var target = new Uri(row.Url);
                var accepted = coordinator.AcceptBrowserDownload(row.Id, target, folder,
                    mediaFormat: MediaTools.IsManifestUrl(row.Url) ? "mp4" : null,
                    filename: PageVideoTools.IsKnownVideoPage(target) ? null : row.Filename,
                    startImmediately: immediately, singleConnection: preferences.DefaultSingleConnection, categorize: UsesCategoryFolders(folder));
                row.Added = true; row.Message = accepted.Duplicate ? "Already in Syphon." : immediately ? "Added to Syphon." : "Added for later. Select Resume to start.";
            } catch (Exception ex) when (ex is ArgumentException or IOException or Syphon.Core.DownloadException) { row.Message = ex.Message; }
        }
        RefreshTasks();
    }

    private async Task PreviewBatchAsync(BatchRow[] rows, Guid id, CancellationTokenSource cancellation)
    {
        using var slots = new SemaphoreSlim(4);
        try {
            await Task.WhenAll(rows.Where(r => r.Valid).Select(async row => {
                await slots.WaitAsync(cancellation.Token);
                try {
                    var preview = await DownloadPreviewReader.ReadAsync(new(row.Url), cancellation.Token);
                    if (batchId != id || row.Added || closing) return;
                    row.Filename = preview.Filename; row.Bytes = preview.Bytes; row.Message = preview.Message; HtmlSurface?.Publish();
                } finally { slots.Release(); }
            }));
        } catch (OperationCanceledException) { }
        finally { if (batchCancellation == cancellation) batchCancellation = null; cancellation.Dispose(); }
    }
}
