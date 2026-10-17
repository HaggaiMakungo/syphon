using System.Diagnostics;
using System.Security.Cryptography;
using Syphon.Core;

namespace Syphon.Engine;

public sealed partial class DownloadCoordinator
{
    private async Task TransferMediaAsync(Guid id, CancellationToken token)
    {
        var task = Get(id);
        File.Delete(task.PartPath); // Only this task's owned scratch path. Media always restarts before a final digest exists.
        Update(id, t => t with { State = DownloadState.Downloading, Received = 0, Committed = 0, Total = null, EstimatedTotal = null, SizeEstimateSource = null, MediaDurationSeconds = null, Blocks = [],
            Connections = 0, BytesPerSecond = 0, Message = YoutubeTools.IsYoutubeVideoTarget(new(task.Url))
                ? "Checking the video before downloading…" : "Downloading and processing media…" });
        var progressGate = new object();
        long received = 0, previous = 0, lastChange = Stopwatch.GetTimestamp();
        long processedBytes = 0; double processedSeconds = 0, lastEstimatedSeconds = 0;
        Stopwatch? clock = null;
        void Progress(long bytes)
        {
            if (bytes < 0) return;
            lock (progressGate)
            {
                // yt-dlp restarts its byte counter when it switches from video to audio.
                var delta = bytes >= previous ? bytes - previous : bytes;
                previous = bytes;
                if (delta == 0) return;
                received += Math.Min(delta, long.MaxValue - received);
                clock ??= Stopwatch.StartNew(); lastChange = Stopwatch.GetTimestamp();
            }
        }
        using var reporting = CancellationTokenSource.CreateLinkedTokenSource(token);
        async Task ReportProgress()
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(reporting.Token).ConfigureAwait(false))
            {
                long count; double rate;
                lock (progressGate)
                {
                    count = received;
                    rate = clock is null || Stopwatch.GetElapsedTime(lastChange).TotalSeconds >= 2 ? 0
                        : count / Math.Max(.25, clock.Elapsed.TotalSeconds);
                }
                Update(id, t => t with { Received = count, BytesPerSecond = rate,
                    Message = count == 0 ? t.Message : rate > 0
                        ? "Downloading and processing media… Final size is available after processing."
                        : "Waiting for media data or finishing processing…" }, persist: false);
                long sampleBytes; double sampleSeconds;
                lock (progressGate) { sampleBytes = processedBytes; sampleSeconds = processedSeconds; }
                if (sampleSeconds >= lastEstimatedSeconds + 5) {
                    var estimateBytes = EstimateFromPlayback(Get(id).MediaDurationSeconds, sampleBytes, sampleSeconds);
                    if (estimateBytes is not null) { SetSizeEstimate(id, estimateBytes, "Full video duration × observed output bytes per second of video playback. Variable bitrate and final container overhead may differ."); lastEstimatedSeconds = sampleSeconds; }
                }
            }
        }
        var reporter = ReportProgress();
        var estimate = !YoutubeTools.IsYoutubeVideoTarget(new(task.Url)) && (task.VideoStream is not null || task.MediaFormat == "mp3")
            ? EstimateSelectedMediaAsync(task, reporting.Token) : Task.CompletedTask;
        try
        {
            if (YoutubeTools.IsYoutubeVideoTarget(new(task.Url)))
                await YoutubeTools.DownloadAsync(task, Progress, token, info => {
                    ApplyVideoTitle(id, info.Title);
                    SetMediaDuration(id, info.Duration);
                    if (task.MediaFormat == "mp4" && task.YoutubeFormat is not null)
                        SetSizeEstimate(id, info.Qualities.FirstOrDefault(q => q.YoutubeFormat == task.YoutubeFormat)?.EstimatedBytes,
                            "Selected video and audio metadata; final container size may differ.");
                    Update(id, t => t with { Message = "Downloading media… Final size is available after processing." }, persist: false);
                }).ConfigureAwait(false);
            else await MediaTools.ConvertAsync(task, Progress, token,
                bytes => SetSizeEstimate(id, bytes, "Duration and selected stream bitrates; final container size may differ."),
                (bytes, seconds) => { lock (progressGate) { if (seconds >= processedSeconds && bytes >= processedBytes) { processedBytes = bytes; processedSeconds = seconds; } } },
                info => SetMediaDuration(id, info.Duration)).ConfigureAwait(false);
        }
        finally
        {
            reporting.Cancel();
            await estimate.ConfigureAwait(false);
            try { await reporter.ConfigureAwait(false); } catch (OperationCanceledException) when (reporting.IsCancellationRequested) { }
            Update(id, t => t with { BytesPerSecond = 0 }, persist: false);
        }
        token.ThrowIfCancellationRequested();
        await using var file = new FileStream(task.PartPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.Asynchronous);
        if (file.Length == 0) throw new IOException("Media processing produced an empty file.");
        // File durability precedes metadata. Final media output uses the same verified publication journal as HTTP tasks.
        file.Flush(true);
        var blocks = new List<CheckpointBlock>(); var buffer = new byte[1024 * 1024]; long offset = 0;
        using var whole = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (offset < file.Length)
        {
            var length = (int)Math.Min(buffer.Length, file.Length - offset);
            await file.ReadExactlyAsync(buffer.AsMemory(0, length), token).ConfigureAwait(false);
            whole.AppendData(buffer, 0, length);
            blocks.Add(new(offset, length, Convert.ToHexStringLower(SHA256.HashData(buffer.AsSpan(0, length))))); offset += length;
        }
        Update(id, t => t with { Committed = offset, Received = offset, Total = offset, EstimatedTotal = null, SizeEstimateSource = null, Blocks = blocks.ToArray(), BytesPerSecond = 0,
            Sha256 = Convert.ToHexStringLower(whole.GetHashAndReset()), Message = "Media processing complete. Verifying output…" });
    }

    private void ApplyVideoTitle(Guid id, string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        lock (gate)
        {
            var task = tasks[id];
            // Only replace an automatically generated browser name. Explicit destinations stay authoritative.
            var placeholder = "youtube-" + YoutubeTools.VideoId(new(task.Url)) + (task.YoutubeFormat is null ? "" : "-" + task.YoutubeFormat) + "." + task.MediaFormat;
            var currentStem = Path.GetFileNameWithoutExtension(task.Filename);
            var placeholderStem = Path.GetFileNameWithoutExtension(placeholder);
            var isPlaceholder = task.Filename == placeholder || currentStem.StartsWith(placeholderStem + " (", StringComparison.Ordinal)
                && currentStem.EndsWith(')') && int.TryParse(currentStem.AsSpan(placeholderStem.Length + 2, currentStem.Length - placeholderStem.Length - 3), out var suffix) && suffix >= 2;
            if (task.BrowserRequestId is null || task.PreserveFilename || !isPlaceholder) return;
            var stem = SafeFilename(title); var extension = "." + task.MediaFormat;
            var folder = Path.GetDirectoryName(task.Destination)!;
            var destination = Path.Combine(folder, stem + extension); var number = 2;
            while (File.Exists(destination) || Directory.Exists(destination) || tasks.Values.Any(t => t.Id != id && string.Equals(t.Destination, destination, StringComparison.OrdinalIgnoreCase)))
            {
                if (number > 10000) throw new IOException("Too many files have that name. Choose a destination in Syphon.");
                destination = Path.Combine(folder, $"{stem} ({number++}){extension}");
            }
            Set(task with { Destination = destination });
        }
    }
}
