using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace Syphon.Engine;

internal static class DownloadSafety
{
    internal static long RequiredSpace(long total, long committed, bool assembly) => checked(Math.Max(0, total - committed) + (assembly ? total : 0));
    internal static void EnsureSpace(string path, long bytes, Func<string, long>? available = null)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;
        long free;
        try { free = available?.Invoke(root) ?? new DriveInfo(root).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Trace.TraceWarning("Unable to inspect destination free space: {0}", ex.GetType().Name); return; }
        if (free < bytes) throw new IOException($"Not enough disk space. Free up at least {bytes - free:N0} more bytes on the destination drive, then Resume. Saved download parts have been retained.");
    }
    internal static void Mark(string path, string url, string? referrer = null)
    {
        if (!OperatingSystem.IsWindows()) return;
        string SafeUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? new UriBuilder(uri) { UserName = "", Password = "", Fragment = "" }.Uri.AbsoluteUri : "";
        try { File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=" + SafeUrl(url) + "\r\n" + (referrer is null ? "" : "ReferrerUrl=" + SafeUrl(referrer) + "\r\n"), new UTF8Encoding(false)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { Trace.TraceWarning("Mark-of-the-Web could not be written: {0}", ex.GetType().Name); }
    }
}
internal sealed class AdaptiveConnections(int maximum)
{
    private double previous;
    private bool plateau;
    internal int Limit { get; private set; } = 1;
    internal int Sample(double rate)
    {
        if (!plateau && rate > 0 && (previous == 0 || rate > previous * 1.1)) { previous = rate; Limit = Math.Min(maximum, Limit + 1); }
        else plateau = true;
        return Limit;
    }
    internal void BackOff() { Limit = Math.Max(1, Limit / 2); plateau = true; }
}
internal static class MediaTags
{
    internal static string Clean(string? text) => string.Concat((text ?? "").EnumerateRunes().Where(r => System.Text.Rune.GetUnicodeCategory(r) is not (System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format)).Take(512).Select(r => r.ToString())).Trim();
    internal static Dictionary<string, string> FromJson(JsonElement root)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in new[] { ("title", "title"), ("artist", "artist"), ("album", "album"), ("track_number", "track"), ("release_year", "date"), ("track", "track"), ("date", "date"), ("year", "date") })
            if (root.TryGetProperty(pair.Item1, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number && Clean(value.ToString()) is { Length: > 0 } text) result[pair.Item2] = text;
        return result;
    }
    internal static async Task ApplyAsync(string path, IReadOnlyDictionary<string, string>? tags, CancellationToken token)
    {


        var scratch = path + ".tags.mp3";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            if (tags is null || tags.Count == 0)
            {
                var json = await MediaTools.RunAsync("ffprobe", ["-v", "error", "-i", path, "-show_entries", "format_tags", "-of", "json"], null, budget.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("format", out var format) || !format.TryGetProperty("tags", out var source)) return;
                tags = FromJson(source);
                if (tags.Count == 0) return;
            }
            File.Delete(scratch); // This task owns the scratch name, including leftovers after a crash.
            var args = new List<string> { "-nostdin", "-hide_banner", "-n", "-i", path, "-map", "0", "-map_metadata", "-1", "-c", "copy", "-id3v2_version", "3" };
            foreach (var tag in tags) args.AddRange(["-metadata", tag.Key + "=" + Clean(tag.Value)]);
            args.Add(scratch);
            await MediaTools.RunAsync("ffmpeg", args, null, budget.Token).ConfigureAwait(false);
            if (!File.Exists(scratch) || new FileInfo(scratch).Length == 0) throw new IOException("Optional tagging produced no output.");
            File.Move(scratch, path, true);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { Trace.TraceWarning("Optional audio tags timed out."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException) { Trace.TraceWarning("Optional audio tags unavailable: {0}", ex.GetType().Name); }
        finally { try { File.Delete(scratch); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}




