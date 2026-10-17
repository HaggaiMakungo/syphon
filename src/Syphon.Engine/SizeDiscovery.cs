using System.Net;
using Syphon.Core;

namespace Syphon.Engine;

public sealed partial class DownloadCoordinator
{
    private sealed record DiscoveredSize(long Bytes, bool Confirmed);

    private async Task<DiscoveredSize?> DiscoverAndPublishSizeAsync(Guid id, string effective, string? etag, CancellationToken token)
    {
        var found = await DiscoverSizeAsync(id, effective, etag, token).ConfigureAwait(false);
        if (found is not null && !token.IsCancellationRequested) {
            if (found.Confirmed) Update(id, t => t.Total is null && t.Received <= found.Bytes && t.State is DownloadState.Starting or DownloadState.Downloading
                ? t with { Total = found.Bytes, EstimatedTotal = null, SizeEstimateSource = null } : t, persist: false);
            else SetSizeEstimate(id, found.Bytes, "Server size metadata; the source did not supply a matching strong file identity.");
        }
        return found;
    }

    // A headers-only, one-byte range request uses the existing redirect, session and origin limits.
    // No extra body is read, and optional metadata must never delay/fail the real transfer.
    private async Task<DiscoveredSize?> DiscoverSizeAsync(Guid id, string effective, string? etag, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try {
            using var response = await transport.GetResponseAsync(new(Get(id).Url), budget.Token, etag: etag, end: 0, context: RequestContext(id)).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri?.AbsoluteUri != effective || response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase))) return null;
            var tag = response.Headers.ETag;
            var strong = tag is { IsWeak: false } && tag.Tag != "*" ? tag.Tag : null;
            if (etag is not null && etag != strong) return null;
            long? bytes = null;
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode == HttpStatusCode.PartialContent && range is { Unit: "bytes", From: 0, To: 0, Length: > 0 } && response.Content.Headers.ContentLength is null or 1)
                bytes = range.Length;
            else if (response.StatusCode == HttpStatusCode.OK && range is null && response.Content.Headers.ContentLength is > 0)
                bytes = response.Content.Headers.ContentLength;
            return bytes is > 0 ? new(bytes.Value, etag is not null && etag == strong) : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or HttpRequestException or DownloadException or InvalidOperationException or FormatException) { return null; }
    }

    private void SetSizeEstimate(Guid id, long? bytes, string source)
    {
        if (bytes is not > 0) return;
        Update(id, t => t.Total is null && t.State == DownloadState.Downloading && bytes > t.Received
            ? t with { EstimatedTotal = bytes, SizeEstimateSource = source } : t, persist: false);
    }

    private async Task EstimateSelectedMediaAsync(StoredDownload task, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        try {
            var duration = await MediaTools.InspectDurationAsync(new(task.Url), budget.Token).ConfigureAwait(false);
            if (duration is > 0) { SetMediaDuration(task.Id, duration); return; }
            var info = await MediaTools.InspectAsync(new(task.Url), budget.Token, boundedProbe: true, fast: false).ConfigureAwait(false);
            SetMediaDuration(task.Id, info.Duration);
            var choice = info.Qualities.FirstOrDefault(q => q.VideoStream == task.VideoStream);
            if (task.MediaFormat == "mp4" && choice?.AudioStream == task.AudioStream) SetSizeEstimate(task.Id, choice?.EstimatedBytes, "Duration and selected stream bitrates; container overhead may differ.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or HttpRequestException or DownloadException or System.Text.Json.JsonException or System.Xml.XmlException or ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private void SetMediaDuration(Guid id, double? seconds)
    {
        if (seconds is not > 0 || !double.IsFinite(seconds.Value) || seconds > TimeSpan.MaxValue.TotalSeconds) return;
        Update(id, t => t.State == DownloadState.Downloading ? t with { MediaDurationSeconds = seconds } : t, persist: false);
    }

    internal static long? EstimateFromPlayback(double? duration, long bytes, double processedSeconds)
    {
        if (duration is not > 0 || !double.IsFinite(duration.Value) || bytes < 262144 || processedSeconds < 10 || !double.IsFinite(processedSeconds) || processedSeconds >= duration.Value) return null;
        var estimate = bytes * (duration.Value / processedSeconds);
        return double.IsFinite(estimate) && estimate > bytes && estimate < long.MaxValue ? (long)Math.Ceiling(estimate) : null;
    }
}
