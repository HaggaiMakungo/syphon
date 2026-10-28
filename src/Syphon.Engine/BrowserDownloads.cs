using Syphon.Core;

namespace Syphon.Engine;

public sealed record BrowserAcceptance(Guid TaskId, StoredDownload? Task, bool Duplicate, bool Removed);

public sealed partial class DownloadCoordinator
{
    public BrowserAcceptance? BrowserStatus(Guid requestId)
    {
        lock (gate)
        {
            var receipt = store.FindBrowserRequest(requestId);
            if (receipt is null) return null;
            var task = tasks.GetValueOrDefault(receipt.Value.TaskId);
            return new(receipt.Value.TaskId, task, true, task is null);
        }
    }

    public BrowserAcceptance AcceptBrowserDownload(Guid requestId, Uri url, string downloadDirectory,
        string? mediaFormat = null, int? videoStream = null, int? audioStream = null, string? youtubeFormat = null, string? title = null,
        string? filename = null, bool startImmediately = true, bool singleConnection = false, int priority = 0, DownloadRequestContext? context = null, int? qualityHeight = null, bool categorize = false)
    {
        context?.Validate(url.AbsoluteUri);
        if (qualityHeight is not null && (mediaFormat != "mp4" || PageVideoTools.IsKnownVideoPage(url) || videoStream is not null || youtubeFormat is not null || qualityHeight is not (144 or 240 or 360 or 480 or 720 or 1080 or 1440 or 2160 or 4320))) throw new ArgumentException("Invalid advertised video quality.");
        if (context is not null && (mediaFormat is not null || PageVideoTools.IsKnownVideoPage(url))) throw new ArgumentException("Browser sign-in sharing currently supports direct files.");
        if (priority is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(priority));
        if (requestId == Guid.Empty || url.AbsoluteUri.Length > 8192) throw new ArgumentException("Invalid browser request.");
        HttpDownloadEngine.ValidateUrl(url);
        if (title?.Length > 512) throw new ArgumentException("Invalid video title.");
        if (PageVideoTools.IsKnownVideoPage(url)) { url = PageVideoTools.CanonicalUrl(url); mediaFormat ??= "mp4"; }
        if (youtubeFormat is not null && (!PageVideoTools.IsPageCandidate(url) || !YoutubeTools.ValidFormat(youtubeFormat))) throw new ArgumentException("Invalid video format.");
        if (mediaFormat is not (null or "mp3" or "mp4") || videoStream is < 0 or > 255 || audioStream is < 0 or > 255 ||
            mediaFormat is null && (videoStream is not null || audioStream is not null)) throw new ArgumentException("Invalid media selection.");
        // Plain download requests preserve their legacy receipt identity. Explicit media requests include all options.
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            var receipt = store.FindBrowserRequest(requestId);
            if (receipt is not null)
            {
                if (receipt.Value.UrlHash != DownloadStore.BrowserIdentityHash(url.AbsoluteUri, mediaFormat, videoStream, audioStream, youtubeFormat, qualityHeight))
                    throw new InvalidDataException("This request ID already belongs to a different link.");
                var existing = tasks.GetValueOrDefault(receipt.Value.TaskId);
                if (existing is not null && context is not null) {
                    Set(existing with { RequiresBrowserContext = true }); KeepContext(existing.Id, context); existing = tasks[existing.Id];
                }
                return new(receipt.Value.TaskId, existing, true, existing is null);
            }
            // A fresh browser gesture after reopening can refresh an existing signed-in job.
            var refresh = context is null ? null : tasks.Values.FirstOrDefault(t => t.Url == url.AbsoluteUri && t.RequiresBrowserContext &&
                !browserContexts.ContainsKey(t.Id) && !updatingLinks.Contains(t.Id) && !t.RestartRequired &&
                (filename is null || filename == t.Filename) && t.State is DownloadState.Paused or DownloadState.Failed);
            if (refresh is not null)
            {
                Set(refresh with { BrowserRequestId = requestId, Message = "Browser sign-in refreshed. Ready to resume." });
                KeepContext(refresh.Id, context!);
                try { if (startImmediately) Resume(refresh.Id); } catch (Exception ex) { RecordFailure(refresh.Id, ex); }
                return new(refresh.Id, tasks[refresh.Id], false, false);
            }
            var folder = Path.GetFullPath(downloadDirectory);
            var name = filename ?? SuggestedBrowserFilename(url, mediaFormat, title, videoStream, youtubeFormat);
            if (filename is not null) ValidateBrowserFilename(filename, mediaFormat);
            if (categorize) { DownloadCategories.EnsureFolders(folder); folder = DownloadCategories.Resolve(folder, name, mediaFormat); }
            var destination = Path.Combine(folder, name);
            var stem = Path.GetFileNameWithoutExtension(name); var extension = Path.GetExtension(name);
            var number = 2;
            while (File.Exists(destination) || Directory.Exists(destination) || tasks.Values.Any(t => string.Equals(t.Destination, destination, StringComparison.OrdinalIgnoreCase)))
            {
                if (number > 10000) throw new IOException("Too many files have that name. Choose a destination in Syphon.");
                destination = Path.Combine(folder, $"{stem} ({number++}){extension}");
            }
            var id = Guid.NewGuid();
            var task = new StoredDownload { Id = id, BrowserRequestId = requestId, Url = url.AbsoluteUri,
                Destination = destination, PartPath = Path.Combine(folder, $".{Path.GetFileName(destination)}.{id:N}.syphon-part"),
                MediaFormat = mediaFormat, QualityHeight = qualityHeight, VideoStream = videoStream, AudioStream = audioStream, YoutubeFormat = youtubeFormat,
                PreserveFilename = filename is not null, SingleConnection = singleConnection, Priority = priority, RequiresBrowserContext = context is not null, State = startImmediately ? DownloadState.Queued : DownloadState.Paused,
                Message = startImmediately ? "Ready to download." : "Added to queue. Select Resume to start." };
            // Task and receipt commit together before acknowledging ownership to the browser.
            Set(task);
            if (context is not null) KeepContext(id, context);
            try { if (startImmediately) Resume(task.Id); }
            catch (Exception ex) { RecordFailure(task.Id, ex); }
            return new(task.Id, tasks[task.Id], false, false);
        }
    }

    public static string SuggestedBrowserFilename(Uri url, string? mediaFormat = null, string? title = null, int? videoStream = null, string? youtubeFormat = null)
    {
        if (YoutubeTools.IsCaptionUrl(url)) return SafeFilename("captions-" + (Uri.UnescapeDataString(url.Query.TrimStart('?').Split('&').FirstOrDefault(q => q.StartsWith("lang="))?[5..] ?? "subtitles"))) + ".vtt";
        var youtube = PageVideoTools.IsKnownVideoPage(url)||mediaFormat is not null&&videoStream is null&&PageVideoTools.IsPageCandidate(url);
        if (youtube) mediaFormat ??= "mp4";
        var name = youtube ? PageVideoTools.PlaceholderStem(url) + (youtubeFormat is null ? "" : "-" + youtubeFormat) + ".mp4" : SafeBrowserFilename(url);
        if (mediaFormat is not null) name = Path.GetFileNameWithoutExtension(name) + (videoStream is int index ? $"-stream{index}" : "") + "." + mediaFormat;
        if (!string.IsNullOrWhiteSpace(title)) {
            var stem = SafeFilename(title);
            if (stem.Length > 100) stem = stem[..100];
            if (stem.Length > 0 && char.IsHighSurrogate(stem[^1])) stem = stem[..^1];
            name = stem.TrimEnd(' ', '.') + Path.GetExtension(name);
        }
        return name;
    }

    public static void ValidateBrowserFilename(string filename, string? mediaFormat = null)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename != SafeFilename(filename) || Path.GetFileName(filename) != filename)
            throw new ArgumentException("Choose a filename up to 120 characters without reserved names or characters such as / \\ : * ?.");
        if (mediaFormat is not null && !string.Equals(Path.GetExtension(filename), "." + mediaFormat, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Keep the .{mediaFormat} extension for this download.");
    }

    public static string SafeBrowserFilename(Uri url)
    {
        var path = url.AbsolutePath;
        var last = path[(path.LastIndexOf('/') + 1)..];
        return SafeFilename(Uri.UnescapeDataString(last));
    }

    internal static string SafeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        name = string.Concat(name.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c)).Trim(' ', '.');
        if (name.Length > 120) name = name[..120];
        if (name.Length > 0 && char.IsHighSurrogate(name[^1])) name = name[..^1];
        name = name.TrimEnd(' ', '.');
        var stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (name.Length == 0) name = "download.bin";
        else if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" or "CLOCK$" ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "0123456789¹²³".Contains(stem[3])) name = "_" + name;
        return name;
    }
}
