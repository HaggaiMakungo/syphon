using Syphon.Core;

namespace Syphon.Engine;

public sealed record CheckpointBlock(long Offset, int Length, string Sha256);
public sealed record DownloadSegment(int Index, long Start, long Length, long Committed, CheckpointBlock[] Blocks);

/// <summary>Immutable task snapshot. Received is advisory; Committed and Blocks authorize reuse.</summary>
public sealed record StoredDownload
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Url { get; init; }
    public required string Destination { get; init; }
    public required string PartPath { get; init; }
    public DownloadState State { get; init; } = DownloadState.Queued;
    public long Received { get; init; }
    public long Committed { get; init; }
    public long? Total { get; init; }
    // Advisory only. Never use an estimate for ranges, checkpoints or publication validation.
    public long? EstimatedTotal { get; init; }
    public string? SizeEstimateSource { get; init; }
    public double? MediaDurationSeconds { get; init; }
    public string? ETag { get; init; }
    public string? EffectiveUrl { get; init; }
    public CheckpointBlock[] Blocks { get; init; } = [];
    public DownloadSegment[] Segments { get; init; } = [];
    public int Connections { get; init; }
    public bool SingleConnection { get; init; }
    public long SpeedLimitBytesPerSecond { get; init; }
    public bool RequiresBrowserContext { get; init; }
    public int Priority { get; init; }
    public long QueueOrder { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public double ActiveSeconds { get; init; }
    public bool HasTiming { get; init; }
    public double? AverageBytesPerSecond => State == DownloadState.Completed && HasTiming && ActiveSeconds > 0 ? Received / ActiveSeconds : null;
    public Guid? BrowserRequestId { get; init; }
    public string? MediaFormat { get; init; }
    public int? VideoStream { get; init; }
    public int? QualityHeight { get; init; }
    public int? AudioStream { get; init; }
    public string? YoutubeFormat { get; init; }
    public bool PreserveFilename { get; init; }
    public string? Sha256 { get; init; }
    public bool PublicationPending { get; init; }
    public string? ReplacementTarget { get; init; }
    public string? ReplacementBackup { get; init; }
    public string? ReplacementExpectedSha256 { get; init; }
    public Guid? ReplacementOldTaskId { get; init; }
    public string Message { get; init; } = "Ready to download.";
    public bool RestartRequired { get; init; }
    public double BytesPerSecond { get; init; }
    public int RetryCount { get; init; }
    public DateTimeOffset? RetryAt { get; init; }
    public string? Diagnostic { get; init; }
    public string Filename => Path.GetFileName(Destination);
    public string ProgressText => Total is long n ? $"{Received:N0} / {n:N0} bytes" : $"{Received:N0} bytes";
}
