using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Syphon.Core;
using Syphon.Engine;
using Syphon.TestServer;

if (args.FirstOrDefault() == "--media-tool-security-tests") return MediaToolSecurityTests.Run(args.Skip(1).ToArray());
if (args.FirstOrDefault() == "--verify-packaged-media-policy") return MediaToolSecurityTests.VerifyPackagedAssembly(args[1]);
if (args.FirstOrDefault() == "--category-tests") return await CategoryTests.RunAsync();
if (args.FirstOrDefault() == "--progressive-tests") return await ProgressiveTests.StandaloneAsync(args.Skip(1).ToArray());
if (args.FirstOrDefault() == "--verify-tests") return await FileVerificationTests.StandaloneAsync(args.Skip(1).ToArray());
if (args.FirstOrDefault() == "--speed-tests") return await RateScheduleTests.StandaloneAsync(args.Skip(1).ToArray());
if (args.FirstOrDefault() == "--media-tests") return await MediaTests.RunAsync(args.Skip(1).ToArray());
if (args.FirstOrDefault() == "--youtube-tests") return await YoutubeTests.RunAsync(args.Skip(1).ToArray());
if (args.FirstOrDefault() == "--youtube-live") return await YoutubeTests.LiveAsync(args.Skip(1).ToArray());
if (args.FirstOrDefault() == "--size-tests") return await SizeDiscoveryTests.RunAsync();
if (args.FirstOrDefault() == "--inspect-media-source") {
    using var source = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var duration = await MediaTools.InspectDurationAsync(new(source.RootElement.GetProperty("Url").GetString()!), budget.Token);
    Console.WriteLine(JsonSerializer.Serialize(new { Duration = duration }));return 0;
}

if (args.FirstOrDefault() == "--crash-worker")
{
    await DurableTests.CrashWorkerAsync(args[1], args[2], args[3]);
    return 0;
}

