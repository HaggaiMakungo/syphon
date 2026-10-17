using Syphon.Core;
using Syphon.Engine;

namespace Syphon.Desktop;

public static class DownloadDisplay
{
    public static string? MediaDuration(StoredDownload task) {
        if (task.MediaDurationSeconds is not > 0 || !double.IsFinite(task.MediaDurationSeconds.Value) || task.MediaDurationSeconds > TimeSpan.MaxValue.TotalSeconds) return null;
        var seconds = (long)Math.Round(task.MediaDurationSeconds.Value);
        return seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60}m {seconds % 60}s" : $"{seconds / 60}m {seconds % 60}s";
    }
    public static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var index = 0;
        while (bytes >= 1024 && index < units.Length - 1) { bytes /= 1024; index++; }
        return $"{bytes:0.#} {units[index]}";
    }
    public static long? Estimate(StoredDownload task) => task.Total is null && task.EstimatedTotal is > 0 && task.EstimatedTotal > task.Received ? task.EstimatedTotal : null;
    public static string ExpectedSize(StoredDownload task) => task.Total is long total ? Bytes(total)
        : Estimate(task) is long estimate ? $"≈{Bytes(estimate)} · Estimated" : "Unknown";
    public static string Progress(StoredDownload task) => task.Total is long total
        ? $"{Bytes(task.Received)} / {Bytes(total)}" : Estimate(task) is long estimate ? $"{Bytes(task.Received)} received · ≈{Bytes(estimate)} estimated"
        : $"{Bytes(task.Received)} received · " + (task.Received == 0 && task.State is DownloadState.Queued or DownloadState.Starting or DownloadState.Downloading ? "Size not available yet" : "Size unknown");
    public static string Speed(StoredDownload task) => task.State == DownloadState.Downloading && task.BytesPerSecond > 0
        ? $"{Bytes(task.BytesPerSecond)}/s" : "—";
    public static string Remaining(StoredDownload task)
    {
        var isEstimate = task.Total is null;
        var total = task.Total ?? Estimate(task);
        if (task.State != DownloadState.Downloading || total is null || task.BytesPerSecond <= 0 || total <= task.Received) return "—";
        var seconds = (total.Value - task.Received) / task.BytesPerSecond;
        if (!double.IsFinite(seconds) || seconds > TimeSpan.MaxValue.TotalSeconds) return "—";
        var prefix = isEstimate ? "Approx. " : "About ";
        if (seconds < 60) return $"{prefix}{Math.Max(1, Math.Ceiling(seconds)):0}s";
        if (seconds < 3600) return $"{prefix}{Math.Ceiling(seconds / 60):0}m";
        return $"{prefix}{seconds / 3600:0.#}h";
    }
    public static string Status(StoredDownload task) => task.State switch
    {
        DownloadState.RetryWaiting => "Retrying soon", DownloadState.Starting => "Connecting",
        DownloadState.Pausing => "Saving progress", DownloadState.Publishing => "Saving file",
        DownloadState.Paused when task.RequiresBrowserContext && task.Message.StartsWith("Browser sign-in", StringComparison.Ordinal) => "Browser sign-in needed",
        _ => task.State.ToString()
    };
}
