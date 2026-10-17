using System.Net;
using System.Security.Cryptography;
using Syphon.Core;

namespace Syphon.Engine;

public sealed partial class DownloadCoordinator
{
    private readonly CancellationTokenSource linkShutdown = new();
    private void KeepContext(Guid id, DownloadRequestContext context) => browserContexts[id] = context with { Cookies = context.Cookies?.ToArray() };
    private DownloadRequestContext? RequestContext(Guid id) { lock (gate) return browserContexts.GetValueOrDefault(id); }

    /// <summary>Explicit renewal only. Every saved block must match the new resource before reuse.</summary>
    public async Task ReplaceLinkAsync(Guid id, Uri url, CancellationToken token = default)
    {
        HttpDownloadEngine.ValidateUrl(url);
        if (url.AbsoluteUri.Length > 8192 || YoutubeTools.IsYoutubeVideoTarget(url)) throw new ArgumentException("Use a fresh direct file link.");
        StoredDownload saved; TransferOptions limits; DownloadRequestContext? context;
        CancellationTokenSource cancellation;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            saved = tasks[id];
            if (saved.MediaFormat is not null || saved.State == DownloadState.Completed || workers.ContainsKey(id) || removing.Contains(id))
                throw new InvalidOperationException("Pause an unfinished ordinary file download before updating its link.");
            if (saved.RestartRequired) throw new InvalidOperationException("This partial download needs a fresh restart before its link can be changed.");
            if (!updatingLinks.Add(id)) throw new InvalidOperationException("This download's new link is already being checked.");
            limits = pendingOptions ?? options; context = browserContexts.GetValueOrDefault(id);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, linkShutdown.Token);
        }
        using (cancellation)
        using (var probeTransport = new HttpDownloadEngine(limits))
        {
            try
            {
                var ct = cancellation.Token;
                if (context is not null && !DownloadRequestContext.SameOrigin(new(context.Url), url)) context = null;
                var verified = await RecoverAsync(saved).ConfigureAwait(false);
                if (verified.RestartRequired) throw new InvalidDataException("The saved partial data needs a fresh restart.");
                var hasData = verified.Committed > 0;
                if (hasData && (verified.ETag is null || verified.Total is null)) throw new InvalidDataException("The saved file has no strong identity. Restart it using a fresh link.");
                using var probe = await probeTransport.GetResponseAsync(url, ct, 0, hasData ? verified.ETag : null, hasData ? 0 : null, context).ConfigureAwait(false);
                var effective = probe.RequestMessage!.RequestUri!;
                if (hasData)
                {
                    ValidateRenewal(probe, verified, 0, 0, effective);
                    await CheckRange(probe, [], 1, ct).ConfigureAwait(false);
                }
                else if (probe.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("The new link could not be opened. Check that it is a direct download link and that you are signed in.");
                // Release the probe's origin slot before checking saved ranges.
                probe.Dispose();
                var spans = verified.Segments.Length > 0
                    ? verified.Segments.Where(s => s.Committed > 0).Select(s => (s.Start, s.Committed, s.Blocks)).ToArray()
                    : hasData ? [(0L, verified.Committed, verified.Blocks)] : [];
                foreach (var (start, length, blocks) in spans)
                {
                    using var response = await probeTransport.GetResponseAsync(url, ct, start, verified.ETag, start + length - 1, context).ConfigureAwait(false);
                    ValidateRenewal(response, verified, start, start + length - 1, effective);
                    await CheckRange(response, blocks, length, ct).ConfigureAwait(false);
                }
                lock (gate)
                {
                    ObjectDisposedException.ThrowIf(closing, this); ct.ThrowIfCancellationRequested();
                    if (!hasData) {
                        if (File.Exists(saved.PartPath)) File.Delete(saved.PartPath);
                        foreach (var segment in saved.Segments) { var path = SegmentPath(saved, segment.Index); if (File.Exists(path)) File.Delete(path); }
                    }
                    if (tasks[id].State != saved.State) throw new InvalidOperationException("The download changed while checking the link. Try again when it is paused.");
                    Set(verified with { Url = url.AbsoluteUri, EstimatedTotal = null, SizeEstimateSource = null, MediaDurationSeconds = null, Priority = tasks[id].Priority, QueueOrder = tasks[id].QueueOrder,
                        EffectiveUrl = hasData ? effective.AbsoluteUri : null,
                        ETag = hasData ? verified.ETag : null, Total = hasData ? verified.Total : null,
                        Segments = hasData ? verified.Segments : [], Blocks = hasData ? verified.Blocks : [],
                        State = DownloadState.Paused, FinishedAt = null, RetryAt = null, Diagnostic = null,
                        RequiresBrowserContext = context is not null,
                        Message = hasData ? "New link checked against saved data. Resume when ready." : "Download link updated. Resume when ready." });
                    if (context is null) browserContexts.Remove(id);
                }

                async Task CheckRange(HttpResponseMessage response, CheckpointBlock[] blocks, long length, CancellationToken ct)
                {
                    await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var buffer = new byte[65536]; long covered = 0;
                    foreach (var block in blocks)
                    {
                        if (block.Offset != covered || block.Length <= 0 || block.Length > 2097152) throw new InvalidDataException("The saved range manifest is invalid.");
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var remaining = block.Length;
                        while (remaining > 0)
                        {
                            var n = Math.Min(remaining, buffer.Length);
                            var read = await bandwidth.ReadAsync(input, buffer.AsMemory(0, n), id, limits.BodyIdleTimeout, ct).ConfigureAwait(false);
                            if (read == 0) throw new EndOfStreamException();
                            hash.AppendData(buffer, 0, read); remaining -= read;
                        }
                        if (Convert.ToHexStringLower(hash.GetHashAndReset()) != block.Sha256)
                            throw new InvalidDataException("The new link points to different file data. Saved progress was kept; choose another link or restart fresh.");
                        covered += block.Length;
                    }
                    // The one-byte probe has no saved block; still verify its exact framing.
                    if (blocks.Length == 0 && length == 1) { if (await bandwidth.ReadAsync(input, buffer.AsMemory(0, 1), id, limits.BodyIdleTimeout, ct).ConfigureAwait(false) != 1) throw new EndOfStreamException(); covered = 1; }
                    if (covered != length) throw new InvalidDataException("The saved range manifest is incomplete.");
                    if (await bandwidth.ReadAsync(input, buffer.AsMemory(0, 1), id, limits.BodyIdleTimeout, ct).ConfigureAwait(false) != 0) throw new InvalidDataException("The new link returned an oversized range.");
                }
            }
            finally { lock (gate) updatingLinks.Remove(id); }
        }
    }

    private static void ValidateRenewal(HttpResponseMessage response, StoredDownload saved, long start, long end, Uri effective)
    {
        var range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent || response.Headers.ETag is not { IsWeak: false } tag || tag.Tag != saved.ETag ||
            range is null || range.Unit != "bytes" || range.From != start || range.To != end || range.Length != saved.Total ||
            response.Content.Headers.ContentLength != end - start + 1 || response.RequestMessage?.RequestUri != effective ||
            response.Content.Headers.ContentEncoding.Any(c => !c.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The new link does not identify the same resumable file. Saved progress was kept; choose another link or restart fresh.");
    }
}
