using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Syphon.Core;
using Microsoft.Data.Sqlite;

namespace Syphon.Engine;

/// <summary>Owns task transitions, single-worker admission, durable checkpoints and publication.</summary>
public sealed partial class DownloadCoordinator : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly DownloadStore store;
    private readonly BandwidthLimiter bandwidth = new();
    private readonly TimeProvider timeProvider;
    private HttpDownloadEngine transport;
    private TransferOptions options;
    private TransferOptions? pendingOptions;
    private bool queueSuspended;
    private readonly Dictionary<Guid, long> timing = new();
    private readonly Dictionary<Guid, StoredDownload> tasks = new();
    private readonly Dictionary<Guid, DownloadRequestContext> browserContexts = new();
    private readonly HashSet<Guid> updatingLinks = [];
    private readonly Dictionary<Guid, Worker> workers = new();
    private readonly List<(Guid Id, Worker Worker)> pending = new();
    private readonly HashSet<Guid> activeSlots = [];
    private readonly HashSet<Guid> removing = [];
    private bool closing;
    // Test-only interruption seam at the actual persistence/publication boundaries.
    internal Action<string, StoredDownload>? Boundary { get; set; }
    internal Action<StoredDownload>? BeforePersist { get; set; }
    private sealed class Worker
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Completion { get; set; } = Task.CompletedTask;
        public bool Cancelled { get; set; }
        public TaskCompletionSource Permit { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private DownloadCoordinator(string directory, TransferOptions? options, TimeProvider? timeProvider)
    {
        this.options = options ?? new();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        bandwidth.SetGlobal(this.options.GlobalBytesPerSecond);
        transport = new(this.options);
        try { store = new(directory); }
        catch { transport.Dispose(); throw; }
    }

    public static async Task<DownloadCoordinator> OpenAsync(string directory, TransferOptions? options = null, TimeProvider? timeProvider = null)
    {
        DownloadCoordinator coordinator;
        for (var attempt = 0; ; attempt++)
        {
            try { coordinator = new DownloadCoordinator(directory, options, timeProvider); break; }
            catch (SqliteException ex) when (attempt < 5 && ex.SqliteErrorCode is 5 or 6 or 10)
            {
                // Reopening immediately after a killed process can race native WAL/SHM cleanup.
                // Reopen intact files with a short bound; never delete journals or retry corrupt schemas.
                await Task.Delay(50 * (1 << attempt)).ConfigureAwait(false);
            }
            catch (IOException ex) when (attempt < 5 && (ex.HResult & 0xFFFF) is 32 or 33)
            {
                // Windows can briefly retain a terminated owner's handle during cleanup.
                // A live owner remains protected by FileShare.None throughout these attempts.
                await Task.Delay(50 * (1 << attempt)).ConfigureAwait(false);
            }
        }
        try
        {
            long position = 0;
            foreach (var saved in coordinator.store.Load())
            {
                var task = await coordinator.RecoverAsync(saved).ConfigureAwait(false);
                if (task.QueueOrder <= 0) task = task with { QueueOrder = ++position };
                position = Math.Max(position, task.QueueOrder);
                if (task.State is DownloadState.Queued or DownloadState.RetryWaiting)
                    task = task with { State = DownloadState.Paused, RetryAt = null, Message = "Paused after reopening. Resume when ready." };
                if (task.RequiresBrowserContext && task.State != DownloadState.Completed && !task.RestartRequired)
                    task = task with { Message = "Browser sign-in is needed again. Send the same link from your browser to refresh it, or update the download link." };
                coordinator.store.Save(task);
                coordinator.tasks.Add(task.Id, task);
                coordinator.bandwidth.SetTask(task.Id, task.SpeedLimitBytesPerSecond);
            }
            await coordinator.RefreshScheduleAsync().ConfigureAwait(false);
            coordinator.scheduleLoop = coordinator.RunScheduleAsync();
            return coordinator;
        }
        catch { coordinator.store.Dispose(); coordinator.transport.Dispose(); coordinator.bandwidth.Dispose(); throw; }
    }

    public StoredDownload[] GetTasks() { lock (gate) return tasks.Values.OrderByDescending(t => t.Priority).ThenBy(t => t.QueueOrder).ToArray(); }
    public StoredDownload Get(Guid id) { lock (gate) return tasks[id]; }

    public StoredDownload Add(Uri url, string destination, bool singleConnection = false)
    {
        HttpDownloadEngine.ValidateUrl(url);
        if (PageVideoTools.IsKnownVideoPage(url)) { url = PageVideoTools.CanonicalUrl(url); destination = Path.ChangeExtension(destination, ".mp4"); }
        destination = Path.GetFullPath(destination);
        var folder = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(Path.GetFileName(destination)))
            throw new ArgumentException("Choose a destination filename.");
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (tasks.Values.Any(t => t.State != DownloadState.Completed &&
                string.Equals(t.Destination, destination, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A task already uses this destination. Select it to resume or choose a different filename.");
            var id = Guid.NewGuid();
            var task = new StoredDownload
            {
                Id = id, Url = url.AbsoluteUri, Destination = destination, SingleConnection = singleConnection,
                MediaFormat = PageVideoTools.IsKnownVideoPage(url) ? "mp4" : null,
                PartPath = Path.Combine(folder, $".{Path.GetFileName(destination)}.{id:N}.syphon-part")
            };
            Set(task);
            return tasks[id];
        }
    }

    public void Resume(Guid id)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            var task = tasks[id];
            if (workers.ContainsKey(id) || removing.Contains(id) || task.State == DownloadState.Completed) return;
            if (task.RestartRequired) throw new InvalidOperationException(task.Message);
            if (updatingLinks.Contains(id)) throw new InvalidOperationException("This download's new link is being checked.");
            if (task.RequiresBrowserContext && !browserContexts.ContainsKey(id)) throw new InvalidOperationException("Send this link from your browser again to refresh sign-in, or use Update download link.");
            var worker = new Worker();
            Set(task with { State = DownloadState.Queued, Message = ScheduleAllowsAdmission() ? "Waiting for a download slot…" : "Waiting for the scheduled start. Keep Syphon open.", BytesPerSecond = 0,
                RetryAt = null, RetryCount = 0, Diagnostic = null, FinishedAt = null });
            workers.Add(id, worker);
            worker.Completion = Task.Run(() => RunAsync(id, worker));
            pending.Add((id, worker));
            PumpQueue();
        }
    }

    public async Task PauseAsync(Guid id, bool cancel = false)
    {
        Task? completion = null;
        lock (gate)
        {
            if (workers.TryGetValue(id, out var worker))
            {
                worker.Cancelled = cancel;
                // Cancellation can run a queued worker's continuation inline under this reentrant gate.
                // Publish Pausing first so it cannot replace that worker's terminal Paused state.
                Set(tasks[id] with { State = DownloadState.Pausing, RetryAt = null, Message = "Saving progress…" });
                worker.Cancellation.Cancel();
                completion = worker.Completion;
            }
            else if (tasks[id].State != DownloadState.Completed)
                Set(tasks[id] with { State = cancel ? DownloadState.Cancelled : DownloadState.Paused,
                    RetryAt = null, Message = cancel ? "Cancelled. Partial data retained." : "Paused." });
        }
        if (completion is not null) await completion.ConfigureAwait(false);
    }

    public Task WaitForIdleAsync(Guid id)
    { lock (gate) return workers.TryGetValue(id, out var worker) ? worker.Completion : Task.CompletedTask; }

    /// <summary>Explicit user action that discards only this task's unfinished data.</summary>
    public void Restart(Guid id, bool? singleConnection = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (removing.Contains(id)) throw new InvalidOperationException("This task is being removed.");
            if (updatingLinks.Contains(id)) throw new InvalidOperationException("Wait for the download link check to finish.");
            if (workers.ContainsKey(id)) throw new InvalidOperationException("Pause the download before restarting it.");
            var task = tasks[id];
            if (task.State == DownloadState.Completed) throw new InvalidOperationException("This download is already complete.");
            File.Delete(task.PartPath);
            DeleteSegmentFiles(task);
            Set(task with { Committed = 0, Received = 0, Total = null, EstimatedTotal = null, SizeEstimateSource = null, MediaDurationSeconds = null, Blocks = [], ETag = null,
                EffectiveUrl = null, Sha256 = null, Segments = [], Connections = 0, SingleConnection = singleConnection ?? task.SingleConnection,
                PublicationPending = false, RestartRequired = false, ActiveSeconds = 0, StartedAt = null, FinishedAt = null, HasTiming = true,
                State = DownloadState.Queued, Message = "Ready to restart." });
        }
        Resume(id);
    }

    public async Task RemoveAsync(Guid id, bool deletePartial = false)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (updatingLinks.Contains(id)) throw new InvalidOperationException("Wait for the download link check to finish.");
            if (!tasks.ContainsKey(id) || !removing.Add(id)) return;
        }
        try
        {
            await PauseAsync(id).ConfigureAwait(false);
            lock (gate)
            {
                // Only the task-owned unfinished file can be deleted; never its final destination.
                if (deletePartial) { File.Delete(tasks[id].PartPath); DeleteSegmentFiles(tasks[id]); YoutubeTools.Cleanup(tasks[id]); }
                store.Remove(id);
                tasks.Remove(id);
                browserContexts.Remove(id);
                bandwidth.SetTask(id, 0);
            }
        }
        finally { lock (gate) removing.Remove(id); }
    }

    // Called under gate. Retry timers and queued tasks do not consume transfer slots.
    private void PumpQueue()
    {
        if (queueSuspended) return;
        if (pendingOptions is not null)
        {
            if (activeSlots.Count > 0) return;
            var replacement = new HttpDownloadEngine(pendingOptions);
            transport.Dispose(); transport = replacement; options = pendingOptions; pendingOptions = null;
        }
        if (!ScheduleAllowsAdmission()) return;
        while (!closing && activeSlots.Count < options.MaximumConcurrentDownloads && pending.Count > 0)
        {
            var entry = pending.OrderByDescending(e => tasks.GetValueOrDefault(e.Id)?.Priority ?? 0)
                .ThenBy(e => tasks.GetValueOrDefault(e.Id)?.QueueOrder ?? long.MaxValue).First();
            pending.Remove(entry);
            if (!workers.TryGetValue(entry.Id, out var worker) || !ReferenceEquals(worker, entry.Worker) ||
                worker.Cancellation.IsCancellationRequested || activeSlots.Contains(entry.Id)) continue;
            try { Set(tasks[entry.Id] with { State = DownloadState.Starting, RetryAt = null, Message = "Connecting…" }); }
            catch (Exception ex) { worker.Permit.TrySetException(ex); continue; }
            activeSlots.Add(entry.Id);
            worker.Permit.TrySetResult();
        }
    }

    // The gate protects both the connection and all task snapshots. No observer code runs here.
    private void Set(StoredDownload task)
    {
        var now = Stopwatch.GetTimestamp();
        if (tasks.TryGetValue(task.Id, out var prior))
        {
            // Use a monotonic clock; paused time, queue time and retry waits are excluded.
            if (timing.TryGetValue(task.Id, out var tick))
                task = task with { ActiveSeconds = prior.ActiveSeconds + Stopwatch.GetElapsedTime(tick, now).TotalSeconds };
            if (task.State != prior.State && task.State is DownloadState.Completed or DownloadState.Failed or DownloadState.Cancelled)
                task = task with { FinishedAt = DateTimeOffset.UtcNow };
            if (task.State is DownloadState.Starting && task.StartedAt is null)
                task = task with { StartedAt = DateTimeOffset.UtcNow, HasTiming = prior.CreatedAt is not null && prior.Committed == 0 };
        }
        else task = task with { CreatedAt = DateTimeOffset.UtcNow, QueueOrder = NextQueueOrder(), HasTiming = true };
        BeforePersist?.Invoke(task); store.Save(task); tasks[task.Id] = task;
        if (task.State is DownloadState.Starting or DownloadState.Downloading or DownloadState.Assembling or DownloadState.Publishing or DownloadState.Pausing)
            timing[task.Id] = now;
        else timing.Remove(task.Id);
    }
    private void Update(Guid id, Func<StoredDownload, StoredDownload> change, bool persist = true)
    {
        lock (gate)
        {
            var next = change(tasks[id]);
            if (persist) Set(next); else tasks[id] = next;
        }
    }

    private async Task RunAsync(Guid id, Worker worker)
    {
        var token = worker.Cancellation.Token;
        try
        {
            while (true)
            {
                await worker.Permit.Task.WaitAsync(token).ConfigureAwait(false);
                try { await AttemptAsync(id, token).ConfigureAwait(false); break; }
                catch (Exception ex) when (IsTransient(ex) && !token.IsCancellationRequested)
                {
                    var snapshot = Get(id);
                    if (snapshot.Committed > 0 && snapshot.Sha256 is null && (snapshot.ETag is null || snapshot.Total is null))
                        throw new RestartException("The connection was interrupted and this server cannot safely resume. Restart required.");
                    if (snapshot.RetryCount >= options.MaximumRetries) throw;
                    var attempt = snapshot.RetryCount + 1;
                    var delay = RetryDelay(attempt, ex is TransientException transient ? transient.RetryAfter : null);
                    Update(id, t => t with { State = DownloadState.RetryWaiting, RetryCount = attempt,
                        RetryAt = DateTimeOffset.UtcNow + delay, BytesPerSecond = 0, Received = t.Committed,
                        Message = $"Connection interrupted or server busy. Retry {attempt} of {options.MaximumRetries} scheduled.",
                        Diagnostic = ErrorDetail(ex) });
                    lock (gate) { activeSlots.Remove(id); PumpQueue(); }
                    await Task.Delay(delay, token).ConfigureAwait(false);
                    lock (gate)
                    {
                        token.ThrowIfCancellationRequested();
                        worker.Permit = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        Set(tasks[id] with { State = DownloadState.Queued, RetryAt = null, QueueOrder = NextQueueOrder(), Message = "Waiting for a download slot…" });
                        pending.Add((id, worker));
                        PumpQueue();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            try
            {
                Update(id, t => t with { State = worker.Cancelled ? DownloadState.Cancelled : DownloadState.Paused,
                    Received = t.Committed, BytesPerSecond = 0, RetryAt = null, Connections = 0,
                    Message = t.MediaFormat is not null ? (worker.Cancelled ? "Media job cancelled." : "Media job paused. Resume starts processing from the beginning.") :
                        worker.Cancelled ? "Cancelled. Partial data retained." : "Paused. Progress saved." });
            }
            catch (Exception ex) { RecordFailure(id, ex); }
        }
        catch (Exception ex)
        {
            RecordFailure(id, ex);
        }
        finally
        {
            lock (gate)
            {
                activeSlots.Remove(id); workers.Remove(id); worker.Cancellation.Dispose(); PumpQueue();
            }
        }
    }

    private void RecordFailure(Guid id, Exception ex)
    {
        lock (gate)
        {
            var prior = tasks[id];
            var failed = prior with { State = DownloadState.Failed, Received = prior.Committed, RetryAt = null, Connections = 0,
                BytesPerSecond = 0, RestartRequired = prior.RestartRequired || ex is RestartException,
                Diagnostic = ErrorDetail(ex), Message = FriendlyError(ex) };
            try { Set(failed); }
            catch (Exception saveError)
            {
                // Keep the last persisted checkpoint authoritative when storage itself is failing.
                tasks[id] = failed with { Message = "Progress could not be saved. Check free space and folder access before resuming.", Diagnostic = saveError.Message };
                timing.Remove(id);
            }
        }
    }

    private async Task AttemptAsync(Guid id, CancellationToken token)
    {
            var task = await RecoverAsync(Get(id)).ConfigureAwait(false);
            if (task.State == DownloadState.Completed) { Update(id, _ => task); return; }
            Update(id, _ => task with { State = DownloadState.Starting, Message = "Connecting…" });
            token.ThrowIfCancellationRequested();
            if (task.RestartRequired) throw new RestartException(task.Message);
            if (File.Exists(task.Destination) || Directory.Exists(task.Destination))
                throw new IOException("The destination already exists. Choose a different filename.");
            Directory.CreateDirectory(Path.GetDirectoryName(task.PartPath)!);
            if (options.DiskSpaceProtectionEnabled && task.Sha256 is null && task.Total is long known) DownloadSafety.EnsureSpace(task.PartPath, DownloadSafety.RequiredSpace(known, task.Committed, task.Segments.Length > 0));
            if (task.Sha256 is null)
            {
                if (task.MediaFormat is not null) await TransferMediaAsync(id, token).ConfigureAwait(false);
                else if (task.Segments.Length > 0)
                {
                    await TransferSegmentsAsync(id, token).ConfigureAwait(false);
                    await AssembleSegmentsAsync(id, token).ConfigureAwait(false);
                }
                else if (task.Committed > 0 && task.Committed == task.Total)
                {
                    await using var complete = File.OpenRead(task.PartPath);
                    var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(complete, token).ConfigureAwait(false));
                    Update(id, t => t with { Sha256 = hash });
                }
                else await TransferAsync(id, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            await PublishAsync(id).ConfigureAwait(false);
    }

    internal TimeSpan RetryDelay(int attempt, TimeSpan? retryAfter)
    {
        var exponential = Math.Min(options.RetryMaximumDelay.TotalMilliseconds,
            options.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        var milliseconds = Math.Min(options.RetryMaximumDelay.TotalMilliseconds, exponential * (.8 + Random.Shared.NextDouble() * .4));
        if (retryAfter is TimeSpan serverDelay)
            milliseconds = Math.Max(milliseconds, Math.Clamp(serverDelay.TotalMilliseconds, 0, options.MaximumServerRetryDelay.TotalMilliseconds));
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static bool IsTransient(Exception ex) => ex is TransientException ||
        ex is DownloadException { Failure: DownloadFailure.Timeout } ||
        ex is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.ResponseEnded };

    private static string ErrorDetail(Exception ex) => ex switch
    {
        MediaToolException media => media.Diagnostic,
        HttpRequestException request => $"Network: {request.HttpRequestError}",
        StatusException status => $"HTTP {status.StatusCode}",
        TransientException transient => transient.Message,
        _ => ex.Message
    };

    private static string FriendlyError(Exception ex) => ex switch
    {
        StatusException { StatusCode: 401 or 403 } => "This link needs permission or may have expired. Get a fresh download link.",
        StatusException { StatusCode: 404 or 410 } => "This file is no longer available at that link. Check the link or get a new one.",
        StatusException => "The server refused this download. Check the link and try again.",
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "A secure connection could not be verified. Check your clock and the website’s certificate.",
        HttpRequestException or TransientException => "The connection is still unavailable after retrying. Resume to try again.",
        DownloadException { Failure: DownloadFailure.Timeout } => "The server is taking too long to respond. Resume to try again.",
        UnauthorizedAccessException => "Syphon cannot write to this folder. Choose a folder you can save to.",
        _ => ex.Message
    };

    private async Task TransferAsync(Guid id, CancellationToken token, bool allowSegmentation = true)
    {
        var task = Get(id);
        var offset = task.Committed;
        if (offset > 0 && (task.Total is null || task.ETag is null))
            throw new RestartException("This server did not provide a strong file identity and size. Restart required.");
        using var response = await transport.GetResponseAsync(new(task.Url), token, offset, task.ETag, context: RequestContext(id)).ConfigureAwait(false);
        if (task.RequiresBrowserContext && response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml" &&
            !string.Equals(response.Content.Headers.ContentDisposition?.DispositionType, "attachment", StringComparison.OrdinalIgnoreCase) &&
            Path.GetExtension(task.Destination).ToLowerInvariant() is not (".html" or ".htm" or ".xhtml"))
            throw new IOException("The site returned a web page instead of the file. Open the site in your browser, then send a fresh direct download link.");
        var code = (int)response.StatusCode;
        if (code is 408 or 429 or 500 or 502 or 503 or 504)
        {
            var retryAfter = response.Headers.RetryAfter;
            var serverDelay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow);
            if (serverDelay > options.MaximumServerRetryDelay)
                throw new IOException("The server asked for a longer wait than automatic retries allow. Resume later.");
            throw new TransientException($"HTTP {code}", serverDelay);
        }
        if (code >= 400 && code != 416) throw new StatusException(code);
        var tag = response.Headers.ETag;
        var etag = tag is { IsWeak: false } && tag.Tag != "*" ? tag.Tag : null;
        var effective = response.RequestMessage!.RequestUri!.AbsoluteUri;
        if (response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
            throw new RestartException("The server changed the content encoding. Restart required.");
        long? total;
        if (offset > 0)
        {
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestedRangeNotSatisfiable)
                throw new RestartException("The file changed or the server cannot continue this download. Restart required.");
            if (response.StatusCode != HttpStatusCode.PartialContent)
                throw new IOException($"The server returned HTTP {(int)response.StatusCode}. Resume to try again.");
            if (etag != task.ETag || effective != task.EffectiveUrl || range is null || range.Unit != "bytes" ||
                range.From != offset || range.Length != task.Total || range.To != task.Total - 1 ||
                response.Content.Headers.ContentLength != task.Total - offset)
                throw new RestartException("The server returned incompatible file data. Restart required.");
            total = task.Total;
        }
        else
        {
            if (response.StatusCode != HttpStatusCode.OK)
                throw new IOException($"The server returned HTTP {(int)response.StatusCode}.");
            total = response.Content.Headers.ContentLength;
            if (options.DiskSpaceProtectionEnabled && total is long knownSize) DownloadSafety.EnsureSpace(task.PartPath, DownloadSafety.RequiredSpace(knownSize, offset, allowSegmentation && !task.SingleConnection && options.MaximumConnectionsPerFile > 1 && knownSize >= options.SegmentationThreshold && etag is not null && response.Headers.AcceptRanges.Contains("bytes")));
            Update(id, t => t with { Total = total, EstimatedTotal = null, SizeEstimateSource = null, ETag = etag, EffectiveUrl = effective });
            if (allowSegmentation && !task.SingleConnection && options.MaximumConnectionsPerFile > 1 && total >= options.SegmentationThreshold &&
                etag is not null && response.Headers.AcceptRanges.Contains("bytes"))
            {
                response.Dispose();
                if (await PrepareSegmentsAsync(id, token).ConfigureAwait(false))
                {
                    await TransferSegmentsAsync(id, token).ConfigureAwait(false);
                    await AssembleSegmentsAsync(id, token).ConfigureAwait(false);
                }
                else await TransferAsync(id, token, allowSegmentation: false).ConfigureAwait(false);
                return;
            }
        }

        await using var output = new FileStream(task.PartPath, offset == 0 ? FileMode.OpenOrCreate : FileMode.Open,
            FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (output.Length != offset) throw new RestartException("The partial file changed. Restart required.");
        output.Position = offset;
        var blocks = task.Blocks.ToList();
        using var blockHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = offset;
        long blockStart = offset;
        int blockLength = 0;
        var clock = Stopwatch.StartNew();
        var lastCheckpoint = clock.Elapsed;
        var lastProgress = clock.Elapsed;
        using var sizeDiscoveryStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var sizeDiscovery = total is null ? DiscoverAndPublishSizeAsync(id, effective, etag, sizeDiscoveryStop.Token) : Task.FromResult<DiscoveredSize?>(null);
        var discoveryApplied = false;
        void ApplyDiscoveredSize() {
            total ??= Get(id).Total;
            if (discoveryApplied || !sizeDiscovery.IsCompletedSuccessfully) return;
            discoveryApplied = true;
            if (sizeDiscovery.Result is not { } found || found.Bytes < received) return;
            if (found.Confirmed) { total = found.Bytes; Update(id, t => t with { Total = found.Bytes, EstimatedTotal = null, SizeEstimateSource = null }); }
            else SetSizeEstimate(id, found.Bytes, "Server size metadata; the source did not supply a matching strong file identity.");
        }

        void Checkpoint()
        {
            // Never commit metadata before data reaches stable storage.
            Boundary?.Invoke("BeforeFlush", Get(id));
            output.Flush(true);
            Boundary?.Invoke("AfterFlush", Get(id));
            if (blockLength > 0)
            {
                blocks.Add(new(blockStart, blockLength, Convert.ToHexStringLower(blockHash.GetHashAndReset())));
                blockStart = received;
                blockLength = 0;
            }
            Update(id, t => t with { Committed = received, Received = received, Blocks = blocks.ToArray() });
            Boundary?.Invoke("AfterCommit", Get(id));
            lastCheckpoint = clock.Elapsed;
        }

        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            var buffer = new byte[65536];
            Update(id, t => t with { State = DownloadState.Downloading, Connections = 1, Message = "Downloading…" });
            while (true)
            {
                int count;
                try { count = await bandwidth.ReadAsync(input, buffer, id, options.BodyIdleTimeout, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                { throw new TransientException("The server stopped sending data."); }
                catch (IOException) { throw new TransientException("The network stream ended unexpectedly."); }
                finally { idle.CancelAfter(Timeout.InfiniteTimeSpan); }
                ApplyDiscoveredSize();
                if (count == 0) break;
                if (total is long size && count > size - received)
                    throw new RestartException("The response exceeded the expected size. Restart required.");
                // Complete an accepted write before checkpointing or acknowledging Pause.
                await output.WriteAsync(buffer.AsMemory(0, count), CancellationToken.None).ConfigureAwait(false);
                blockHash.AppendData(buffer, 0, count);
                received = checked(received + count);
                blockLength += count;
                if (blockLength >= 1024 * 1024 || clock.Elapsed - lastCheckpoint >= TimeSpan.FromSeconds(2)) Checkpoint();
                if (clock.Elapsed - lastProgress >= TimeSpan.FromMilliseconds(100))
                {
                    Update(id, t => t with { Received = received,
                        BytesPerSecond = (received - offset) / Math.Max(.001, clock.Elapsed.TotalSeconds) }, false);
                    lastProgress = clock.Elapsed;
                }
                token.ThrowIfCancellationRequested();
            }
            if (total is long expected && received != expected)
                throw new TransientException("The transfer ended before the expected size arrived.");
        }
        finally { sizeDiscoveryStop.Cancel(); await sizeDiscovery.ConfigureAwait(false); Checkpoint(); }
        if (total is null && Get(id).Total is long discoveredTotal && received != discoveredTotal)
            throw new TransientException("The transfer ended before the discovered file size arrived.");
        token.ThrowIfCancellationRequested();
        output.Position = 0;
        var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(output, token).ConfigureAwait(false));
        Update(id, t => t with { Sha256 = digest, Total = received, EstimatedTotal = null, SizeEstimateSource = null });
    }

    private async Task PublishAsync(Guid id)
    {
        var task = Get(id);
        if (!await MatchesAsync(task.PartPath, task.Committed, task.Sha256!).ConfigureAwait(false))
            throw new RestartException("The completed partial file changed. Restart required.");
        // Journal survives a crash between rename and the Completed commit.
        Update(id, t => t with { State = DownloadState.Publishing, PublicationPending = true, Message = "Saving your completed file…" });
        Boundary?.Invoke("BeforeRename", Get(id));
        File.Move(task.PartPath, task.Destination, overwrite: false);
        if (options.MarkOfTheWebEnabled) DownloadSafety.Mark(task.Destination, task.Url, RequestContext(id)?.Referrer);
        Boundary?.Invoke("AfterRename", Get(id));
        Update(id, t => t with { State = DownloadState.Completed, PublicationPending = false, Received = t.Committed,
            BytesPerSecond = 0, Connections = 0, Diagnostic = null, Message = "Download complete." });
        // The journal now points at a verified final file; leftover segments are cleanup-only.
        try { DeleteSegmentFiles(task); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static async Task<bool> MatchesAsync(string path, long length, string hash)
    {
        if (!File.Exists(path)) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        return file.Length == length && Convert.ToHexStringLower(await SHA256.HashDataAsync(file).ConfigureAwait(false)) == hash;
    }

    private async Task<StoredDownload> RecoverAsync(StoredDownload task)
    {
        if (task.State == DownloadState.Completed)
        {
            try { DeleteSegmentFiles(task); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return task with { Connections = 0 };
        }
        try
        {
            if (task.PublicationPending && task.Sha256 is string digest &&
                await MatchesAsync(task.Destination, task.Committed, digest).ConfigureAwait(false))
            {
                if (options.MarkOfTheWebEnabled) DownloadSafety.Mark(task.Destination, task.Url);
                try { DeleteSegmentFiles(task); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return task with { State = DownloadState.Completed, PublicationPending = false, Connections = 0, Received = task.Committed, Message = "Download complete." };
            }
            if (task.Segments.Length > 0) return await RecoverSegmentsAsync(task).ConfigureAwait(false);
            if (PageVideoTools.RequiresExtractor(task)) YoutubeTools.Cleanup(task);
            if (!File.Exists(task.PartPath))
            {
                if (task.Committed > 0 || task.Sha256 is not null) throw new InvalidDataException("The partial file is missing. Restart required.");
            }
            else
            {
                await using var file = new FileStream(task.PartPath, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None, 65536, FileOptions.Asynchronous);
                if (file.Length < task.Committed) throw new InvalidDataException("The partial file is shorter than its checkpoint. Restart required.");
                var buffer = new byte[65536];
                long position = 0;
                foreach (var block in task.Blocks)
                {
                    if (block.Offset != position || block.Length <= 0 || block.Length > 2 * 1024 * 1024)
                        throw new InvalidDataException("The checkpoint is invalid. Restart required.");
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var remaining = block.Length;
                    while (remaining > 0)
                    {
                        var count = await file.ReadAsync(buffer.AsMemory(0, Math.Min(remaining, buffer.Length))).ConfigureAwait(false);
                        if (count == 0) throw new InvalidDataException("The partial file is incomplete. Restart required.");
                        hash.AppendData(buffer, 0, count); remaining -= count;
                    }
                    if (Convert.ToHexStringLower(hash.GetHashAndReset()) != block.Sha256)
                        throw new InvalidDataException("The partial file was modified. Restart required.");
                    position += block.Length;
                }
                if (position != task.Committed) throw new InvalidDataException("The checkpoint coverage is invalid. Restart required.");
                if (file.Length != task.Committed) { file.SetLength(task.Committed); file.Flush(true); }
            }
            if (task.Committed > 0 && task.Sha256 is null && (task.ETag is null || task.Total is null))
                return task with { RestartRequired = true, State = DownloadState.Paused, Received = task.Committed,
                    Message = "This server did not provide a strong file identity and size. Restart required." };
            return task with { Received = task.Committed, BytesPerSecond = 0, Connections = 0,
                State = task.State is DownloadState.Queued or DownloadState.Cancelled or DownloadState.Failed ? task.State : DownloadState.Paused,
                RetryAt = null, Message = task.RestartRequired || task.State is DownloadState.Failed or DownloadState.Cancelled
                    ? task.Message : "Progress verified. Resume when ready." };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return task with { State = DownloadState.Failed, Received = task.Committed,
                BytesPerSecond = 0, RestartRequired = true, Message = ex.Message };
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (gate)
        {
            if (closing) return;
            closing = true;
            scheduleShutdown.Cancel();
            linkShutdown.Cancel();
            foreach (var worker in workers.Values) worker.Cancellation.Cancel();
            pending = workers.Values.Select(w => w.Completion).ToArray();
        }
        try { await Task.WhenAll(pending.Append(scheduleLoop ?? Task.CompletedTask)).ConfigureAwait(false); }
        finally { store.Dispose(); transport.Dispose(); bandwidth.Dispose(); }
    }

    private sealed class RestartException(string message) : IOException(message);
    private sealed class TransientException(string message, TimeSpan? retryAfter = null) : IOException(message)
    { public TimeSpan? RetryAfter { get; } = retryAfter; }
    private sealed class StatusException(int code) : IOException($"HTTP {code}")
    { public int StatusCode { get; } = code; }
}
