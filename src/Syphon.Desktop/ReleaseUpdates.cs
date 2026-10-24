using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;

namespace Syphon.Desktop;

public sealed record ReleaseNotice(string Tag, string Url, string Notes);

public static class ReleaseUpdates
{
    public static string CurrentVersion => typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] is { } value && value != "1.0.0" ? value : "0.1.0-beta.17";
    private static readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(12), MaxResponseContentBufferSize = 1024 * 1024 };
    public static async Task<ReleaseNotice?> CheckAsync(string? skipped, CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/HaggaiMakungo/syphon/releases?per_page=20");
        request.Headers.UserAgent.ParseAdd("Syphon/" + CurrentVersion);
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        return SelectRelease(await response.Content.ReadAsStringAsync(token), CurrentVersion, skipped);
    }

    public static ReleaseNotice? SelectRelease(string json, string current, string? skipped)
    {
        if (!TryVersion(current, out var installed)) return null;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
        var eligible = new List<(ReleaseNotice Notice, (Version Core, int Beta) Version)>();
        foreach (var release in document.RootElement.EnumerateArray()) {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (!release.TryGetProperty("tag_name", out var tagValue) || !release.TryGetProperty("html_url", out var urlValue)) continue;
            var tag = tagValue.GetString(); var address = urlValue.GetString();
            if (tag == skipped || !TryVersion(tag, out var candidate) || Compare(candidate, installed) <= 0) continue;
            if (installed.Beta == int.MaxValue && candidate.Beta != int.MaxValue) continue;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var url) || url.Scheme != "https" || url.Host != "github.com" ||
                !string.IsNullOrEmpty(url.UserInfo) || url.Port != 443 || !url.AbsolutePath.StartsWith("/HaggaiMakungo/syphon/releases/", StringComparison.OrdinalIgnoreCase)) continue;
            var notes = release.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : "";
            eligible.Add((new(tag!, url.AbsoluteUri, notes[..Math.Min(notes.Length, 24000)]), candidate));
        }
        return eligible.OrderByDescending(x => x.Version.Core).ThenByDescending(x => x.Version.Beta).Select(x => x.Notice).FirstOrDefault();
    }
    private static int Compare((Version Core, int Beta) a, (Version Core, int Beta) b) => a.Core.CompareTo(b.Core) is var core && core != 0 ? core : a.Beta.CompareTo(b.Beta);
    private static bool TryVersion(string? text, out (Version Core, int Beta) result)
    {
        result = (new Version(0, 0), 0);
        var pieces = (text ?? "").TrimStart('v').Split('-', 2);
        if (!Version.TryParse(pieces[0], out var core)) return false;
        var beta = int.MaxValue;
        if (pieces.Length > 1 && (!pieces[1].StartsWith("beta.", StringComparison.Ordinal) || !int.TryParse(pieces[1][5..], out beta) || beta < 0)) return false;
        result = (core, beta); return true;
    }
}
