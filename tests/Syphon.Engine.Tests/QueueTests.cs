using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Security.Cryptography;
using Syphon.Core;
using Syphon.Engine;
using Syphon.TestServer;

internal static class QueueTests
{
    private static readonly TransferOptions Fast = new()
    { MaximumConcurrentDownloads = 1, MaximumRetries = 2, RetryBaseDelay = TimeSpan.FromMilliseconds(15), RetryMaximumDelay = TimeSpan.FromMilliseconds(50) };

    public static async Task RunAsync(string root, FixtureServer server, Func<string, Func<Task>, Task> run)
    {
        string Folder() { var p = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
        Uri Url(string route) => new(server.BaseAddress, route);
        string RetryRoute(string key, string mode, int failures = 2, string retry = "") =>
            $"retry?key={key}&mode={mode}&failures={failures}&retry={retry}&size=1048576";
        using var observer = new HttpClient();
        async Task<int> Attempts(string key) => int.Parse(await observer.GetStringAsync(Url($"attempts/{key}")));

        await run("Queue enforces two active transfers and completes every file", async () =>
        {
            var folder = Folder();
            await using var owner = await DownloadCoordinator.OpenAsync(folder);
            var tasks = Enumerable.Range(0, 5).Select(i => owner.Add(Url("resume?size=2097152"), Path.Combine(folder, $"{i}.bin"))).ToArray();
            foreach (var task in tasks) owner.Resume(task.Id);
            Check(owner.GetTasks().Count(t => t.State == DownloadState.Queued) >= 3, "Extra downloads were admitted.");
            var all = Task.WhenAll(tasks.Select(t => owner.WaitForIdleAsync(t.Id)));
            while (!all.IsCompleted)
            {
                Check(owner.GetTasks().Count(t => t.State is DownloadState.Starting or DownloadState.Downloading or DownloadState.Publishing) <= 2, "Concurrency limit exceeded.");
                await Task.Delay(5);
            }
            await all;
            foreach (var task in tasks) await Verify(owner.Get(task.Id), 2097152);
        });

        await run("FIFO queue, duplicate Resume and pause before admission", async () =>
        {
            var folder = Folder();
            await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
            var a = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "a.bin"));
            var key = Guid.NewGuid().ToString("N");
            var b = owner.Add(Url(RetryRoute(key, "503", 0)), Path.Combine(folder, "b.bin"));
            var c = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "c.bin"));
            owner.Resume(a.Id); owner.Resume(b.Id); owner.Resume(c.Id);
            for (var i = 0; i < 10; i++) owner.Resume(b.Id);
            Check(owner.Get(b.Id).State == DownloadState.Queued && await Attempts(key) == 0, "Queued task contacted the server.");
            await owner.PauseAsync(b.Id);
            await owner.PauseAsync(a.Id);
            await Until(() => owner.Get(c.Id).Received > 0);
            var pausedState = owner.Get(b.Id).State;
            var pausedAttempts = await Attempts(key);
            Check(pausedState == DownloadState.Paused && pausedAttempts == 0, $"Paused queue entry started: state={pausedState}, requests={pausedAttempts}, message={owner.Get(b.Id).Message}.");
            await owner.PauseAsync(c.Id);
            owner.Resume(b.Id);
            await owner.WaitForIdleAsync(b.Id);
            Check(await Attempts(key) == 1, "Repeated Resume created duplicate requests.");
            await Verify(owner.Get(b.Id), 1048576);
        });

        await run("Queue order is FIFO when slots become available", async () =>
        {
            var folder = Folder();
            await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
            var tasks = Enumerable.Range(0, 3).Select(i => owner.Add(Url("resume?size=8388608"), Path.Combine(folder, $"{i}.bin"))).ToArray();
            foreach (var task in tasks) owner.Resume(task.Id);
            await owner.PauseAsync(tasks[0].Id);
            await Until(() => owner.Get(tasks[1].Id).State == DownloadState.Downloading);
            Check(owner.Get(tasks[2].Id).State == DownloadState.Queued, "Third task jumped ahead.");
            await owner.PauseAsync(tasks[1].Id);
            await Until(() => owner.Get(tasks[2].Id).State == DownloadState.Downloading);
        });

        await run("Closing restores queued tasks paused without making requests", async () =>
        {
            var folder = Folder();
            var key = Guid.NewGuid().ToString("N");
            Guid queued;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Fast))
            {
                var active = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "a.bin"));
                queued = owner.Add(Url(RetryRoute(key, "503", 0)), Path.Combine(folder, "b.bin")).Id;
                owner.Resume(active.Id); owner.Resume(queued);
            }
            await using var restored = await DownloadCoordinator.OpenAsync(folder, Fast);
            Check(restored.Get(queued).State == DownloadState.Paused && await Attempts(key) == 0, "Reopening auto-started queued work.");
        });

        await run("Persistent priority and manual order control admission without preemption", async () =>
        {
            var folder = Folder(); Guid a, b, c;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Fast))
            {
                a = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "a.bin")).Id;
                b = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "b.bin")).Id;
                c = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "c.bin")).Id;
                owner.MoveInQueue(c, -1);
                Check(owner.GetTasks().Select(t => t.Id).SequenceEqual(new[] { a, c, b }), "Move up did not swap peers.");
                owner.SetPriority(b, 1); owner.SetPriority(a, -1);
            }
            await using var restored = await DownloadCoordinator.OpenAsync(folder, Fast);
            Check(restored.GetTasks().Select(t => t.Id).SequenceEqual(new[] { b, c, a }), "Priority/order were not durable.");
            restored.ResumeAll();
            Check(restored.Get(b).State != DownloadState.Queued && restored.Get(a).State == DownloadState.Queued, "High priority did not get the first slot.");
            restored.SetPriority(a, 1);
            Check(restored.Get(a).State == DownloadState.Queued, "Changing priority preempted a running download.");
            await restored.PauseAllAsync();
            Check(restored.GetTasks().All(t => t.State == DownloadState.Paused), "Pause all left a queued worker active.");
            await Task.Delay(50);
            Check(restored.GetTasks().All(t => t.State == DownloadState.Paused), "Pause all admitted queued work.");
        });

        await run("Live concurrency and deferred connection settings preserve active files", async () =>
        {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
            var a = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "a.bin"));
            var b = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "b.bin"));
            owner.Resume(a.Id); owner.Resume(b.Id);
            owner.Configure(Fast with { MaximumConcurrentDownloads = 2 });
            Check(owner.Get(b.Id).State != DownloadState.Queued, "Increasing concurrency did not release a slot.");
            owner.Configure(Fast with { MaximumConcurrentDownloads = 1, MaximumConnectionsPerOrigin = 1, MaximumConnectionsPerFile = 1 });
            Check(owner.SettingsPending, "Connection limits replaced a live transport.");
            await owner.PauseAllAsync();
            Check(!owner.SettingsPending && owner.Options.MaximumConnectionsPerOrigin == 1, "Deferred settings did not apply after draining.");
            owner.ResumeAll();
            Check(owner.GetTasks().Count(t => t.State == DownloadState.Queued) == 1, "New concurrency limit was ignored.");
            await Task.WhenAll(owner.WaitForIdleAsync(a.Id), owner.WaitForIdleAsync(b.Id));
            await Verify(owner.Get(a.Id), 8388608); await Verify(owner.Get(b.Id), 8388608);
            try { owner.Configure(Fast with { MaximumConcurrentDownloads = 0 }); throw new Exception("Invalid settings were accepted."); }
            catch (ArgumentOutOfRangeException) { }
            Check(owner.Options.MaximumConcurrentDownloads == 1, "Rejected settings changed the engine.");
        });

        await run("History dates and average survive pause, reopen and completion", async () =>
        {
            var folder = Folder(); Guid id; double active;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Fast))
            {
                id = owner.Add(Url("resume?size=2097152"), Path.Combine(folder, "history.bin")).Id;
                Check(owner.Get(id).CreatedAt is not null && owner.Get(id).StartedAt is null, "Creation date missing or start fabricated.");
                owner.Resume(id); await Until(() => owner.Get(id).Committed > 0); await owner.PauseAsync(id);
                active = owner.Get(id).ActiveSeconds;
                Check(active > 0 && owner.Get(id).FinishedAt is null, "Pause timing is invalid.");
                await Task.Delay(150); Check(owner.Get(id).ActiveSeconds == active, "Paused time was counted.");
            }
            await using var restored = await DownloadCoordinator.OpenAsync(folder, Fast);
            Check(restored.Get(id).ActiveSeconds == active, "Active duration was lost on reopen.");
            restored.Resume(id); await restored.WaitForIdleAsync(id);
            var result = restored.Get(id); await Verify(result, 2097152);
            Check(result.FinishedAt >= result.StartedAt && result.StartedAt >= result.CreatedAt, "History timestamps are out of order.");
            Check(result.AverageBytesPerSecond > 0 && result.ActiveSeconds >= active, "Average speed or duration missing.");
            await restored.PauseAsync(id); Check(restored.Get(id).FinishedAt == result.FinishedAt, "Completed timestamp was overwritten.");
        });

        await run("Legacy metadata migrates without inventing history dates or speeds", async () =>
        {
            var folder = Folder(); Guid id;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Fast))
            {
                id = owner.Add(Url("file?size=100003"), Path.Combine(folder, "legacy.bin")).Id;
                owner.Resume(id); await owner.WaitForIdleAsync(id);
            }
            using (var db = new SqliteConnection($"Data Source={Path.Combine(folder, "tasks.db")};Pooling=False"))
            {
                db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT snapshot FROM tasks";
                var snapshot = JsonNode.Parse((string)cmd.ExecuteScalar()!)!.AsObject();
                foreach (var key in new[] { "Priority", "QueueOrder", "CreatedAt", "StartedAt", "FinishedAt", "ActiveSeconds", "HasTiming" }) snapshot.Remove(key);
                cmd.CommandText = "UPDATE tasks SET snapshot=$snapshot; PRAGMA user_version=5";
                cmd.Parameters.AddWithValue("$snapshot", snapshot.ToJsonString()); cmd.ExecuteNonQuery();
            }
            await using var restored = await DownloadCoordinator.OpenAsync(folder, Fast);
            var task = restored.Get(id); await Verify(task, 100003);
            Check(task.CreatedAt is null && task.FinishedAt is null && task.AverageBytesPerSecond is null, "Migration fabricated historical data.");
            Check(task.QueueOrder > 0 && task.Priority == 0, "Legacy queue defaults were not applied.");
        });

        await run("Configured threshold and connection count govern new segmented jobs", async () =>
        {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
            owner.Configure(Fast with { MaximumConnectionsPerFile = 2, MaximumConnectionsPerOrigin = 2, SegmentationThreshold = 1048576 });
            var task = owner.Add(Url("segments?size=8388608"), Path.Combine(folder, "segments.bin"));
            owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id); await Verify(owner.Get(task.Id), 8388608);
            Check(owner.Get(task.Id).Segments.Length == 2, "Configured segmentation count/threshold was not used.");
            owner.Configure(Fast with { SegmentationThreshold = 16777216 });
            var plain = owner.Add(Url("segments?size=8388608"), Path.Combine(folder, "plain.bin"));
            owner.Resume(plain.Id); await owner.WaitForIdleAsync(plain.Id); await Verify(owner.Get(plain.Id), 8388608);
            Check(owner.Get(plain.Id).Segments.Length == 0, "Raised threshold was ignored.");
        });

        foreach (var status in new[] { "408", "429", "500", "502", "503", "504" })
            await run($"Automatic bounded retry recovers HTTP {status}", async () =>
            {
                var folder = Folder(); var key = Guid.NewGuid().ToString("N");
                await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
                var task = owner.Add(Url(RetryRoute(key, status)), Path.Combine(folder, "result.bin"));
                owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
                Check(await Attempts(key) == 3 && owner.Get(task.Id).RetryCount == 2, "Retry count differs.");
                await Verify(owner.Get(task.Id), 1048576);
            });

        foreach (var status in new[] { "400", "401", "403", "404", "410", "501" })
            await run($"HTTP {status} is explained without automatic retry", async () =>
            {
                var folder = Folder(); var key = Guid.NewGuid().ToString("N");
                await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
                var task = owner.Add(Url(RetryRoute(key, status, 99)), Path.Combine(folder, "result.bin"));
                owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
                var failed = owner.Get(task.Id);
                Check(failed.State == DownloadState.Failed && failed.RetryCount == 0 && await Attempts(key) == 1, "Permanent failure was retried.");
                Check(failed.Diagnostic == $"HTTP {status}", "HTTP details missing.");
                if (status is "401" or "403") Check(failed.Message.Contains("fresh download link"), "Expired/permission error has no useful advice.");
            });

        await run("Retry budget stops persistent failures", async () =>
        {
            var folder = Folder(); var key = Guid.NewGuid().ToString("N");
            await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
            var task = owner.Add(Url(RetryRoute(key, "503", 99)), Path.Combine(folder, "result.bin"));
            owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
            Check(owner.Get(task.Id).State == DownloadState.Failed && await Attempts(key) == 3, "Retry budget was exceeded.");
        });

        foreach (var value in new[] { "1", "date", "invalid" })
            await run($"Retry-After {value} with released transfer slot", async () =>
            {
                var folder = Folder(); var key = Guid.NewGuid().ToString("N");
                await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast with { RetryBaseDelay = TimeSpan.FromMilliseconds(100), RetryMaximumDelay = TimeSpan.FromMilliseconds(100) });
                var task = owner.Add(Url(RetryRoute(key, "429", 1, value)), Path.Combine(folder, "a.bin"));
                var next = owner.Add(Url("file?size=1234"), Path.Combine(folder, "b.bin"));
                owner.Resume(task.Id); owner.Resume(next.Id);
                await Until(() => owner.Get(task.Id).State == DownloadState.RetryWaiting);
                if (value == "1") Check(owner.Get(task.Id).RetryAt > DateTimeOffset.UtcNow.AddMilliseconds(750), "Retry-After was shortened.");
                await owner.WaitForIdleAsync(next.Id);
                await Verify(owner.Get(next.Id), 1234);
                await owner.WaitForIdleAsync(task.Id);
                await Verify(owner.Get(task.Id), 1048576);
            });

        await run("Excessive Retry-After requires manual retry instead of early request", async () =>
        {
            var folder = Folder(); var key = Guid.NewGuid().ToString("N");
            await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
            var task = owner.Add(Url(RetryRoute(key, "503", 99, "600")), Path.Combine(folder, "a.bin"));
            owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
            Check(owner.Get(task.Id).State == DownloadState.Failed && await Attempts(key) == 1, "Excessive server delay caused an early retry.");
        });

        foreach (var cancel in new[] { false, true })
            await run($"{(cancel ? "Cancel" : "Pause")} interrupts retry wait and prevents another request", async () =>
            {
                var folder = Folder(); var key = Guid.NewGuid().ToString("N");
                await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
                var task = owner.Add(Url(RetryRoute(key, "503", 99, "1")), Path.Combine(folder, "a.bin"));
                owner.Resume(task.Id);
                await Until(() => owner.Get(task.Id).State == DownloadState.RetryWaiting);
                await owner.PauseAsync(task.Id, cancel);
                await Task.Delay(1050);
                Check(owner.Get(task.Id).State == (cancel ? DownloadState.Cancelled : DownloadState.Paused) && await Attempts(key) == 1, "Stopped retry ran again.");
            });

        await run("Network disconnect retries from a verified checkpoint", async () =>
        {
            var folder = Folder(); var key = Guid.NewGuid().ToString("N");
            await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
            var task = owner.Add(Url(RetryRoute(key, "disconnect", 1)), Path.Combine(folder, "a.bin"));
            owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
            Check(await Attempts(key) == 2 && owner.Get(task.Id).RetryCount == 1, "Disconnected transfer did not retry.");
            await Verify(owner.Get(task.Id), 1048576);
        });

        await run("Network disconnect with weak validator never silently restarts", async () =>
        {
            var folder = Folder(); var key = Guid.NewGuid().ToString("N");
            await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
            var task = owner.Add(Url(RetryRoute(key, "weak", 1)), Path.Combine(folder, "a.bin"));
            owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
            Check(owner.Get(task.Id).RestartRequired && await Attempts(key) == 1 && !File.Exists(task.Destination), "Weak data was silently reused.");
        });

        await run("Remove active, queued and completed tasks preserves user files", async () =>
        {
            var folder = Folder(); var key = Guid.NewGuid().ToString("N");
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Fast))
            {
                var keep = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "keep.bin"));
                owner.Resume(keep.Id); await Until(() => owner.Get(keep.Id).Received > 0);
                var queued = owner.Add(Url(RetryRoute(key, "503", 0)), Path.Combine(folder, "queued.bin"));
                owner.Resume(queued.Id);
                await owner.RemoveAsync(queued.Id, true);
                Check(await Attempts(key) == 0, "Removed queued task started.");
                await owner.RemoveAsync(keep.Id);
                Check(File.Exists(keep.PartPath), "Default removal deleted partial data.");
                var deleted = owner.Add(Url("resume?size=8388608"), Path.Combine(folder, "deleted.bin"));
                owner.Resume(deleted.Id); await Until(() => owner.Get(deleted.Id).Received > 0);
                await owner.RemoveAsync(deleted.Id, true);
                Check(!File.Exists(deleted.PartPath), "Requested partial deletion did not happen.");
                var done = owner.Add(Url("file?size=1234"), Path.Combine(folder, "done.bin"));
                owner.Resume(done.Id); await owner.WaitForIdleAsync(done.Id);
                await owner.RemoveAsync(done.Id, true);
                Check(File.Exists(done.Destination) && File.Exists(keep.PartPath) && owner.GetTasks().Length == 0, "Removal affected a final or unrelated file.");
            }
            await using var reopened = await DownloadCoordinator.OpenAsync(folder, Fast);
            Check(reopened.GetTasks().Length == 0, "Removed records returned after reopening.");
        });

        await run("Untrusted HTTPS certificate fails without retries", async () =>
        {
            var folder = Folder();
            await using var tls = await FixtureServer.StartAsync(https: true);
            await using var owner = await DownloadCoordinator.OpenAsync(folder, Fast);
            var task = owner.Add(new(tls.BaseAddress, "file?size=1000"), Path.Combine(folder, "a.bin"));
            owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
            Check(owner.Get(task.Id).State == DownloadState.Failed && owner.Get(task.Id).RetryCount == 0 &&
                owner.Get(task.Id).Diagnostic?.Contains("SecureConnectionError") == true && !File.Exists(task.Destination), "Certificate failure was ignored or retried.");
        });
    }

    private static async Task Verify(StoredDownload task, long size)
    {
        Check(task.State == DownloadState.Completed, $"Transfer failed: {task.State}: {task.Message}");
        await using var file = File.OpenRead(task.Destination);
        Check(file.Length == size && Convert.ToHexStringLower(await SHA256.HashDataAsync(file)) == Payload.ExpectedHash(size), "Output differs from fixture.");
    }
    private static async Task Until(Func<bool> predicate)
    {
        var until = Stopwatch.StartNew();
        while (!predicate()) { if (until.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Queue operation timed out."); await Task.Delay(5); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
