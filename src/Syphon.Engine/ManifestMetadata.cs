using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Syphon.Engine;

public static partial class MediaTools
{
    internal static int DisplayTier(int height) => new[] { 144, 240, 360, 480, 720, 1080, 1440, 2160, 4320 }
        .MinBy(tier => Math.Abs((long)tier - height));

    // Metadata only: never fetch segments, keys, child playlists or remote XML resources.
    private static async Task<MediaInfo?> InspectManifestMetadataAsync(Uri url, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(4));
        using var client = new HttpClient(new HttpClientHandler { MaxAutomaticRedirections = 5 }) { Timeout = Timeout.InfiniteTimeSpan };
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(false);
        using var body = new MemoryStream(); var buffer = new byte[16384]; int read;
        while ((read = await input.ReadAsync(buffer, budget.Token).ConfigureAwait(false)) != 0) {
            if (body.Length + read > 2 * 1024 * 1024) throw new IOException("Stream manifest is too large.");
            body.Write(buffer, 0, read);
        }
        return ParseManifestMetadata(Encoding.UTF8.GetString(body.ToArray()));
    }

    internal static MediaInfo? ParseManifestMetadata(string text)
    {
        var captions = new List<MediaCaption>();
        var choices = new List<MediaChoice>(); double? duration = null; bool audio = false;
        if (text.TrimStart().StartsWith("#EXTM3U", StringComparison.Ordinal)) {
            var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
            if (lines.Any(l => (l.StartsWith("#EXT-X-KEY:") || l.StartsWith("#EXT-X-SESSION-KEY:")) && !l.Contains("METHOD=NONE")))
                throw new IOException("Encrypted or protected HLS is not supported.");
            foreach (var line in lines.Where(l => l.StartsWith("#EXT-X-MEDIA:") && (l.Contains("TYPE=SUBTITLES") || l.Contains("TYPE=CLOSED-CAPTIONS"))).Take(32)) {
                string Attr(string name) { var m = Regex.Match(line, "(?:[:,])" + name + "=\"([^\"]{0,120})\""); return m.Success ? m.Groups[1].Value : ""; }
                var label = Attr("NAME"); if (label.Length == 0) label = Attr("LANGUAGE");
                captions.Add(new(label.Length > 0 ? label : "Closed captions", line.Contains("TYPE=CLOSED-CAPTIONS") ? "Embedded closed captions — separate saving is not available yet" : "Streaming subtitle track — separate saving is not available yet"));
            }
            foreach (var line in lines.Where(l => l.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal)).Take(128)) {
                var size = Regex.Match(line, @"(?:^|,)RESOLUTION=(\d{1,5})x(\d{1,5})(?:,|$)");
                // The first attribute follows a colon rather than a comma.
                if (!size.Success) size = Regex.Match(line, @":RESOLUTION=(\d{1,5})x(\d{1,5})(?:,|$)");
                if (!size.Success || !int.TryParse(size.Groups[1].Value, out var width) || !int.TryParse(size.Groups[2].Value, out var height) || height <= 0) continue;
                var bandwidth = Regex.Match(line, @"(?:[:,])(?:AVERAGE-)?BANDWIDTH=(\d{1,12})(?:,|$)");
                long? rate = long.TryParse(bandwidth.Groups[1].Value, out var b) ? b : null;
                choices.Add(new(0, null, width, height, "", rate, QualityHeight: DisplayTier(height), ResolveAtDownload: true));
            }
            audio = lines.Any(l => l.StartsWith("#EXT-X-MEDIA:") && l.Contains("TYPE=AUDIO") || l.StartsWith("#EXT-X-STREAM-INF:") && Regex.IsMatch(l, "mp4a|opus|ac-3", RegexOptions.IgnoreCase));
        } else {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2097152 });
            var doc = XDocument.Load(reader);
            if (doc.Root?.Name.LocalName != "MPD") return null;
            if (doc.Descendants().Any(e => e.Name.LocalName == "ContentProtection")) throw new IOException("DRM/protected DASH is not supported.");
            if ((string?)doc.Root.Attribute("type") == "dynamic") throw new IOException("Live DASH recording is not supported.");
            if ((string?)doc.Root.Attribute("mediaPresentationDuration") is string value) {
                try { duration = XmlConvert.ToTimeSpan(value).TotalSeconds; } catch (FormatException) { }
            }
            foreach (var rep in doc.Descendants().Where(e => e.Name.LocalName == "Representation").Take(256)) {
                string? Attr(string key) => (string?)rep.Attribute(key) ?? (string?)rep.Parent?.Attribute(key);
                if (Attr("contentType") == "text" || Attr("mimeType")?.StartsWith("text/") == true || Attr("codecs") is string captionCodec && (captionCodec.Contains("stpp") || captionCodec.Contains("wvtt"))) {
                    captions.Add(new(Attr("lang") ?? "Closed captions", "Streaming subtitle track — separate saving is not available yet")); continue;
                }
                if (Attr("contentType") == "audio" || Attr("mimeType")?.StartsWith("audio/") == true) { audio = true; continue; }
                if (!int.TryParse(Attr("height"), out var height) || height <= 0 || height > 32768) continue;
                int.TryParse(Attr("width"), out var width); long? rate = long.TryParse(Attr("bandwidth"), NumberStyles.None, CultureInfo.InvariantCulture, out var b) && b > 0 ? b : null;
                choices.Add(new(0, null, width, height, Attr("codecs")?[..Math.Min(40, Attr("codecs")!.Length)] ?? "", rate, QualityHeight: DisplayTier(height), ResolveAtDownload: true));
            }
        }
        if (choices.Count == 0) return null;
        return new(choices.GroupBy(q => q.QualityHeight).Select(g => g.OrderByDescending(q => q.Bitrate).First()).OrderByDescending(q => q.QualityHeight).Take(16).ToArray(),
            audio, duration, "Qualities advertised by the player. Availability is checked when downloading.", Captions: captions.Select(c => c with { Label = c.Label.Length > 80 ? c.Label[..80] : c.Label }).Distinct().Take(16).ToArray());
    }
}
