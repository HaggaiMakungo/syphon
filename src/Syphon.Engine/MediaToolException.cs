namespace Syphon.Engine;

internal sealed class MediaToolException(string tool, int exitCode, string category, string explanation) : IOException(explanation)
{
    public string Diagnostic => $"Tool: {tool} · Exit code: {exitCode} · Category: {category}";
}

/// <summary>Inspect bounded transient output; retain only fixed explanations, never tool text or credentials.</summary>
internal sealed class MediaErrorClassifier
{
    private string tail = "";
    private (string Category, string Message, int Rank)? match;

    public void Observe(string chunk)
    {
        var text = (tail + chunk).ToLowerInvariant();
        tail = text.Length > 1024 ? text[^1024..] : text;
        bool Has(params string[] words) => words.Any(text.Contains);
        void Set(string category, string message, int rank)
        { if (match is null || rank >= match.Value.Rank) match = (category, message, rank); }
        if (Has("http error 500", "http error 502", "http error 503", "http error 504", "server returned 5"))
            Set("server", "The media server is temporarily unavailable. Resume to try again later.", 2);
        if (Has("timed out", "timeout", "connection reset", "connection refused", "unable to resolve", "name resolution", "getaddrinfo failed"))
            Set("network", "The media connection failed or timed out. Check your connection, then resume to try again.", 3);
        if (Has("certificate verify failed", "certificate verification failed"))
            Set("tls", "The media server certificate could not be verified. Check your system clock and the site's certificate.", 4);
        foreach (var code in new[] { 401, 403, 404, 410, 429 })
            if (Has($"http error {code}", $"http {code}", $"server returned {code}"))
                Set("http-" + code, code switch {
                    401 => "The media server returned HTTP 401 and requires permission. Check access on the original site.",
                    403 => "The media server returned HTTP 403 and refused access. Refresh the source link; if it persists, check access on the original site.",
                    404 or 410 => $"The media server returned HTTP {code}. This resource is no longer available at that link.",
                    _ => "The media server returned HTTP 429 (too many requests). Wait before trying again." }, 5);
        if(Has("unsupported url","no suitable extractor","unable to extract"))
            Set("unsupported-page","This page could not be resolved to a downloadable video. Use an individual video link or update the video extractor.",6);
        if (Has("requested format is not available", "no video formats found", "no formats found"))
            Set("format-unavailable", "The requested media quality is unavailable. Open New download, refresh the video menu and choose an available quality.", 6);
        if (Has("signature solving failed", "challenge solving failed", "n challenge", "no supported javascript runtime", "javascript runtime could not", "ejs challenge", "ejs scripts"))
            Set("extractor-runtime", "The video's player challenge could not be processed. Update yt-dlp and check the JavaScript runtime in Settings.", 7);
        if (Has("video unavailable", "video has been removed", "not available in your country", "copyright"))
            Set("unavailable", "This video is unavailable or restricted at the source. Check its availability on the original site.", 8);
        if (Has("sign in", "sign-in", "login required", "private video", "members-only", "premium-only", "confirm your age", "not a bot"))
            Set("account-required", "The source requires sign-in, account access or browser verification. Check the video on the original site. Syphon does not import browser sessions.", 9);
        if (Has("drm protected", "drm-protected", "protected by drm"))
            Set("protected-media", "This media uses protected playback, which Syphon does not support.", 10);
        if (Has("permission denied", "access is denied", "read-only file system"))
            Set("file-permission", "The media tool cannot write its output. Check the destination folder's permissions and available access.", 11);
        if (Has("no space left on device", "not enough space on the disk", "disk full"))
            Set("disk-full", "The destination disk is full. Free space before resuming the download.", 12);
    }

    public MediaToolException Failure(string tool, int exitCode) => new(tool, exitCode,
        match?.Category ?? "unclassified",
        match?.Message ?? $"{tool} stopped with exit code {exitCode}. No recognized error was reported. Check the source on its website and update the media tools before retrying.");
}
