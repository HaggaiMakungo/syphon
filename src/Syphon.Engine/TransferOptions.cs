namespace Syphon.Engine;

public sealed record TransferOptions
{
    public bool MarkOfTheWebEnabled { get; init; } = true;
    public bool MediaMetadataEnabled { get; init; } = true;
    public bool AdaptiveConnectionsEnabled { get; init; } = true;
    public bool DiskSpaceProtectionEnabled { get; init; } = true;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan HeaderTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan BodyIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public int MaximumRedirects { get; init; } = 10;
    public int MaximumConcurrentDownloads { get; init; } = 2;
    public int MaximumConnectionsPerFile { get; init; } = 4;
    public int MaximumConnectionsPerOrigin { get; init; } = 4;
    public long SegmentationThreshold { get; init; } = 16 * 1024 * 1024;
    public int MaximumRetries { get; init; } = 5;
    public long GlobalBytesPerSecond { get; init; }
    public QueueSchedule? Schedule { get; init; }
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RetryMaximumDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaximumServerRetryDelay { get; init; } = TimeSpan.FromMinutes(5);

    public void Validate()
    {
        BandwidthLimiter.ValidateRate(GlobalBytesPerSecond); Schedule?.Validate();
        if (ConnectTimeout <= TimeSpan.Zero || HeaderTimeout <= TimeSpan.Zero || BodyIdleTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(HeaderTimeout), "Timeouts must be positive.");
        if (MaximumRedirects is < 0 or > 20)
            throw new ArgumentOutOfRangeException(nameof(MaximumRedirects));
        if (MaximumConnectionsPerFile is < 1 or > 4 || MaximumConnectionsPerOrigin is < 1 or > 8 || SegmentationThreshold < 65536)
            throw new ArgumentOutOfRangeException(nameof(MaximumConnectionsPerFile));
        if (MaximumConcurrentDownloads is < 1 or > 8 || MaximumRetries is < 0 or > 10 ||
            RetryBaseDelay <= TimeSpan.Zero || RetryMaximumDelay < RetryBaseDelay ||
            RetryMaximumDelay > TimeSpan.FromMinutes(5) || MaximumServerRetryDelay <= TimeSpan.Zero ||
            MaximumServerRetryDelay > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(MaximumConcurrentDownloads), "Choose valid queue and retry limits.");
    }
}
