using System.IO;
using System.Windows;
using System.Windows.Threading;
using Syphon.BrowserBridge;
using Syphon.Engine;

namespace Syphon.Desktop;

public partial class MainWindow
{
    private sealed record PendingOptions(BrowserRequest Request, DownloadOptionsDialog Dialog, TaskCompletionSource<BrowserReply> Completion);
    private readonly Dictionary<Guid, PendingOptions> pendingOptions = new();
    private string preferencesPath = "";

    private async Task<BrowserReply> ShowDownloadOptionsAsync(BrowserRequest request, CancellationToken token)
    {
        var operation = await Dispatcher.InvokeAsync(() => PrepareDownloadOptions(request, token), DispatcherPriority.Normal, token);
        return await operation.WaitAsync(token);
    }

    private Task<BrowserReply> PrepareDownloadOptions(BrowserRequest request, CancellationToken token)
    {
        BrowserReply Reply(string status, string message) => new(1, request.RequestId, status, null, message);
        if (!IsReady || coordinator is null) return Task.FromResult(Reply("unavailable", "Syphon is closing. Try again after reopening."));
        if (coordinator.BrowserStatus(request.RequestId) is not null) return HandleBrowserRequestAsync(request with { ShowOptions = false }, token);
        if (pendingOptions.TryGetValue(request.RequestId, out var existing)) {
            if (request.Url != existing.Request.Url || request.Format != existing.Request.Format || request.YoutubeFormat != existing.Request.YoutubeFormat || request.VideoStream != existing.Request.VideoStream || request.AudioStream != existing.Request.AudioStream || request.QualityHeight != existing.Request.QualityHeight)
                return Task.FromResult(Reply("rejected", "This request already belongs to a different download."));
            if (ShowInTaskbar) existing.Dialog.Activate();
            return existing.Completion.Task;
        }
        if (pendingOptions.Count >= 1 || manualDialog?.IsVisible == true) return Task.FromResult(Reply("rejected", "Finish the open download options window, then try this download again."));
        var url = new Uri(request.Url!);
        var format = request.Format ?? (PageVideoTools.IsKnownVideoPage(url) ? "mp4" : null);
        var preferences = DownloadPreferences.Load(preferencesPath);
        var folder = preferences.Folder;
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) folder = browserDownloads;
        var name = DownloadCoordinator.SuggestedBrowserFilename(url, format, request.Title, request.VideoStream, request.YoutubeFormat);
        var dialog = new DownloadOptionsDialog(request.Title ?? name, name, folder, request.Quality ?? (format == "mp3" ? "MP3 audio" : null), request.EstimatedBytes, preferences.Folder is not null, request.Url, singleConnection: preferences.DefaultSingleConnection, htmlUi: useHtml) { Owner = IsVisible ? this : null };
        if (!ShowInTaskbar) { dialog.ShowInTaskbar = false; dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = Left; dialog.Top = Top; }
        var completion = new TaskCompletionSource<BrowserReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingOptions.Add(request.RequestId, new(request, dialog, completion));
        var cancellation = token.Register(() => { _ = Dispatcher.BeginInvoke(new Action(() => { if (dialog.IsVisible) dialog.Close(); })); });
        dialog.Confirmed += () => {
            if (token.IsCancellationRequested || !IsReady) { dialog.Close(); return; }
            try {
                DownloadCoordinator.ValidateBrowserFilename(dialog.Filename, format);
                if (TryExistingDownload(request.Url!, dialog.Folder, dialog.Filename, dialog.StartImmediately, out var existing, format)) {
                    var reused = coordinator.UseExistingBrowserDownload(request.RequestId, existing!.TaskId, url, request.Format, request.VideoStream, request.AudioStream, request.YoutubeFormat, request.QualityHeight);
                    completion.TrySetResult(new(1, request.RequestId, "accepted", reused.TaskId, "Using the existing download.", reused.Task!.State.ToString()));
                    dialog.Close(); return;
                }
                var accepted = coordinator.AcceptBrowserDownload(request.RequestId, url, dialog.Folder, request.Format, request.VideoStream, request.AudioStream, request.YoutubeFormat, request.Title,
                    dialog.Filename, dialog.StartImmediately && !HasPendingDuplicateReplacement, dialog.SingleConnection, dialog.Priority, request.Context, request.QualityHeight, categorize: UsesCategoryFolders(dialog.Folder));
                RegisterDuplicateReplacement(accepted.Task!, dialog.StartImmediately);
                var message = dialog.StartImmediately ? "Added to Syphon. Your download is starting." : "Added to queue. Select Resume in Syphon when ready.";
                try { (DownloadPreferences.Load(preferencesPath) with { Folder = dialog.RememberFolder ? dialog.Folder : null }).Save(preferencesPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { message += " The folder preference could not be saved."; }
                RevealDownload(accepted.TaskId);
                completion.TrySetResult(new(1, request.RequestId, "accepted", accepted.TaskId, message, accepted.Task!.State.ToString()));
                dialog.Close();
            }
            catch (OperationCanceledException) { dialog.Close(); }
            catch (Exception ex) { dialog.ShowError(ex.Message); }
        };
        dialog.Closed += (_, _) => {
            pendingOptions.Remove(request.RequestId); cancellation.Dispose();
            completion.TrySetResult(Reply("cancelled", "Download cancelled. Nothing was added to Syphon."));
        };
        dialog.Show();
        return completion.Task;
    }
}
