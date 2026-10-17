using System.Security.Cryptography;
using Syphon.Core;

namespace Syphon.Engine;

public sealed record FileVerificationResult(string Sha256, long Length, bool? MatchesRecorded, bool? MatchesExpected);

/// <summary>Reads the completed destination without changing its saved download record.</summary>
public static class DownloadFileVerifier
{
    public static async Task<FileVerificationResult> VerifyAsync(StoredDownload task, string? expectedSha256 = null,
        CancellationToken cancellationToken = default)
    {
        if (task.State != DownloadState.Completed)
            throw new InvalidOperationException("Only completed downloads can be checked.");
        var expected = expectedSha256?.Trim();
        if (!string.IsNullOrEmpty(expected) && !IsSha256(expected))
            throw new ArgumentException("Enter a SHA-256 checksum containing exactly 64 hexadecimal characters.");
        cancellationToken.ThrowIfCancellationRequested();
        // On Windows, deny writes and deletion for the duration of this read.
        await using var input = new FileStream(task.Destination, FileMode.Open, FileAccess.Read, FileShare.Read,
            131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = input.Length;
        var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();
        return new(digest, length,
            IsSha256(task.Sha256) ? string.Equals(digest, task.Sha256, StringComparison.OrdinalIgnoreCase) : null,
            !string.IsNullOrEmpty(expected) ? string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase) : null);
    }

    private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
