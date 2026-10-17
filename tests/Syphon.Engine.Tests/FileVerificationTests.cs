using System.Security.Cryptography;
using System.Text.Json;
using Syphon.Core;
using Syphon.Engine;

internal static class FileVerificationTests
{
    public static async Task<int> StandaloneAsync(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "Syphon.Verify.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var results = new List<object>(); var failed = 0;
        try { await RunAsync(root, async (name, action) => {
            try { await action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex); }
        }); }
        finally {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Syphon.Verify.Tests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test cleanup path.");
            Directory.Delete(root, true);
        }
        var index = Array.IndexOf(args, "--report");
        if (index >= 0 && index + 1 < args.Length) {
            var report = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { failed, results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failed == 0 ? 0 : 1;
    }

    public static async Task RunAsync(string root, Func<string, Func<Task>, Task> run)
    {
        var file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".bin");
        var bytes = new byte[] { 0, 4, 8, 15, 16, 23, 42, 255 };
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var task = new StoredDownload { Url = "https://example.invalid/file", Destination = file, PartPath = file + ".part",
            State = DownloadState.Completed, Received = bytes.Length, Total = bytes.Length, Sha256 = digest };
        async Task Reset() => await File.WriteAllBytesAsync(file, bytes);
        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static async Task Reject<T>(Func<Task> action) where T : Exception {
            try { await action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name);
        }
        try {
            await run("Saved bytes and supplied uppercase SHA-256 match without modifying the record", async () => {
                await Reset(); var result = await DownloadFileVerifier.VerifyAsync(task, " " + digest.ToUpperInvariant() + " ");
                Check(result.Sha256 == digest && result.Length == bytes.Length && result.MatchesRecorded == true && result.MatchesExpected == true, "Matching digest lost.");
                Check(task.Sha256 == digest && (await File.ReadAllBytesAsync(file)).SequenceEqual(bytes), "Verification changed saved data.");
            });
            await run("Changed bytes disagree with the original digest independently of a supplied digest", async () => {
                var changed = bytes.ToArray(); changed[0] = 1; await File.WriteAllBytesAsync(file, changed);
                var current = Convert.ToHexStringLower(SHA256.HashData(changed));
                var result = await DownloadFileVerifier.VerifyAsync(task, current);
                Check(result.MatchesRecorded == false && result.MatchesExpected == true && task.Sha256 == digest, "Mismatch was hidden or saved digest replaced.");
            });
            await run("Wrong publisher digest and absent legacy metadata remain distinct", async () => {
                await Reset(); var result = await DownloadFileVerifier.VerifyAsync(task with { Sha256 = null }, new string('0', 64));
                Check(result.MatchesRecorded is null && result.MatchesExpected == false, "Unknown digest was treated as a match.");
                var noExpected = await DownloadFileVerifier.VerifyAsync(task);
                Check(noExpected.MatchesRecorded == true && noExpected.MatchesExpected is null, "Absent publisher digest was treated as verified.");
            });
            await run("Invalid digests and unfinished jobs are rejected before reading a file", async () => {
                await Reject<ArgumentException>(() => DownloadFileVerifier.VerifyAsync(task with { Destination = file + ".missing" }, "bad"));
                await Reject<ArgumentException>(() => DownloadFileVerifier.VerifyAsync(task, new string('z', 64)));
                await Reject<InvalidOperationException>(() => DownloadFileVerifier.VerifyAsync(task with { State = DownloadState.Paused }));
            });
            await run("Missing files and cancellation do not change the record", async () => {
                await Reject<FileNotFoundException>(() => DownloadFileVerifier.VerifyAsync(task with { Destination = file + ".missing" }));
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                await Reject<OperationCanceledException>(() => DownloadFileVerifier.VerifyAsync(task, cancellationToken: cancelled.Token));
                Check(task.State == DownloadState.Completed && task.Sha256 == digest, "Failure changed the download record.");
            });
            await run("An active writer is rejected and empty files verify correctly", async () => {
                await Reset();
                using (var writer = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                    await Reject<IOException>(() => DownloadFileVerifier.VerifyAsync(task));
                await File.WriteAllBytesAsync(file, []);
                var empty = Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>()));
                var result = await DownloadFileVerifier.VerifyAsync(task with { Sha256 = empty, Total = 0, Received = 0 });
                Check(result.Length == 0 && result.MatchesRecorded == true, "Empty file verification failed.");
            });
        } finally { File.Delete(file); }
    }
}
