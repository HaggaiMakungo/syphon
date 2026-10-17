namespace Syphon.Core;

public enum DownloadState
{
    Queued, Starting, Downloading, Pausing, Paused, RetryWaiting,
    Assembling, Publishing, Completed, Failed, Cancelled
}

public sealed record DownloadRequest(Uri Url, string DestinationPath)
{
    public Guid Id { get; init; } = Guid.NewGuid();
}

public sealed record DownloadProgress(
    Guid Id, DownloadState State, long BytesReceived, long? TotalBytes,
    TimeSpan Elapsed, double BytesPerSecond)
{
    public double? Fraction => TotalBytes is > 0 ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1) : null;
}

public sealed record DownloadResult(
    Guid Id, string DestinationPath, long Length, string Sha256, TimeSpan Elapsed);

public enum DownloadFailure
{
    InvalidRequest, AlreadyRunning, DestinationExists, HttpStatus, Network, Timeout,
    UnexpectedResponse, IncompleteResponse, FileSystem
}

public sealed class DownloadException(
    DownloadFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public DownloadFailure Failure { get; } = failure;
}

public interface IDownloadEngine
{
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request, IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
