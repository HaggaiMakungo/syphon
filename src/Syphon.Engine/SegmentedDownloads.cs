using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Syphon.Core;

namespace Syphon.Engine;

public sealed partial class DownloadCoordinator
{
    internal static string SegmentPath(StoredDownload task, int index) => $"{task.PartPath}.{index}";
    private static void DeleteSegmentFiles(StoredDownload task)
    { foreach (var segment in task.Segments) File.Delete(SegmentPath(task, segment.Index)); }

    private void CheckSegmentStatus(HttpResponseMessage response)
    {
        var code = (int)response.StatusCode;
        if (code is 408 or 429 or 500 or 502 or 503 or 504)
        {
            var retry = response.Headers.RetryAfter;
            var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow);
            if (delay > options.MaximumServerRetryDelay) throw new IOException("The server asked for a longer wait than automatic retries allow. Resume later.");
            throw new TransientException($"HTTP {code}", delay);
        }
        if (code >= 400 && code != 416) throw new StatusException(code);
    }

    private static void ValidateSegmentResponse(HttpResponseMessage response, StoredDownload task, long start, long end)
    {
        var range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent || range is null || range.Unit != "bytes" ||
            range.From != start || range.To != end || range.Length != task.Total ||
            response.Content.Headers.ContentLength != end - start + 1 ||
            response.Headers.ETag is not { IsWeak: false } tag || tag.Tag != task.ETag ||
            response.RequestMessage?.RequestUri?.AbsoluteUri != task.EffectiveUrl ||
            response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new RestartException("The server changed the file or returned incompatible ranges. Restart fresh using one connection.");
    }

    private async Task<bool> PrepareSegmentsAsync(Guid id, CancellationToken token)
    {
        var task = Get(id);
        using var probe = await transport.GetResponseAsync(new(task.EffectiveUrl!), token, 0, task.ETag, 0, RequestContext(id)).ConfigureAwait(false);
        CheckSegmentStatus(probe);
        if (probe.StatusCode == HttpStatusCode.OK) return false;
        ValidateSegmentResponse(probe, task, 0, 0);
        await using var input = await probe.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
        idle.CancelAfter(options.BodyIdleTimeout);
        var one = new byte[1];
        try
        {
            if (await bandwidth.ReadAsync(input, one, id, options.BodyIdleTimeout, token).ConfigureAwait(false) != 1) throw new EndOfStreamException();
            if (await bandwidth.ReadAsync(input, one, id, options.BodyIdleTimeout, token).ConfigureAwait(false) != 0)
                throw new RestartException("The server returned an oversized range. Restart required.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TransientException("The server stopped sending the range probe."); }
        catch (IOException ex) when (ex is not RestartException) { throw new TransientException("The range probe ended unexpectedly."); }
        var length = task.Total!.Value;
        var count = options.MaximumConnectionsPerFile;
        var segments = Enumerable.Range(0, count).Select(index =>
        {
            // Division before multiplication keeps offsets valid for 64-bit representations.
            var start = length / count * index + Math.Min(index, length % count);
            var size = length / count + (index < length % count ? 1 : 0);
            return new DownloadSegment(index, start, size, 0, []);
        }).ToArray();
        Update(id, t => t with { Segments = segments, Blocks = [], Committed = 0, Received = 0 });
        Boundary?.Invoke("SegmentsPlanned", Get(id));
        return true;
    }

    private async Task TransferSegmentsAsync(Guid id, CancellationToken token)
    {
        var task = Get(id);
        using var siblings = CancellationTokenSource.CreateLinkedTokenSource(token);
        var live = new ConcurrentDictionary<int, long>(task.Segments.Select(s => new KeyValuePair<int, long>(s.Index, s.Committed)));
        var startedAt = task.Committed;
        var clock = Stopwatch.StartNew();
        var errors = new object();
        Exception? failure = null;
        int connections = 0;
        using var perFile = new SemaphoreSlim(options.MaximumConnectionsPerFile);
        Update(id, t => t with { State = DownloadState.Downloading, Message = $"Downloading with up to {task.Segments.Length} connections…", Connections = 0 });

        async Task DownloadPart(DownloadSegment segment)
        {
            if (segment.Committed == segment.Length) return;
            var admitted = false;
            try
            {
                await perFile.WaitAsync(siblings.Token).ConfigureAwait(false);
                admitted = true;
                var start = segment.Start + segment.Committed;
                var end = segment.Start + segment.Length - 1;
                using var response = await transport.GetResponseAsync(new(task.EffectiveUrl!), siblings.Token, start, task.ETag, end, RequestContext(id)).ConfigureAwait(false);
                CheckSegmentStatus(response);
                ValidateSegmentResponse(response, task, start, end);
                Interlocked.Increment(ref connections);
                Update(id, t => t with { Connections = Volatile.Read(ref connections) }, false);
                try
                {
                    await using var file = new FileStream(SegmentPath(task, segment.Index), FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (file.Length != segment.Committed) throw new RestartException("A download part changed. Restart required.");
                    file.Position = segment.Committed;
                    var blocks = segment.Blocks.ToList();
                    using var blockHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long received = segment.Committed;
                    long blockStart = received;
                    int blockLength = 0;
                    var lastCheckpoint = clock.Elapsed;
                    var lastProgress = clock.Elapsed;

                    void Checkpoint()
                    {
                        Boundary?.Invoke("SegmentBeforeFlush", Get(id));
                        file.Flush(true);
                        Boundary?.Invoke("SegmentAfterFlush", Get(id));
                        if (blockLength > 0)
                        {
                            blocks.Add(new(blockStart, blockLength, Convert.ToHexStringLower(blockHash.GetHashAndReset())));
                            blockStart = received; blockLength = 0;
                        }
                        var committed = segment with { Committed = received, Blocks = blocks.ToArray() };
                        Update(id, t =>
                        {
                            var updated = t.Segments.Select(s => s.Index == segment.Index ? committed : s).ToArray();
                            return t with { Segments = updated, Committed = updated.Sum(s => s.Committed) };
                        });
                        Boundary?.Invoke("SegmentAfterCommit", Get(id));
                        lastCheckpoint = clock.Elapsed;
                    }

                    try
                    {
                        await using var input = await response.Content.ReadAsStreamAsync(siblings.Token).ConfigureAwait(false);
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(siblings.Token);
                        var buffer = new byte[65536];
                        while (true)
                        {
                            int count;
                            try { count = await bandwidth.ReadAsync(input, buffer, id, options.BodyIdleTimeout, siblings.Token).ConfigureAwait(false); }
                            catch (OperationCanceledException) when (!siblings.IsCancellationRequested)
                            { throw new TransientException("A download connection stopped sending data."); }
                            catch (IOException) { throw new TransientException("A download connection ended unexpectedly."); }
                            finally { idle.CancelAfter(Timeout.InfiniteTimeSpan); }
                            if (count == 0) break;
                            if (count > segment.Length - received) throw new RestartException("A range exceeded its reserved space. Restart required.");
                            await file.WriteAsync(buffer.AsMemory(0, count), CancellationToken.None).ConfigureAwait(false);
                            blockHash.AppendData(buffer, 0, count);
                            received += count; blockLength += count; live[segment.Index] = received;
                            if (blockLength >= 1048576 || clock.Elapsed - lastCheckpoint >= TimeSpan.FromSeconds(2)) Checkpoint();
                            if (clock.Elapsed - lastProgress >= TimeSpan.FromMilliseconds(100))
                            {
                                var bytes = live.Values.Sum();
                                Update(id, t => t with { Received = bytes, Connections = Volatile.Read(ref connections),
                                    BytesPerSecond = (bytes - startedAt) / Math.Max(.001, clock.Elapsed.TotalSeconds) }, false);
                                lastProgress = clock.Elapsed;
                            }
                            siblings.Token.ThrowIfCancellationRequested();
                        }
                        if (received != segment.Length) throw new TransientException("A range ended before all its bytes arrived.");
                    }
                    finally { Checkpoint(); }
                }
                finally { Interlocked.Decrement(ref connections); }
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException)
                    lock (errors) { if (failure is null || ex is RestartException) failure = ex; }
                siblings.Cancel();
                throw;
            }
            finally { if (admitted) perFile.Release(); }
        }

        try { await Task.WhenAll(task.Segments.Select(DownloadPart)).ConfigureAwait(false); }
        catch
        {
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
        token.ThrowIfCancellationRequested();
        Update(id, t => t with { Received = t.Committed, Connections = 0 });
    }

    private async Task AssembleSegmentsAsync(Guid id, CancellationToken token)
    {
        var task = Get(id);
        ValidateCoverage(task);
        if (task.Segments.Any(s => s.Committed != s.Length)) throw new RestartException("Download coverage is incomplete. Restart required.");
        // Reverify every committed digest before assembling; length alone is insufficient.
        var checkedTask = await RecoverSegmentsAsync(task).ConfigureAwait(false);
        if (checkedTask.RestartRequired) throw new RestartException(checkedTask.Message);
        Update(id, t => t with { State = DownloadState.Assembling, Connections = 0, Message = "Verifying and joining download parts…" });
        Boundary?.Invoke("BeforeAssembly", Get(id));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var output = new FileStream(task.PartPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536];
            foreach (var segment in task.Segments)
            {
                await using var input = File.OpenRead(SegmentPath(task, segment.Index));
                foreach (var block in segment.Blocks)
                {
                    using var blockHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var remaining = block.Length;
                    while (remaining > 0)
                    {
                        var count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), token).ConfigureAwait(false);
                        if (count == 0) throw new RestartException("A part is incomplete. Restart required.");
                        await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                        hash.AppendData(buffer, 0, count); blockHash.AppendData(buffer, 0, count); remaining -= count;
                        Boundary?.Invoke("DuringAssembly", Get(id));
                    }
                    if (Convert.ToHexStringLower(blockHash.GetHashAndReset()) != block.Sha256)
                        throw new RestartException("A part changed during assembly. Restart required.");
                }
                if (await input.ReadAsync(buffer.AsMemory(0, 1), token).ConfigureAwait(false) != 0)
                    throw new RestartException("A part exceeded its reserved size. Restart required.");
            }
            await output.FlushAsync(token).ConfigureAwait(false); output.Flush(true);
        }
        Boundary?.Invoke("AssemblyFlushed", Get(id));
        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        Update(id, t => t with { Sha256 = digest, Received = t.Committed });
    }

    private static void ValidateCoverage(StoredDownload task)
    {
        if (task.Total is null || task.ETag is null || task.EffectiveUrl is null || task.Segments.Length is < 2 or > 4)
            throw new InvalidDataException("The download range manifest is invalid. Restart required.");
        long end = 0;
        for (var i = 0; i < task.Segments.Length; i++)
        {
            var part = task.Segments[i];
            if (part.Index != i || part.Start != end || part.Length <= 0 || part.Length > task.Total - end ||
                part.Committed < 0 || part.Committed > part.Length)
                throw new InvalidDataException("Download ranges overlap or have gaps. Restart required.");
            end += part.Length;
        }
        if (end != task.Total || task.Segments.Sum(s => s.Committed) != task.Committed)
            throw new InvalidDataException("Download coverage is inconsistent. Restart required.");
    }

    private static async Task<StoredDownload> RecoverSegmentsAsync(StoredDownload task)
    {
        try
        {
            ValidateCoverage(task);
            foreach (var segment in task.Segments)
            {
                var path = SegmentPath(task, segment.Index);
                if (!File.Exists(path))
                {
                    if (segment.Committed != 0) throw new InvalidDataException("A committed download part is missing. Restart required.");
                    continue;
                }
                await using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 65536, true);
                if (file.Length < segment.Committed) throw new InvalidDataException("A download part is shorter than its checkpoint. Restart required.");
                long position = 0;
                var buffer = new byte[65536];
                foreach (var block in segment.Blocks)
                {
                    if (block.Offset != position || block.Length <= 0 || block.Length > 2097152 || block.Length > segment.Committed - position)
                        throw new InvalidDataException("A part checkpoint is invalid. Restart required.");
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var remaining = block.Length;
                    while (remaining > 0)
                    {
                        var count = await file.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining))).ConfigureAwait(false);
                        if (count == 0) throw new InvalidDataException("A download part is incomplete. Restart required.");
                        hash.AppendData(buffer, 0, count); remaining -= count;
                    }
                    if (Convert.ToHexStringLower(hash.GetHashAndReset()) != block.Sha256)
                        throw new InvalidDataException("A download part was modified. Restart required.");
                    position += block.Length;
                }
                if (position != segment.Committed) throw new InvalidDataException("A part checkpoint has missing coverage. Restart required.");
                if (file.Length != position) { file.SetLength(position); file.Flush(true); }
            }
            // A crashed assembly is disposable: committed segment files remain the source of truth.
            if (task.Sha256 is not null && !await MatchesAsync(task.PartPath, task.Total!.Value, task.Sha256).ConfigureAwait(false))
                task = task with { Sha256 = null };
            return task with { State = task.State is DownloadState.Failed or DownloadState.Cancelled ? task.State : DownloadState.Paused,
                Connections = 0, Received = task.Committed, BytesPerSecond = 0, RetryAt = null,
                Message = task.RestartRequired ? task.Message : "Download parts verified. Resume when ready." };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { return task with { State = DownloadState.Failed, RestartRequired = true, Connections = 0, Message = ex.Message }; }
    }
}
