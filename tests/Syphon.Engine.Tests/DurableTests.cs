using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Syphon.Core;
using Syphon.Engine;
using Syphon.TestServer;

internal static class DurableTests
{
    private const long Size = 8 * 1024 * 1024;
    public static async Task RunAsync(string root, FixtureServer server, Func<string, Func<Task>, Task> run)
    {
        string Folder() { var path = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
        Uri Url(string mode = "", long size = Size) => new(server.BaseAddress, $"resume?size={size}&mode={mode}");

        await run("Durable pause, duplicate Resume, reopen and verified continuation", async () =>
        {
            var folder = Folder();
            Guid id;
            long committed;
            await using (var owner = await DownloadCoordinator.OpenAsync(folder))
            {
                id = owner.Add(Url(), Path.Combine(folder, "result.bin")).Id;
                owner.Resume(id);
                await Until(() => owner.Get(id).Received > 131072);
                await owner.PauseAsync(id);
                var paused = owner.Get(id);
                committed = paused.Committed;
                Check(paused.State == DownloadState.Paused && committed > 0 && committed < Size, "Pause did not commit partial progress.");
                Check(new FileInfo(paused.PartPath).Length == committed, "Checkpoint differs from part length.");
            }
            await using (var owner = await DownloadCoordinator.OpenAsync(folder))
            {
                Check(owner.Get(id).Committed == committed && owner.Get(id).State == DownloadState.Paused, "Reopen lost progress/state.");
                for (var i = 0; i < 12; i++) owner.Resume(id);
                await owner.WaitForIdleAsync(id);
                await VerifyCompleted(owner.Get(id), Size);
            }
            await using var final = await DownloadCoordinator.OpenAsync(folder);
            Check(final.GetTasks().Length == 1 && final.Get(id).State == DownloadState.Completed, "Completed history lost.");
        });

        foreach (var mode in new[] { "ignore", "changed", "wrongstart", "wrongtotal", "missingetag", "encoded", "416" })
            await run($"Resume rejects {mode} without appending", async () =>
            {
                var folder = Folder();
                await using var owner = await DownloadCoordinator.OpenAsync(folder);
                var id = owner.Add(Url(mode), Path.Combine(folder, "result.bin")).Id;
                owner.Resume(id);
                await Until(() => owner.Get(id).Received > 131072);
                await owner.PauseAsync(id);
                var before = owner.Get(id);
                var bytes = await File.ReadAllBytesAsync(before.PartPath);
                owner.Resume(id);
                await owner.WaitForIdleAsync(id);
                var after = owner.Get(id);
                Check(after.RestartRequired && after.State == DownloadState.Failed, $"Invalid response was accepted: {after.Message}");
                var afterBytes = await File.ReadAllBytesAsync(before.PartPath);
                Check(bytes.SequenceEqual(afterBytes) && !File.Exists(before.Destination), "Rejected response changed partial/output.");
            });

        foreach (var mode in new[] { "weak", "noetag", "unknown" })
            await run($"Resume requires restart for {mode} identity", async () =>
            {
                var folder = Folder();
                await using var owner = await DownloadCoordinator.OpenAsync(folder);
                var id = owner.Add(Url(mode), Path.Combine(folder, "result.bin")).Id;
                owner.Resume(id);
                await Until(() => owner.Get(id).Received > 131072);
                await owner.PauseAsync(id);
                owner.Resume(id);
                await owner.WaitForIdleAsync(id);
                Check(owner.Get(id).RestartRequired && !File.Exists(owner.Get(id).Destination), "Unvalidated bytes reused.");
                owner.Restart(id);
                await owner.WaitForIdleAsync(id);
                await VerifyCompleted(owner.Get(id), Size);
            });

        foreach (var damage in new[] { "missing", "short", "corrupt", "tail" })
            await run($"Recovery handles {damage} partial data", async () =>
            {
                var folder = Folder();
                StoredDownload saved;
                await using (var owner = await DownloadCoordinator.OpenAsync(folder))
                {
                    var id = owner.Add(Url(), Path.Combine(folder, "result.bin")).Id;
                    owner.Resume(id);
                    await Until(() => owner.Get(id).Received > 131072);
                    await owner.PauseAsync(id);
                    saved = owner.Get(id);
                }
                if (damage == "missing") File.Delete(saved.PartPath);
                else
                {
                    using var file = File.Open(saved.PartPath, FileMode.Open, FileAccess.Write, FileShare.None);
                    if (damage == "short") file.SetLength(saved.Committed - 1);
                    if (damage == "corrupt") { file.Position = saved.Committed / 2; file.WriteByte(0xAA); }
                    if (damage == "tail") { file.Position = file.Length; file.Write(new byte[3000]); }
                }
                await using var recovered = await DownloadCoordinator.OpenAsync(folder);
                var task = recovered.Get(saved.Id);
                if (damage == "tail")
                {
                    Check(!task.RestartRequired && new FileInfo(task.PartPath).Length == task.Committed, "Uncommitted tail was trusted.");
                    recovered.Resume(task.Id);
                    await recovered.WaitForIdleAsync(task.Id);
                    await VerifyCompleted(recovered.Get(task.Id), Size);
                }
                else Check(task.RestartRequired && task.State == DownloadState.Failed, "Damaged committed data was trusted.");
            });

        await run("Durable cancellation, multiple tasks and exclusive store ownership", async () =>
        {
            var folder = Folder();
            await using var owner = await DownloadCoordinator.OpenAsync(folder);
            var a = owner.Add(Url(), Path.Combine(folder, "a.bin"));
            var b = owner.Add(Url(size: 100003), Path.Combine(folder, "b.bin"));
            owner.Resume(a.Id); owner.Resume(b.Id);
            await Until(() => owner.Get(a.Id).Received > 131072);
            await owner.PauseAsync(a.Id, true);
            await owner.WaitForIdleAsync(b.Id);
            Check(owner.Get(a.Id).State == DownloadState.Cancelled && File.Exists(a.PartPath), "Cancel lost partial/state.");
            await VerifyCompleted(owner.Get(b.Id), 100003);
            try { await using var duplicate = await DownloadCoordinator.OpenAsync(folder); }
            catch (IOException) { return; }
            throw new InvalidOperationException("A second database owner was allowed.");
        });

        await run("Pause before headers and immediate Resume", async () =>
        {
            var folder = Folder();
            await using var owner = await DownloadCoordinator.OpenAsync(folder);
            var task = owner.Add(new(server.BaseAddress, "headers-delay?size=1000"), Path.Combine(folder, "result.bin"));
            owner.Resume(task.Id);
            await Task.Delay(100);
            await owner.PauseAsync(task.Id);
            Check(owner.Get(task.Id).Committed == 0, "Unexpected header-stage progress.");
            owner.Resume(task.Id);
            await owner.WaitForIdleAsync(task.Id);
            await VerifyCompleted(owner.Get(task.Id), 1000);
        });

        await run("Durable destination collision preserves complete part for retry", async () =>
        {
            var folder = Folder();
            await using var owner = await DownloadCoordinator.OpenAsync(folder);
            var task = owner.Add(Url(size: 100003), Path.Combine(folder, "result.bin"));
            owner.Boundary = (point, _) => { if (point == "BeforeRename") File.WriteAllText(task.Destination, "keep me"); };
            owner.Resume(task.Id);
            await owner.WaitForIdleAsync(task.Id);
            Check(owner.Get(task.Id).State == DownloadState.Failed && await File.ReadAllTextAsync(task.Destination) == "keep me", "Collision was not preserved.");
            Check(File.Exists(task.PartPath), "Only completed copy was lost.");
            owner.Boundary = null;
            File.Delete(task.Destination);
            owner.Resume(task.Id);
            await owner.WaitForIdleAsync(task.Id);
            await VerifyCompleted(owner.Get(task.Id), 100003);
        });

        await run("Publication journal survives a post-rename failure and Resume", async () =>
        {
            var folder = Folder();
            await using var owner = await DownloadCoordinator.OpenAsync(folder);
            var task = owner.Add(Url(size: 100003), Path.Combine(folder, "result.bin"));
            owner.Boundary = (point, _) => { if (point == "AfterRename") throw new IOException("Injected completion-commit interruption."); };
            owner.Resume(task.Id);
            await owner.WaitForIdleAsync(task.Id);
            Check(owner.Get(task.Id).PublicationPending, "Publication journal was cleared before completion.");
            owner.Boundary = null;
            owner.Resume(task.Id);
            await owner.WaitForIdleAsync(task.Id);
            await VerifyCompleted(owner.Get(task.Id), 100003);
        });

        await run("Schema initialization is transactional and newer versions are preserved", async () =>
        {
            var folder = Folder();
            await using (var owner = await DownloadCoordinator.OpenAsync(folder))
                owner.Add(Url(), Path.Combine(folder, "result.bin"));
            using (var connection = new SqliteConnection($"Data Source={Path.Combine(folder, "tasks.db")};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version=99";
                command.ExecuteNonQuery();
            }
            try { await using var rejected = await DownloadCoordinator.OpenAsync(folder); }
            catch (InvalidDataException)
            {
                using var connection = new SqliteConnection($"Data Source={Path.Combine(folder, "tasks.db")};Pooling=False");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM tasks";
                Check(Convert.ToInt32(command.ExecuteScalar()) == 1, "Newer database was modified.");
                command.CommandText = "PRAGMA user_version=0";
                command.ExecuteNonQuery();
            }
            await using var migrated = await DownloadCoordinator.OpenAsync(folder);
            Check(migrated.GetTasks().Length == 1, "Schema initialization lost an existing task.");
        });

        foreach (var boundary in new[] { "BeforeFlush", "AfterFlush", "AfterCommit", "BeforeRename", "AfterRename", "AfterRenamePausing" })
            await run($"Killed process recovery at {boundary}", async () =>
            {
                var folder = Folder();
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
                foreach (var argument in new[] { "--crash-worker", folder, boundary, Url().AbsoluteUri }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    await Until(() => File.Exists(Path.Combine(folder, "boundary.ready")) || process.HasExited);
                    if (process.HasExited) throw new InvalidOperationException($"Crash worker failed: {await stdout} {await stderr}");
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
                await using var owner = await DownloadCoordinator.OpenAsync(folder);
                var task = owner.GetTasks().Single();
                Check(!task.RestartRequired, $"Recovery failed: {task.Message}");
                if (boundary.StartsWith("AfterRename")) Check(task.State == DownloadState.Completed, "Published output was not reconciled.");
                else
                {
                    Check(task.State == DownloadState.Paused, "Interrupted task auto-started or lost state.");
                    Check(new FileInfo(task.PartPath).Length == task.Committed, "Uncommitted bytes were not truncated.");
                    if (boundary is "BeforeFlush" or "AfterFlush") Check(task.Committed == 0, "Data was promoted before metadata commit.");
                    if (boundary == "AfterCommit") Check(task.Committed > 0, "Committed data was lost.");
                    owner.Resume(task.Id);
                    await owner.WaitForIdleAsync(task.Id);
                }
                await VerifyCompleted(owner.Get(task.Id), Size);
            });
    }

    public static async Task CrashWorkerAsync(string folder, string boundary, string url)
    {
        await using var owner = await DownloadCoordinator.OpenAsync(folder, new() { SegmentationThreshold = 1048576 });
        int triggered = 0;
        owner.Boundary = (point, task) =>
        {
            if (point != (boundary == "AfterRenamePausing" ? "AfterRename" : boundary)) return;
            if (Interlocked.CompareExchange(ref triggered, 1, 0) != 0) return;
            if (boundary == "AfterRenamePausing") _ = owner.PauseAsync(task.Id);
            File.WriteAllText(Path.Combine(folder, "boundary.ready"), point);
            Thread.Sleep(Timeout.Infinite);
        };
        var task = owner.Add(new(url), Path.Combine(folder, "result.bin"));
        owner.Resume(task.Id);
        await owner.WaitForIdleAsync(task.Id);
        throw new InvalidOperationException("Worker did not reach the requested interruption boundary.");
    }

    private static async Task VerifyCompleted(StoredDownload task, long size)
    {
        Check(task.State == DownloadState.Completed, $"Download did not complete: {task.State}: {task.Message}");
        await using var file = File.OpenRead(task.Destination);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
        Check(file.Length == size && hash == Payload.ExpectedHash(size) && task.Sha256 == hash, "Completed bytes/digest differ.");
        Check(!File.Exists(task.PartPath), "Publication left a part file.");
    }
    private static async Task Until(Func<bool> predicate)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException("Durable task timed out.");
            await Task.Delay(15);
        }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
