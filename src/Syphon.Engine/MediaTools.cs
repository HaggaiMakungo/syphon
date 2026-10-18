using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Syphon.Engine;

public sealed record MediaChoice(int VideoStream, int? AudioStream, int Width, int Height, string Codec, long? Bitrate, string? YoutubeFormat = null, int? QualityHeight = null, long? EstimatedBytes = null, bool ResolveAtDownload = false);
public sealed record MediaCaption(string Label, string Note);
public sealed record MediaInfo(MediaChoice[] Qualities, bool HasAudio, double? Duration, string Message, string? Title = null, MediaCaption[]? Captions = null);

/// <summary>Unprotected VOD inspection/conversion. No cookies, keys, local input protocols or arbitrary CLI arguments.</summary>
public static partial class MediaTools
{
    internal static Action<string>? TestToolError { get; set; }
    public static string FindTool(string name)
    {
        // Packaged builds never consult user-controlled environment or search paths.
        if (MediaToolResolver.IsPackagedBuild)
            return MediaToolResolver.Resolve(name, true, AppContext.BaseDirectory, null, []);
        return MediaToolResolver.Resolve(name, false, AppContext.BaseDirectory,
            Environment.GetEnvironmentVariable("SYPHON_MEDIA_DIRECTORY"),
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
    }
    public static bool IsManifestUrl(string url) => ManifestExtension().IsMatch(new Uri(url).AbsolutePath);
    [GeneratedRegex(@"\.(m3u8|mpd)$", RegexOptions.IgnoreCase)] private static partial Regex ManifestExtension();
    [GeneratedRegex("(?:^|,)URI=\"([^\"]+)\"", RegexOptions.IgnoreCase)] private static partial Regex HlsUri();

    public static async Task<MediaInfo> InspectAsync(Uri url, CancellationToken token = default, bool boundedProbe = false, bool fast = true)
    {
        if (YoutubeTools.IsYoutubeVideoTarget(url)) return await YoutubeTools.InspectAsync(url, token).ConfigureAwait(false);
        if (fast && IsManifestUrl(url.AbsoluteUri)) {
            try { var advertised = await InspectManifestMetadataAsync(url, token).ConfigureAwait(false); if (advertised is not null) return advertised; }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (HttpRequestException) { }
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var manifestDuration = await InspectDurationAsync(url, timeout.Token).ConfigureAwait(false);
            var arguments = InputOptions(url).Concat(boundedProbe ? new[] { "-probesize", "262144", "-analyzeduration", "1000000" } : [])
                .Concat(new[] { "-i", url.AbsoluteUri, "-show_streams", "-show_programs", "-show_format", "-of", "json" }).ToArray();
            var json = await RunAsync("ffprobe", arguments, null, timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var streams = root.GetProperty("streams").EnumerateArray().ToArray();
            int? Index(JsonElement stream) => stream.TryGetProperty("index", out var n) && n.TryGetInt32(out var index) && index is >= 0 and <= 255 ? index : null;
            string Type(JsonElement stream) => stream.TryGetProperty("codec_type", out var value) ? value.GetString() ?? "" : "";
            var audios = streams.Where(s => Type(s) == "audio").Select(Index).OfType<int>().ToArray();
            var choices = new List<MediaChoice>();
            foreach (var stream in streams.Where(s => Type(s) == "video").Take(32))
            {
                if (Index(stream) is not int index) continue;
                int? audio = audios.Length > 0 ? audios[0] : null;
                if (root.TryGetProperty("programs", out var programs))
                    foreach (var program in programs.EnumerateArray())
                    {
                        var members = program.GetProperty("streams").EnumerateArray().ToArray();
                        if (members.Any(s => Index(s) == index))
                        { audio = members.Where(s => Type(s) == "audio").Select(Index).OfType<int>().Select(i => (int?)i).FirstOrDefault(); break; }
                    }
                int Dimension(string field) => stream.TryGetProperty(field, out var n) && n.TryGetInt32(out var value) ? value : 0;
                var codec = stream.TryGetProperty("codec_name", out var c) ? c.GetString() ?? "unknown" : "unknown";
                long? bitrate = stream.TryGetProperty("bit_rate", out var b) && long.TryParse(b.GetString(), out var rate) ? rate : null;
                choices.Add(new(index, audio, Dimension("width"), Dimension("height"), codec, bitrate));
            }
            double? duration = manifestDuration;
            if (root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var d) &&
                double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds)) duration = seconds;
            if (choices.Count == 0 && audios.Length == 0) throw new IOException("No playable audio or video streams were found.");
            choices = choices.Select(choice => {
                var audio = streams.FirstOrDefault(s => Index(s) == choice.AudioStream);
                long? audioRate = audio.ValueKind == JsonValueKind.Object && audio.TryGetProperty("bit_rate", out var ab) && long.TryParse(ab.GetString(), out var ar) && ar > 0 ? ar : null;
                return choice with { EstimatedBytes = choice.Bitrate is > 0 && (choice.AudioStream is null || audioRate is not null)
                    ? EstimateBytes(duration, (double)choice.Bitrate.Value + (audioRate ?? 0)) : null };
            }).ToList();
            return new(choices.OrderByDescending(c => c.Height).ThenByDescending(c => c.Bitrate).ToArray(), audios.Length > 0, duration,
                "Detected streams. Login sessions and protected playback are not transferred.", Captions: streams.Where(s => Type(s) == "subtitle").Take(32).Select(s => {
                    var language = s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var lang) ? lang.GetString() : null;
                    return new MediaCaption(language ?? "Closed captions", "Embedded subtitle track — separate saving is not available yet");
                }).ToArray());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException("Media inspection timed out. Try a fresh direct media link."); }
    }

    internal static long? EstimateBytes(double? duration, double bitrate)
    {
        var bytes = duration * bitrate / 8;
        return bytes is > 0 and < long.MaxValue && double.IsFinite(bytes.Value) ? (long)Math.Ceiling(bytes.Value) : null;
    }

    internal static string[] InputOptions(Uri url) => new[] { "-v", "error", "-protocol_whitelist", url.Scheme == "https" ? "http,https,tcp,tls" : "http,tcp",
        "-format_whitelist", "mov,matroska,webm,hls,dash,mpegts,mp3,aac,ogg,wav,flac", "-rw_timeout", "10000000" }
        .Concat(url.Scheme == "https" ? new[] { "-tls_verify", "1" } : []).ToArray();

    internal static async Task ConvertAsync(StoredDownload task, Action<long> progress, CancellationToken token, Action<long?>? estimatedSize = null, Action<long, double>? mediaProgress = null, Action<MediaInfo>? inspected = null)
    {
        var videoStream = task.VideoStream; var audioStream = task.AudioStream;
        if (task.MediaFormat == "mp4" && videoStream is null)
        {
            var info = await InspectAsync(new(task.Url), token, fast: false).ConfigureAwait(false);
            inspected?.Invoke(info);
            var best = task.QualityHeight is int height ? info.Qualities.FirstOrDefault(q => DisplayTier(q.Height) == height) ?? throw new IOException("The selected quality is no longer available. Refresh the video menu and choose again.") : info.Qualities.FirstOrDefault()
                ?? throw new IOException("This source has no downloadable video stream.");
            if (audioStream is null || audioStream == best.AudioStream) estimatedSize?.Invoke(best.EstimatedBytes);
            videoStream = best.VideoStream; audioStream ??= best.AudioStream;
        }
        await ValidateManifestAsync(new(task.Url), token).ConfigureAwait(false);
        var args = new List<string> { "-nostdin", "-hide_banner", "-n", "-stats_period", "0.5", "-progress", "pipe:1" };
        args.AddRange(InputOptions(new(task.Url))); args.AddRange(["-i", task.Url]);
        if (task.MediaFormat == "mp3")
        {
            args.AddRange(["-map", audioStream is int a ? $"0:{a}" : "0:a:0", "-vn", "-sn", "-dn", "-c:a", "libmp3lame", "-q:a", "2", "-f", "mp3"]);
        }
        else
        {
            args.AddRange(["-map", videoStream is int v ? $"0:{v}" : "0:v:0?", "-map", audioStream is int a ? $"0:{a}" : "0:a:0?",
                "-sn", "-dn", "-c", "copy", "-movflags", "+faststart", "-f", "mp4"]);
        }
        args.Add(task.PartPath);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromHours(12));
        try { await RunAsync("ffmpeg", args, progress, deadline.Token, mediaProgress).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("Media job exceeded its 12-hour processing limit."); }
    }

    internal static async Task<string> RunAsync(string tool, IEnumerable<string> arguments, Action<long>? progress, CancellationToken token, Action<long, double>? mediaProgress = null)
    {
        var start = new ProcessStartInfo(FindTool(tool)) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Media tool could not be started.");
        using var job = MediaProcessJob.Attach(process);
        process.StandardInput.Close();
        using var stop = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        // Persist only classified explanations. Raw output remains available solely to explicit test hooks.
        var classification = new MediaErrorClassifier();
        var diagnostic = TestToolError is null ? null : new StringBuilder();
        var errors = DrainAsync(process.StandardError, token, diagnostic, classification);
        var result = new StringBuilder();
        var output = ReadAsync();
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(token), output, errors).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) { if (diagnostic is not null) TestToolError?.Invoke(diagnostic.ToString()); throw classification.Failure(tool, process.ExitCode); }
            return result.ToString();
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        async Task ReadAsync()
        {
            if (progress is null)
            {
                var buffer = new char[4096]; int count;
                while ((count = await process.StandardOutput.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    if (result.Length + count > 2 * 1024 * 1024) { process.Kill(true); throw new IOException("Media inspection exceeded its response limit."); }
                    result.Append(buffer, 0, count);
                }
            }
            else {
                long outputBytes = -1; double outputSeconds = 0;
                while (await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) is string line)
                {
                    if (line.StartsWith("total_size=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(11), out var bytes) && bytes >= 0) { outputBytes = bytes; progress(bytes); }
                    else if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(12), out var micros) && micros > 0) outputSeconds = micros / 1000000.0;
                    else if (line.StartsWith("progress=", StringComparison.Ordinal)) {
                        if (outputBytes >= 0 && outputSeconds > 0) mediaProgress?.Invoke(outputBytes, outputSeconds);
                        outputBytes = -1; outputSeconds = 0;
                    }
                }
            }
        }
    }
    private static async Task DrainAsync(StreamReader reader, CancellationToken token, StringBuilder? diagnostic, MediaErrorClassifier classification)
    { var buffer = new char[4096]; int count; while ((count = await reader.ReadAsync(buffer, token).ConfigureAwait(false)) > 0) { classification.Observe(new string(buffer, 0, count)); if (diagnostic is not null && diagnostic.Length < 16384) diagnostic.Append(buffer, 0, Math.Min(count, 16384 - diagnostic.Length)); } }

    public static async Task ValidateManifestAsync(Uri url, CancellationToken token = default) =>
        _ = await InspectDurationAsync(url, token).ConfigureAwait(false);

    // Reuse the manifests already checked for protected/live content. No segment bodies are needed.
    internal static async Task<double?> InspectDurationAsync(Uri url, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        using var handler = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10, AutomaticDecompression = DecompressionMethods.All };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        var visited = new HashSet<string>();
        var durations = new List<double>();
        try { await Visit(url, 0).ConfigureAwait(false); }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new IOException("The media server did not finish sending its stream manifest in time. Try again or get a fresh link.", ex); }
        return durations.Count > 0 && durations.All(d => double.IsFinite(d) && d > 0 && Math.Abs(d - durations[0]) <= 1)
            ? durations[0] : null;
        async Task Visit(Uri current, int depth)
        {
            HttpDownloadEngine.ValidateUrl(current);
            if (!visited.Add(current.AbsoluteUri)) return;
            if (depth > 4 || visited.Count > 64) throw new IOException("This stream has too many nested playlists.");
            using var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new IOException($"The media server returned HTTP {(int)response.StatusCode}. Get a fresh link or try again later.");
            var final = response.RequestMessage!.RequestUri!;
            HttpDownloadEngine.ValidateUrl(final);
            var mime = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!IsManifestUrl(final.AbsoluteUri) && !mime.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) && !mime.Contains("dash+xml", StringComparison.OrdinalIgnoreCase)) return;
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var buffer = new byte[16384]; int count;
            while ((count = await body.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
            { if (bytes.Length + count > 2 * 1024 * 1024) throw new IOException("Stream manifest is too large."); bytes.Write(buffer, 0, count); }
            var text = Encoding.UTF8.GetString(bytes.ToArray());
            if (text.TrimStart().StartsWith("#EXTM3U", StringComparison.Ordinal))
            {
                var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
                if (lines.Any(l => (l.StartsWith("#EXT-X-KEY:", StringComparison.Ordinal) || l.StartsWith("#EXT-X-SESSION-KEY:", StringComparison.Ordinal)) &&
                    !l.Contains("METHOD=NONE", StringComparison.Ordinal))) throw new IOException("Encrypted or protected HLS is not supported. No keys or licenses are extracted.");
                var master = lines.Any(l => l.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal));
                if (!master && !lines.Contains("#EXT-X-ENDLIST")) throw new IOException("Live HLS recording is not supported. Choose an on-demand stream.");
                if (!master) {
                    double duration = 0; var valid = false;
                    foreach (var line in lines.Where(l => l.StartsWith("#EXTINF:", StringComparison.Ordinal))) {
                        var value = line[8..].Split(',')[0];
                        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds) || seconds <= 0) { duration = double.NaN; break; }
                        duration += seconds; valid = true;
                    }
                    durations.Add(valid ? duration : double.NaN);
                }
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
                    {
                        var next = lines.Skip(i + 1).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
                        if (next is not null) await Visit(new Uri(final, next), depth + 1).ConfigureAwait(false);
                    }
                    else if (lines[i].StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal) && HlsUri().Match(lines[i][13..]) is { Success: true } match)
                        await Visit(new Uri(final, match.Groups[1].Value), depth + 1).ConfigureAwait(false);
                }
            }
            else
            {
                using var xml = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
                var document = XDocument.Load(xml);
                if (document.Root?.Name.LocalName != "MPD") throw new IOException("Invalid stream manifest.");
                if (document.Descendants().Any(e => e.Name.LocalName == "ContentProtection")) throw new IOException("DRM/protected DASH is not supported. No keys or licenses are extracted.");
                if ((string?)document.Root.Attribute("type") == "dynamic") throw new IOException("Live DASH recording is not supported. Choose an on-demand stream.");
                if ((string?)document.Root.Attribute("mediaPresentationDuration") is string value) {
                    try { durations.Add(XmlConvert.ToTimeSpan(value).TotalSeconds); }
                    catch (Exception ex) when (ex is FormatException or OverflowException) { durations.Add(double.NaN); }
                }
            }
        }
    }
}