var root = Path.Combine(Path.GetTempPath(), "Syphon.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var results = new List<object>();
var failed = 0;
var large = args.Contains("--large");
await using var server = await FixtureServer.StartAsync();
using var engine = new HttpDownloadEngine();
Uri Url(string route) => new(server.BaseAddress, route);
string Destination() => Path.Combine(root, Guid.NewGuid().ToString("N") + ".bin");

async Task Run(string name, Func<Task> action)
{
    var clock = Stopwatch.StartNew();
    var before = Directory.GetFileSystemEntries(root).ToHashSet(StringComparer.OrdinalIgnoreCase);
    try
    {
        await action();
        results.Add(new { name, passed = true, elapsedMs = clock.ElapsedMilliseconds });
        Console.WriteLine($"PASS {name} ({clock.ElapsedMilliseconds} ms)");
    }
    catch (Exception ex)
    {
        failed++;
        results.Add(new { name, passed = false, elapsedMs = clock.ElapsedMilliseconds, error = ex.ToString() });
        Console.WriteLine($"FAIL {name}: {ex}");
    }
    finally
    {
        // Every fixture uses this run's unique directory; release each case's files promptly.
        foreach (var entry in Directory.GetFileSystemEntries(root).Where(p => !before.Contains(p)))
        {
            var full = Path.GetFullPath(entry);
            if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test cleanup escaped its fixture directory.");
            if (Directory.Exists(full)) Directory.Delete(full, recursive: true); else File.Delete(full);
        }
    }
}

async Task VerifyDownload(string route, long length)
{
    var destination = Destination();
    var updates = new List<DownloadProgress>();
    var result = await engine.DownloadAsync(new(Url(route), destination), new InlineProgress(updates.Add));
    Check(new FileInfo(destination).Length == length && result.Length == length, "Saved length differs from fixture.");
    await using var file = File.OpenRead(destination);
    var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
    Check(actual == Payload.ExpectedHash(length) && result.Sha256 == actual, "Saved bytes or reported digest differ from fixture.");
    Check(updates.First().State == DownloadState.Starting && updates.Last().State == DownloadState.Completed, "Missing lifecycle updates.");
    Check(!Directory.EnumerateFiles(root, ".*.syphon-part").Any(), "Successful transfer left an unfinished part.");
    file.Close();
    File.Delete(destination);
}

try
{
    if (!args.Contains("--queue-only") && !args.Contains("--segments-only") && !args.Contains("--internet-only"))
    {
    await Run("Known-size bytes and SHA-256", () => VerifyDownload("file?size=4194321", 4194321));
    await Run("Unknown-size streamed response", () => VerifyDownload("unknown?size=1000003", 1000003));
    await Run("Empty file", () => VerifyDownload("file?size=0", 0));
    await Run("Relative redirect preserves query", () => VerifyDownload("redirect?size=900001", 900001));
    await Run("64 MiB streamed transfer", () => VerifyDownload("file?size=67108864", 67108864));
    await Run("Existing destination is preserved", async () =>
    {
        var destination = Destination();
        await File.WriteAllTextAsync(destination, "keep me");
        await ExpectFailure(() => engine.DownloadAsync(new(Url("file"), destination)), DownloadFailure.DestinationExists);
        Check(await File.ReadAllTextAsync(destination) == "keep me", "Existing file changed.");
        File.Delete(destination);
    });
    await Run("Publication race does not overwrite", async () =>
    {
        var destination = Destination();
        var request = new DownloadRequest(Url("file?size=100000"), destination);
        var created = false;
        try
        {
            await ExpectFailure(() => engine.DownloadAsync(request, new InlineProgress(p =>
            {
                if (!created && p.State == DownloadState.Downloading)
                { File.WriteAllText(destination, "arrived during transfer"); created = true; }
            })), DownloadFailure.DestinationExists);
            Check(await File.ReadAllTextAsync(destination) == "arrived during transfer", "Racing destination changed.");
            var part = Directory.EnumerateFiles(root, $"*.{request.Id:N}.syphon-part").Single();
            Check(new FileInfo(part).Length == 100000, "Complete part was not retained after publication failure.");
            File.Delete(part);
        }
        finally { File.Delete(destination); }
    });
    await Run("Cancellation retains and releases unfinished file", async () =>
    {
        using var cancel = new CancellationTokenSource();
        var request = new DownloadRequest(Url("slow?size=8388608"), Destination());
        try
        {
            await engine.DownloadAsync(request, new InlineProgress(p =>
            { if (p.BytesReceived >= 65536) cancel.Cancel(); }), cancel.Token);
            throw new InvalidOperationException("Cancellation was ignored.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        Check(!File.Exists(request.DestinationPath), "Cancelled transfer published a file.");
        var part = Directory.EnumerateFiles(root, $"*.{request.Id:N}.syphon-part").Single();
        using (File.Open(part, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Check(new FileInfo(part).Length is > 0 and < 8388608, "Unexpected partial length.");
        File.Delete(part);
    });
    await Run("Header timeout", async () =>
    {
        using var timed = new HttpDownloadEngine(new() { HeaderTimeout = TimeSpan.FromMilliseconds(300) });
        await ExpectFailure(() => timed.DownloadAsync(new(Url("headers-delay"), Destination())), DownloadFailure.Timeout);
    });
    await Run("Cancellation while awaiting headers remains cancellation", async () =>
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var request = new DownloadRequest(Url("headers-delay"), Destination());
        try { await engine.DownloadAsync(request, cancellationToken: cancel.Token); throw new InvalidOperationException("Cancellation was ignored."); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        Check(!File.Exists(request.DestinationPath), "Header cancellation published a file.");
    });
    await Run("Duplicate task cannot start a second worker", async () =>
    {
        using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new DownloadRequest(Url("slow?size=8388608"), Destination());
        var first = engine.DownloadAsync(request, new InlineProgress(p =>
        { if (p.State == DownloadState.Downloading) started.TrySetResult(); }), cancel.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ExpectFailure(() => engine.DownloadAsync(request), DownloadFailure.AlreadyRunning);
        }
        finally
        {
            cancel.Cancel();
            try { await first; } catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            foreach (var part in Directory.EnumerateFiles(root, $"*.{request.Id:N}.syphon-part")) File.Delete(part);
        }
    });
    await Run("Body idle timeout", async () =>
    {
        using var timed = new HttpDownloadEngine(new() { BodyIdleTimeout = TimeSpan.FromMilliseconds(300) });
        var request = new DownloadRequest(Url("stall"), Destination());
        await ExpectFailure(() => timed.DownloadAsync(request), DownloadFailure.Timeout);
        Check(!File.Exists(request.DestinationPath), "Stalled transfer published a file.");
        foreach (var part in Directory.EnumerateFiles(root, $"*.{request.Id:N}.syphon-part")) File.Delete(part);
    });
    await Run("Truncated response is never published", async () =>
    {
        var request = new DownloadRequest(Url("truncate"), Destination());
        await ExpectFailure(() => engine.DownloadAsync(request), DownloadFailure.IncompleteResponse, DownloadFailure.Network);
        Check(!File.Exists(request.DestinationPath), "Truncated file published.");
        foreach (var part in Directory.EnumerateFiles(root, $"*.{request.Id:N}.syphon-part")) File.Delete(part);
    });
    await Run("HTTP errors and unexpected partial responses", async () =>
    {
        await ExpectFailure(() => engine.DownloadAsync(new(Url("status/503"), Destination())), DownloadFailure.HttpStatus);
        await ExpectFailure(() => engine.DownloadAsync(new(Url("wrong-range"), Destination())), DownloadFailure.HttpStatus);
    });
    await Run("Unexpected content encoding rejected", () => ExpectFailure(
        () => engine.DownloadAsync(new(Url("encoded"), Destination())), DownloadFailure.UnexpectedResponse));
    await Run("Redirect loop bounded", () => ExpectFailure(
        () => engine.DownloadAsync(new(Url("redirect-loop"), Destination())), DownloadFailure.UnexpectedResponse));
    await Run("Unsupported schemes and credentials rejected", async () =>
    {
        await ExpectFailure(() => engine.DownloadAsync(new(new Uri("file:///C:/unused"), Destination())), DownloadFailure.InvalidRequest);
        await ExpectFailure(() => engine.DownloadAsync(new(new Uri("http://user:password@127.0.0.1/"), Destination())), DownloadFailure.InvalidRequest);
        await ExpectFailure(() => engine.DownloadAsync(new(Url("file"), Path.GetPathRoot(root)!)), DownloadFailure.InvalidRequest);
    });
    await Run("Fixture supports offsets beyond 4 GiB and 416", async () =>
    {
        using var client = new HttpClient();
        const long offset = 4294967311;
        using var message = new HttpRequestMessage(HttpMethod.Get, Url($"range?size={offset + 1000}"));
        message.Headers.Range = new RangeHeaderValue(offset, offset + 99);
        using var response = await client.SendAsync(message);
        Check(response.StatusCode == HttpStatusCode.PartialContent, "Fixture did not serve range.");
        Check(response.Content.Headers.ContentRange?.From == offset, "64-bit offset changed.");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var expected = new byte[100];
        Payload.Fill(expected, offset);
        Check(bytes.SequenceEqual(expected), "Range bytes differ.");
        using var invalid = new HttpRequestMessage(HttpMethod.Get, Url("range?size=10"));
        invalid.Headers.Range = new RangeHeaderValue(100, 200);
        using var rejected = await client.SendAsync(invalid);
        Check(rejected.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable, "Fixture did not return 416.");
    });
    await Run("Fixture can change body and strong validator at one URL", async () =>
    {
        using var client = new HttpClient();
        using var before = await client.GetAsync(Url("mutable"));
        var beforeBytes = await before.Content.ReadAsByteArrayAsync();
        using var changed = await client.PostAsync(Url("mutable/bump"), null);
        changed.EnsureSuccessStatusCode();
        using var after = await client.GetAsync(Url("mutable"));
        var afterBytes = await after.Content.ReadAsByteArrayAsync();
        Check(before.Headers.ETag != after.Headers.ETag && !beforeBytes.SequenceEqual(afterBytes), "Fixture representation did not change.");
    });
    if (large)
        await Run("Full transfer larger than 4 GiB", () => VerifyDownload("file?size=4295032832", 4295032832));
    await DurableTests.RunAsync(root, server, Run);
    }
    if (!args.Contains("--segments-only") && !args.Contains("--internet-only")) { await QueueTests.RunAsync(root, server, Run); await SiteCompatibilityTests.RunAsync(root, server, Run); await RateScheduleTests.RunAsync(root, server, Run); await FileVerificationTests.RunAsync(root, Run); await ProgressiveTests.RunAsync(root, server, Run); await ManifestValidationTests.RunAsync(server, Run); }
    if (!args.Contains("--queue-only") && !args.Contains("--internet-only"))
        await SegmentTests.RunAsync(root, server, (name, action) => args.Contains("--crash-only") && !name.StartsWith("Killed segmented") ? Task.CompletedTask : Run(name, action));
    if (args.Contains("--internet") || args.Contains("--internet-only")) await InternetChecks.RunAsync(root, Run);
}
finally
{
    // This directory is created uniquely above and contains only this run's fixtures.
    Directory.Delete(root, recursive: true);
}

var reportIndex = Array.IndexOf(args, "--report");
if (reportIndex >= 0 && reportIndex + 1 < args.Length)
{
    var path = Path.GetFullPath(args[reportIndex + 1]);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { large, internet = args.Contains("--internet") || args.Contains("--internet-only"), queueOnly = args.Contains("--queue-only"), internetOnly = args.Contains("--internet-only"), failed, results }, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine($"{results.Count - failed}/{results.Count} passed");
return failed == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task ExpectFailure(Func<Task<DownloadResult>> action, params DownloadFailure[] failures)
{
    try { await action(); }
    catch (DownloadException ex) when (failures.Contains(ex.Failure)) { return; }
    throw new InvalidOperationException("Expected download failure was not observed.");
}

sealed class InlineProgress(Action<DownloadProgress> action) : IProgress<DownloadProgress>
{
    public void Report(DownloadProgress value) => action(value);
}
