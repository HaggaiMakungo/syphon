using System.Text.Json;
using System.Text.RegularExpressions;

namespace Syphon.Engine;

public static partial class YoutubeTools
{
    internal static Func<string[], Action<long>?, CancellationToken, Task<string>>? TestRunner { get; set; }
    private static Task<string> RunAsync(IEnumerable<string> args, Action<long>? progress, CancellationToken token) =>
        TestRunner is { } test ? test(args.ToArray(), progress, token) : MediaTools.RunAsync("yt-dlp", args, progress, token);
    public static bool IsYoutubeHost(Uri url) => url.Host.ToLowerInvariant() is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtu.be" or "www.youtu.be" or "www.youtube-nocookie.com" or "youtube-nocookie.com";
    public static bool IsCaptionUrl(Uri url) => url.Scheme == "https" && url.IsDefaultPort && string.IsNullOrEmpty(url.UserInfo) &&
        url.Host is "youtube.com" or "www.youtube.com" && url.AbsolutePath == "/api/timedtext";
    public static bool IsYoutubeVideoTarget(Uri url) => IsYoutubeHost(url) && !IsCaptionUrl(url);
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")] private static partial Regex IdPattern();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")] private static partial Regex FormatPattern();
    public static bool ValidFormat(string value) => FormatPattern().IsMatch(value);
    [GeneratedRegex(@"(?<!\d)(144|240|360|480|720|1080|1440|2160|4320)p", RegexOptions.IgnoreCase)] private static partial Regex QualityPattern();
    internal static int QualityHeight(string note, int height)
    {
        var match = QualityPattern().Match(note);
        if (match.Success) return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        // Cropped uploads can have heights such as 1920 for YouTube's 2160p tier.
        // The platform label takes precedence; absent that, use only height, never width.
        return new[] { 144, 240, 360, 480, 720, 1080, 1440, 2160, 4320 }.MinBy(tier => Math.Abs((long)tier - height));
    }
    public static string VideoId(Uri url)
    {
        HttpDownloadEngine.ValidateUrl(url);
        if (!IsYoutubeHost(url) || !url.IsDefaultPort) throw new ArgumentException("Choose a YouTube video link.");
        var parts = url.AbsolutePath.Trim('/').Split('/'); string? id = null;
        if (url.Host is "youtu.be" or "www.youtu.be" && parts.Length == 1) id = parts[0];
        else if (parts.Length == 2 && parts[0] is "shorts" or "embed" or "live") id = parts[1];
        else if (url.AbsolutePath == "/watch")
        {
            var ids = url.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0] == "v").Select(p => Uri.UnescapeDataString(p[1])).Distinct().ToArray();
            if (ids.Length == 1) id = ids[0];
        }
        if (id is null || !IdPattern().IsMatch(id)) throw new ArgumentException("Choose an individual YouTube video, Shorts or embed link. Playlists and channels are not supported.");
        return id;
    }
    public static Uri CanonicalUrl(Uri url) => new("https://www.youtube.com/watch?v=" + VideoId(url));

    internal static string[] CommonArguments() => ["--ignore-config", "--no-config-locations", "--no-plugin-dirs", "--no-remote-components",
        "--no-js-runtimes", "--js-runtimes", "node:" + MediaTools.FindTool("node"), "--no-cache-dir", "--no-playlist", "--use-extractors", "youtube",
        "--socket-timeout", "10", "--retries", "1", "--extractor-retries", "1", "--fragment-retries", "1", "--no-warnings", "--color", "never"];

    public static async Task<MediaInfo> InspectAsync(Uri url, CancellationToken token = default)
    {
        var canonical = CanonicalUrl(url);
        if (TestRunner is null) { MediaTools.FindTool("yt-dlp"); MediaTools.FindTool("node"); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(35));
        string json;
        try
        {
            var args = CommonArguments().Concat(new[] { "--skip-download", "--print", "%(.{id,title,live_status,duration,availability,formats})j", "--", canonical.AbsoluteUri });
            json = await RunAsync(args, null, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("YouTube inspection timed out. Try again later or update the YouTube extractor."); }
        return ParseInfo(json);
    }

    internal static MediaInfo ParseInfo(string json)
    {
        using var document = JsonDocument.Parse(json); var root = document.RootElement;
        string Text(JsonElement e, string key) => e.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        int Number(JsonElement e, string key) => e.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : 0;
        double? duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds > 0 ? seconds : null;
        long? Size(JsonElement format) {
            foreach (var key in new[] { "filesize", "filesize_approx" })
                if (format.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var size) && size > 0) return size;
            return format.TryGetProperty("tbr", out var rate) && rate.ValueKind == JsonValueKind.Number && rate.TryGetDouble(out var kbps)
                ? MediaTools.EstimateBytes(duration, kbps * 1000) : null;
        }
        if (Text(root, "live_status") is "is_live" or "is_upcoming" or "post_live") throw new IOException("YouTube live/upcoming videos are not supported. Choose an available on-demand video.");
        if (Text(root, "availability") is "private" or "premium_only" or "subscriber_only" or "needs_auth") throw new IOException("This YouTube video requires an account or subscription. Syphon does not import browser sessions.");
        if (!root.TryGetProperty("formats", out var formats) || formats.ValueKind != JsonValueKind.Array) throw new IOException("YouTube returned no downloadable formats.");
        var audioFormats = formats.EnumerateArray().Where(f => !(f.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True)
            && ValidFormat(Text(f, "format_id")) && Text(f, "vcodec") == "none" && Text(f, "acodec") is not ("" or "none"))
            .OrderByDescending(f => Text(f, "ext") == "m4a").ThenByDescending(f => Size(f) ?? 0).ToArray();
        var audioSize = audioFormats.Length > 0 ? Size(audioFormats[0]) : null;
        var choices = new List<MediaChoice>(); bool audio = false;
        foreach (var format in formats.EnumerateArray())
        {
            if (format.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True) continue;
            var id = Text(format, "format_id"); if (!ValidFormat(id)) continue;
            var acodec = Text(format, "acodec"); if (acodec.Length > 0 && acodec != "none") audio = true;
            var codec = Text(format, "vcodec"); var height = Number(format, "height");
            if (codec.Length == 0 || codec == "none" || height <= 0) continue;
            long? bitrate = format.TryGetProperty("tbr", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetDouble(out var rate) && double.IsFinite(rate) && rate is > 0 and < 1000000 ? (long)(rate * 1000) : null;
            var size = Size(format);
            if (acodec is "" or "none") size = size is long videoBytes && audioSize is long audioBytes && videoBytes <= long.MaxValue - audioBytes ? videoBytes + audioBytes : null;
            choices.Add(new(0, null, Number(format, "width"), height, codec.Length > 40 ? codec[..40] : codec, bitrate, id, QualityHeight(Text(format, "format_note"), height), size));
        }
        choices = choices.GroupBy(c => (c.QualityHeight, c.Codec)).Select(g => g.OrderByDescending(c => c.Bitrate).First())
            .OrderByDescending(c => c.QualityHeight).ThenByDescending(c => c.Bitrate).Take(32).ToList();
        if (choices.Count == 0) throw new IOException("No unprotected YouTube video formats were available.");
        var title = Text(root, "title");
        return new(choices.ToArray(), audio, duration, "YouTube qualities detected. Choose a quality or convert audio to MP3.", title.Length > 512 ? title[..512] : title);
    }

    internal static string FormatSelector(string? selected, string output)
    {
        if (output == "mp3") return "bestaudio/best";
        if (selected is null) return "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/bestvideo+bestaudio/best";
        if (!ValidFormat(selected)) throw new ArgumentException("Invalid YouTube format.");
        return $"{selected}[acodec=none]+bestaudio[ext=m4a]/{selected}[acodec=none]+bestaudio/{selected}";
    }
    internal static string OutputTemplate(StoredDownload task) => (task.PartPath + ".yt.").Replace("%", "%%") + "%(ext)s";

    internal static async Task DownloadAsync(StoredDownload task, Action<long> progress, CancellationToken token, Action<MediaInfo>? inspected = null)
    {
        var canonical = CanonicalUrl(new(task.Url));
        var info = await InspectAsync(canonical, token).ConfigureAwait(false);
        if (task.YoutubeFormat is not null && !info.Qualities.Any(q => q.YoutubeFormat == task.YoutubeFormat))
            throw new IOException("That YouTube quality is no longer available. Refresh the video menu and choose again.");
        token.ThrowIfCancellationRequested();
        inspected?.Invoke(info);
        var prefix = task.PartPath + ".yt.";
        Cleanup(task);
        try
        {
            var args = CommonArguments().Concat(new[] { "--no-overwrites", "--no-continue", "--no-part", "--no-keep-fragments", "--progress", "--newline", "--progress-template", "download:total_size=%(progress.downloaded_bytes)s",
                "--ffmpeg-location", Path.GetDirectoryName(MediaTools.FindTool("ffmpeg"))!, "--format", FormatSelector(task.YoutubeFormat, task.MediaFormat!), "--output", OutputTemplate(task),
                "--match-filters", "live_status!=is_live & live_status!=is_upcoming & live_status!=post_live" }).ToList();
            if (task.MediaFormat == "mp3") args.AddRange(["--extract-audio", "--audio-format", "mp3", "--audio-quality", "2"]);
            else args.AddRange(["--merge-output-format", "mp4", "--remux-video", "mp4"]);
            args.AddRange(["--", canonical.AbsoluteUri]);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromHours(12));
            await RunAsync(args, progress, deadline.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var result = prefix + task.MediaFormat;
            if (!File.Exists(result) || new FileInfo(result).Length == 0) throw new IOException("YouTube processing did not produce the requested output.");
            File.Move(result, task.PartPath, overwrite: false);
        }
        finally { Cleanup(task); }
    }

    internal static void Cleanup(StoredDownload task)
    {
        var directory = Path.GetDirectoryName(task.PartPath)!;
        if (!Directory.Exists(directory)) return;
        var prefix = Path.GetFileName(task.PartPath) + ".yt.";
        // Only flat scratch files beginning with this GUID-based task's complete filename prefix.
        foreach (var file in Directory.EnumerateFiles(directory))
            if (Path.GetFileName(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
    }
}
