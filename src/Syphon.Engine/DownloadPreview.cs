namespace Syphon.Engine;

public sealed record DownloadPreview(string Filename, long? Bytes, string Message);

public static class DownloadPreviewReader
{
    // Optional headers only. Never download a body or resolve video qualities to enable queueing.
    public static async Task<DownloadPreview> ReadAsync(Uri url, CancellationToken token = default)
    {
        HttpDownloadEngine.ValidateUrl(url);
        var name = DownloadCoordinator.SuggestedBrowserFilename(url, MediaTools.IsManifestUrl(url.AbsoluteUri) ? "mp4" : null);
        if (PageVideoTools.IsKnownVideoPage(url) || MediaTools.IsManifestUrl(url.AbsoluteUri))
            return new(name, null, "Media details will be resolved when downloading.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token); budget.CancelAfter(TimeSpan.FromSeconds(3));
        try {
            using var client = new HttpClient(new HttpClientHandler { MaxAutomaticRedirections = 5 }) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(name, null, "Preview unavailable. You can still queue this link.");
            var suggested = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            if (!string.IsNullOrWhiteSpace(suggested)) name = DownloadCoordinator.SafeFilename(suggested);
            return new(name, response.Content.Headers.ContentEncoding.Count == 0 ? response.Content.Headers.ContentLength : null,
                "Server preview; actual filename, size and resume support are checked when downloading.");
        } catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException) {
            token.ThrowIfCancellationRequested();
            return new(name, null, "Preview unavailable. You can still queue this link.");
        }
    }
}
