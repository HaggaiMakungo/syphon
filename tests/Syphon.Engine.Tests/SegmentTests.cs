using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Syphon.Core;
using Syphon.Engine;
using Syphon.TestServer;

internal static class SegmentTests
{
    private const long Size = 8 * 1024 * 1024 + 17;
    private static readonly TransferOptions Options = new() { SegmentationThreshold = 1048576, RetryBaseDelay = TimeSpan.FromMilliseconds(15), RetryMaximumDelay = TimeSpan.FromMilliseconds(50) };
    public static async Task RunAsync(string root, FixtureServer server, Func<string, Func<Task>, Task> run)
    {
        string Folder() { var p = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
        Uri Url(string mode = "", long size = Size, string? key = null) => new(server.BaseAddress, $"segments?size={size}&mode={mode}&key={key ?? Guid.NewGuid().ToString("N")}");
        using var observer = new HttpClient();

        await run("Adaptive ranges assemble to exactly the single-connection bytes", async () =>
        {
            var folder = Folder(); var key = Guid.NewGuid().ToString("N");
            await using var owner = await DownloadCoordinator.OpenAsync(folder, Options);
            var multi = owner.Add(Url(key: key), Path.Combine(folder, "multi.bin"));
            var single = owner.Add(Url(), Path.Combine(folder, "single.bin"), singleConnection: true);
            owner.Resume(multi.Id); owner.Resume(single.Id);
            await Until(() => owner.Get(multi.Id).Connections > 0);
            await Task.Delay(100);
            using (var early = JsonDocument.Parse(await observer.GetStringAsync(new Uri(server.BaseAddress, $"segment-stats/{key}"))))
                Check(early.RootElement.GetProperty("peak").GetInt32() == 1, "Adaptive download did not start conservatively with one connection.");
            await Task.WhenAll(owner.WaitForIdleAsync(multi.Id), owner.WaitForIdleAsync(single.Id));
            await Verify(owner.Get(multi.Id), Size); await Verify(owner.Get(single.Id), Size);
            Check(owner.Get(multi.Id).Segments.Length == 4 && owner.Get(single.Id).Segments.Length == 0, "Transfer mode was ignored.");
            Check(owner.Get(multi.Id).Sha256 == owner.Get(single.Id).Sha256, "One/four connection bytes differ.");
            using var stats = JsonDocument.Parse(await observer.GetStringAsync(new Uri(server.BaseAddress, $"segment-stats/{key}")));
            Check(stats.RootElement.GetProperty("requestCount").GetInt32() == 4 && stats.RootElement.GetProperty("peak").GetInt32() is >= 1 and <= 4, "Range request count or connection cap was incorrect.");
            Check(!Directory.EnumerateFiles(folder, "*.syphon-part*").Any(), "Successful assembly left data parts.");
        });

        await run("Pause, reopen and resume preserve all range checkpoints", async () =>
        {
            var folder = Folder(); Guid id; long committed;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Options))
            {
                id = owner.Add(Url(size: 33554432), Path.Combine(folder, "result.bin")).Id;
                owner.Resume(id); await Until(() => owner.Get(id).Committed > 1048576);
                await owner.PauseAsync(id);
                var paused = owner.Get(id); committed = paused.Committed;
                Check(paused.State == DownloadState.Paused && paused.Segments.Length == 4 && committed < 33554432, "Pause did not retain partial ranges.");
                Check(paused.Segments.Sum(s => s.Committed) == committed, "Committed coverage lost.");
            }
            await using var reopened = await DownloadCoordinator.OpenAsync(folder, Options);
            Check(reopened.Get(id).Committed == committed && !reopened.Get(id).RestartRequired, "Reopen lost or invalidated checkpoints.");
            for (var i = 0; i < 10; i++) reopened.Resume(id);
            await reopened.WaitForIdleAsync(id); await Verify(reopened.Get(id), 33554432);
        });

        foreach (var mode in new[] { "ignore", "changed", "wrongstart", "wrongend", "wrongtotal", "encoded" })
            await run($"Segment workers reject {mode} without publication", async () =>
            {
                var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, Options);
                var task = owner.Add(Url(mode), Path.Combine(folder, "result.bin"));
                owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
                Check(owner.Get(task.Id).RestartRequired && owner.Get(task.Id).State == DownloadState.Failed && !File.Exists(task.Destination), "Invalid range was published or retried.");
                owner.Restart(task.Id, singleConnection: true); await owner.WaitForIdleAsync(task.Id);
                await Verify(owner.Get(task.Id), Size);
            });

        foreach (var mode in new[] { "ignoreprobe", "weak" })
            await run($"{mode} capability uses a safe ordinary download", async () =>
            {
                var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, Options);
                var task = owner.Add(Url(mode), Path.Combine(folder, "result.bin"));
                owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id); await Verify(owner.Get(task.Id), Size);
                Check(owner.Get(task.Id).Segments.Length == 0, "Unsupported representation was segmented.");
            });

        await run("Interrupted range retries without redownloading committed coverage", async () =>
        {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, Options);
            var task = owner.Add(Url("truncate"), Path.Combine(folder, "result.bin"));
            owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id); await Verify(owner.Get(task.Id), Size);
            Check(owner.Get(task.Id).RetryCount >= 1, "Truncated range did not retry.");
        });

        await run("Shared origin limit covers two segmented files", async () =>
        {
            var folder = Folder(); var key = Guid.NewGuid().ToString("N");
            await using var owner = await DownloadCoordinator.OpenAsync(folder, Options with { MaximumConnectionsPerOrigin = 2 });
            var tasks = Enumerable.Range(0, 2).Select(i => owner.Add(Url(key: key), Path.Combine(folder, $"{i}.bin"))).ToArray();
            foreach (var t in tasks) owner.Resume(t.Id);
            await Task.WhenAll(tasks.Select(t => owner.WaitForIdleAsync(t.Id)));
            foreach (var t in tasks) await Verify(owner.Get(t.Id), Size);
            using var stats = JsonDocument.Parse(await observer.GetStringAsync(new Uri(server.BaseAddress, $"segment-stats/{key}")));
            Check(stats.RootElement.GetProperty("peak").GetInt32() <= 2, "Per-origin limit was exceeded.");
        });

        await run("Segment requests and checkpoints retain offsets beyond 4 GiB", async () =>
        {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, Options);
            var task = owner.Add(Url(size: 8L * 1024 * 1024 * 1024 - 123), Path.Combine(folder, "large.bin"));
            owner.Resume(task.Id);
            await Until(() => owner.Get(task.Id).Segments.Length == 4 && owner.Get(task.Id).Segments.All(s => s.Committed > 65536));
            await owner.PauseAsync(task.Id);
            var saved = owner.Get(task.Id);
            Check(saved.Segments.Last().Start > uint.MaxValue && saved.Committed < 134217728 && !File.Exists(task.Destination), "Large offset test lost bounds or downloaded excessive data.");
            foreach (var segment in saved.Segments)
            {
                using var file = File.OpenRead(DownloadCoordinator.SegmentPath(saved, segment.Index));
                var actual = new byte[1000]; file.ReadExactly(actual);
                var expected = new byte[1000]; Payload.Fill(expected, segment.Start);
                Check(actual.SequenceEqual(expected), "Range data at a 64-bit offset differs.");
            }
        });

        await run("Existing four-part task respects a lower connection limit on resume", async () =>
        {
            var folder = Folder(); Guid id;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Options))
            {
                id = owner.Add(Url(size: 33554432), Path.Combine(folder, "result.bin")).Id;
                owner.Resume(id); await Until(() => owner.Get(id).Committed > 1048576); await owner.PauseAsync(id);
            }
            await using var reopened = await DownloadCoordinator.OpenAsync(folder, Options with { MaximumConnectionsPerFile = 1 });
            reopened.Resume(id);
            var complete = reopened.WaitForIdleAsync(id);
            while (!complete.IsCompleted) { Check(reopened.Get(id).Connections <= 1, "Per-file setting ignored."); await Task.Delay(10); }
            await complete; await Verify(reopened.Get(id), 33554432);
        });

        await run("Removing a segmented task deletes only its owned unfinished files", async () =>
        {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, Options);
            var task = owner.Add(Url(size: 33554432), Path.Combine(folder, "result.bin"));
            var unrelated = Path.Combine(folder, "keep.txt"); await File.WriteAllTextAsync(unrelated, "keep");
            owner.Resume(task.Id); await Until(() => owner.Get(task.Id).Committed > 1048576);
            var snapshot = owner.Get(task.Id);
            await owner.RemoveAsync(task.Id, deletePartial: true);
            Check(owner.GetTasks().Length == 0 && snapshot.Segments.All(s => !File.Exists(DownloadCoordinator.SegmentPath(snapshot, s.Index))) &&
                !File.Exists(task.PartPath) && await File.ReadAllTextAsync(unrelated) == "keep", "Segment cleanup affected the wrong files.");
        });

        foreach (var damage in new[] { "missing", "short", "corrupt", "tail" })
            await run($"Segment recovery validates {damage} part data", async () =>
            {
                var folder = Folder(); StoredDownload saved;
                await using (var owner = await DownloadCoordinator.OpenAsync(folder, Options))
                {
                    var task = owner.Add(Url(size: 33554432), Path.Combine(folder, "result.bin"));
                    owner.Resume(task.Id); await Until(() => owner.Get(task.Id).Committed > 1048576); await owner.PauseAsync(task.Id); saved = owner.Get(task.Id);
                }
                var part = saved.Segments.First(s => s.Committed > 0); var path = DownloadCoordinator.SegmentPath(saved, part.Index);
                if (damage == "missing") File.Delete(path);
                else
                {
                    using var file = new FileStream(path, FileMode.Open, FileAccess.Write);
                    if (damage == "short") file.SetLength(part.Committed - 1);
                    if (damage == "tail") { file.Position = file.Length; file.Write(new byte[123]); }
                    if (damage == "corrupt") { file.Position = 0; file.WriteByte(0xEE); }
                }
                await using var reopened = await DownloadCoordinator.OpenAsync(folder, Options);
                if (damage == "tail")
                {
                    Check(new FileInfo(path).Length == part.Committed && !reopened.Get(saved.Id).RestartRequired, "Uncommitted tail was reused.");
                    reopened.Resume(saved.Id); await reopened.WaitForIdleAsync(saved.Id); await Verify(reopened.Get(saved.Id), 33554432);
                }
                else Check(reopened.Get(saved.Id).RestartRequired, "Damaged segment was trusted.");
            });

        await run("Assembly verifies bytes actually copied, not only an earlier read", async () =>
        {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, Options);
            owner.Boundary = (point, snapshot) =>
            {
                if (point != "BeforeAssembly") return;
                using var file = new FileStream(DownloadCoordinator.SegmentPath(snapshot, 0), FileMode.Open, FileAccess.Write);
                file.WriteByte(0xEE);
            };
            var task = owner.Add(Url(), Path.Combine(folder, "result.bin"));
            owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
            Check(owner.Get(task.Id).RestartRequired && !File.Exists(task.Destination), "Changed assembly input was published.");
        });

        foreach (var boundary in new[] { "SegmentBeforeFlush", "SegmentAfterFlush", "SegmentAfterCommit", "DuringAssembly", "AssemblyFlushed", "BeforeRename", "AfterRename" })
            await run($"Killed segmented process recovers at {boundary}", async () =>
            {
                var folder = Folder(); var start = new ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                foreach (var arg in new[] { "--crash-worker", folder, boundary, Url(size: 33554432).AbsoluteUri }) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!; var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
                try
                {
                    await Until(() => File.Exists(Path.Combine(folder, "boundary.ready")) || process.HasExited);
                    if (process.HasExited) throw new InvalidOperationException($"Crash worker exited: {await error} {await output}");
                    process.Kill(true); await process.WaitForExitAsync();
                }
                finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
                await using var owner = await DownloadCoordinator.OpenAsync(folder, Options);
                var task = owner.GetTasks().Single();
                Check(!task.RestartRequired && task.Segments.Length == 4, $"Recovery failed: {task.Message}");
                if (task.State != DownloadState.Completed)
                {
                    Check(task.State == DownloadState.Paused, "Interrupted task did not recover paused.");
                    owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
                }
                await Verify(owner.Get(task.Id), 33554432);
            });

        foreach (var boundary in new[] { "BeforeFlush", "AfterFlush", "SegmentBeforeFlush", "SegmentAfterFlush", "DuringAssembly", "BeforeRename" })
            await run($"Injected disk failure at {boundary} never publishes", async () =>
            {
                var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, Options);
                owner.Boundary = (point, _) => { if (point == boundary) throw new IOException("Injected disk full / flush failure."); };
                var task = owner.Add(boundary is "BeforeFlush" or "AfterFlush" ? new(server.BaseAddress, "file?size=2097152") : Url(), Path.Combine(folder, "result.bin"));
                owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
                Check(owner.Get(task.Id).State == DownloadState.Failed && !File.Exists(task.Destination), "Disk error reported completion.");
                owner.Boundary = null; owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
                await Verify(owner.Get(task.Id), boundary is "BeforeFlush" or "AfterFlush" ? 2097152 : Size);
            });

        await run("Persistent metadata failure reports failure and preserves old checkpoint", async () =>
        {
            var folder = Folder(); Guid id;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Options))
            {
                id = owner.Add(new(server.BaseAddress, "file?size=2097152"), Path.Combine(folder, "result.bin")).Id;
                owner.BeforePersist = t => { if (t.Committed > 0) throw new IOException("Injected database disk full."); };
                owner.Resume(id); await owner.WaitForIdleAsync(id);
                Check(owner.Get(id).State == DownloadState.Failed && owner.Get(id).Committed == 0 && !File.Exists(owner.Get(id).Destination), "Uncommitted metadata was accepted.");
                owner.BeforePersist = null;
            }
            await using var reopened = await DownloadCoordinator.OpenAsync(folder, Options);
            Check(reopened.Get(id).Committed == 0, "Metadata advanced despite failed commit.");
            reopened.Resume(id); await reopened.WaitForIdleAsync(id); await Verify(reopened.Get(id), 2097152);
        });

        await run("Version 1 tasks migrate without losing their single-file checkpoints", async () =>
        {
            var folder = Folder(); StoredDownload saved;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Options))
            {
                var task = owner.Add(new(server.BaseAddress, "resume?size=8388608"), Path.Combine(folder, "old.bin"));
                owner.Resume(task.Id); await Until(() => owner.Get(task.Id).Received > 131072); await owner.PauseAsync(task.Id); saved = owner.Get(task.Id);
            }
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(folder, "tasks.db")};Pooling=False"))
            {
                connection.Open();
                var json = JsonNode.Parse(JsonSerializer.Serialize(saved))!.AsObject();
                json.Remove("Segments"); json.Remove("Connections"); json.Remove("SingleConnection");
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE tasks SET snapshot=$snapshot; PRAGMA user_version=1";
                command.Parameters.AddWithValue("$snapshot", json.ToJsonString()); command.ExecuteNonQuery();
            }
            await using var reopened = await DownloadCoordinator.OpenAsync(folder, Options);
            Check(reopened.Get(saved.Id).Committed == saved.Committed && reopened.Get(saved.Id).Segments.Length == 0, "Upgrade lost legacy progress.");
            reopened.Resume(saved.Id); await reopened.WaitForIdleAsync(saved.Id); await Verify(reopened.Get(saved.Id), 8388608);
        });

        await run("Overlapping range manifest is rejected before networking", async () =>
        {
            var folder = Folder(); StoredDownload saved;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder, Options))
            {
                var task = owner.Add(Url(size: 33554432), Path.Combine(folder, "bad.bin"));
                owner.Resume(task.Id); await Until(() => owner.Get(task.Id).Committed > 1048576); await owner.PauseAsync(task.Id); saved = owner.Get(task.Id);
            }
            var ranges = saved.Segments.ToArray(); ranges[1] = ranges[1] with { Start = ranges[0].Start };
            using (var store = new DownloadStore(folder)) store.Save(saved with { Segments = ranges });
            await using var reopened = await DownloadCoordinator.OpenAsync(folder, Options);
            Check(reopened.Get(saved.Id).RestartRequired && !File.Exists(saved.Destination), "Overlapping manifest was accepted.");
        });
    }

    private static async Task Verify(StoredDownload task, long length)
    {
        Check(task.State == DownloadState.Completed, $"Transfer failed: {task.State}: {task.Message} ({task.Diagnostic})");
        await using var file = File.OpenRead(task.Destination);
        Check(file.Length == length && Convert.ToHexStringLower(await SHA256.HashDataAsync(file)) == Payload.ExpectedHash(length), "Completed bytes differ.");
    }
    private static async Task Until(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition()) { if (clock.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException("Segment operation timed out."); await Task.Delay(10); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
