using System.Security.Cryptography;
using Syphon.Core;
using Syphon.Engine;
using Syphon.TestServer;

internal static class SiteCompatibilityTests
{
    public static async Task RunAsync(string root, FixtureServer server, Func<string, Func<Task>, Task> run)
    {
        string Folder() { var p = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
        Uri Url(string route) => new(server.BaseAddress, route);
        var limits = new TransferOptions { SegmentationThreshold = 1048576, MaximumConnectionsPerFile = 2, MaximumRetries = 0 };
        DownloadRequestContext Context(Uri url) => new(url.AbsoluteUri, Url("source-page").AbsoluteUri, "FixtureBrowser/1.0",
            [new("SyphonFixtureSession", "fixture-secret", url.Host)]);
        static void Check(bool v, string m) { if (!v) throw new InvalidOperationException(m); }
        async Task Until(Func<bool> predicate) { var end = DateTime.UtcNow.AddSeconds(20); while (!predicate()) { if(DateTime.UtcNow > end) throw new TimeoutException("Compatibility fixture timed out."); await Task.Delay(10); } }
        async Task Verify(StoredDownload task, long length) {
            Check(task.State == DownloadState.Completed, "Download failed: " + task.Message);
            await using var file = File.OpenRead(task.Destination);
            Check(file.Length == length && Convert.ToHexStringLower(await SHA256.HashDataAsync(file)) == Payload.ExpectedHash(length), "Saved file bytes differ.");
        }

        await run("Browser context authenticates segmented files without writing cookies to disk", async () => {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, limits);
            var url = Url("segments?size=8388608&auth=1&referer=1");
            var accepted = owner.AcceptBrowserDownload(Guid.NewGuid(), url, folder, context: Context(url));
            await owner.WaitForIdleAsync(accepted.TaskId); await Verify(owner.Get(accepted.TaskId), 8388608);
            Check(owner.Get(accepted.TaskId).Segments.Length == 2, "Signed-in segmented transfer did not run.");
            foreach (var path in Directory.GetFiles(folder, "tasks.db*")) {
                await using var input = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                using var buffer = new MemoryStream(); await input.CopyToAsync(buffer);
                Check(!System.Text.Encoding.UTF8.GetString(buffer.ToArray()).Contains("fixture-secret"), "Cookie was persisted in SQLite.");
            }
        });

        await run("Sign-in web pages are not published as authenticated binary downloads", async () => {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder,limits);
            var url = Url("compat-login");
            var task = owner.AcceptBrowserDownload(Guid.NewGuid(),url,folder,filename:"file.zip",context:Context(url));
            await owner.WaitForIdleAsync(task.TaskId);
            Check(owner.Get(task.TaskId).State == DownloadState.Failed && !File.Exists(owner.Get(task.TaskId).Destination), "Sign-in HTML was saved as a completed archive.");
        });

        await run("Browser context is lost on reopen and a fresh gesture resumes the same job", async () => {
            var folder = Folder(); Guid id; var url = Url("segments?size=8388608&auth=1");
            await using(var owner = await DownloadCoordinator.OpenAsync(folder, limits)) {
                id = owner.AcceptBrowserDownload(Guid.NewGuid(), url, folder, context: Context(url)).TaskId;
                await Until(()=>owner.Get(id).Committed > 1048576); await owner.PauseAsync(id);
            }
            await using var restored = await DownloadCoordinator.OpenAsync(folder, limits);
            try { restored.Resume(id); throw new Exception("Missing browser context was silently ignored."); } catch(InvalidOperationException) { }
            var result = restored.AcceptBrowserDownload(Guid.NewGuid(), url, folder, context: Context(url));
            Check(result.TaskId == id && restored.GetTasks().Length == 1, "Fresh sign-in created a duplicate job.");
            await restored.WaitForIdleAsync(id); await Verify(restored.Get(id), 8388608);
        });

        await run("Browser cookies respect path, expiry, secure flag and redirect origin", async () => {
            var url = Url("compat-echo");
            var context = Context(url) with { Cookies = [new("private", "secret-path", url.Host, "/private"), new("expired", "secret-old", url.Host, Expires: 1), new("secure", "secret-tls", url.Host, Secure:true)] };
            context.Validate(url.AbsoluteUri); Check(context.CookieHeader(url) == "", "Cookie scope filtering failed.");
            await using var other = await FixtureServer.StartAsync();
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, limits);
            var redirect = Url("compat-redirect?target=" + Uri.EscapeDataString(new Uri(other.BaseAddress,"compat-echo").AbsoluteUri));
            var task = owner.AcceptBrowserDownload(Guid.NewGuid(), redirect, folder, context: Context(redirect));
            await owner.WaitForIdleAsync(task.TaskId);
            var body = await File.ReadAllTextAsync(owner.Get(task.TaskId).Destination);
            Check(!body.Contains("fixture-secret") && body == "cookie=\nreferer=", "Browser context leaked across an origin redirect.");
            try { (context with { Cookies = [new("x", "y", "unrelated.example")] }).Validate(url.AbsoluteUri); throw new Exception("Unrelated cookie domain accepted."); } catch(ArgumentException) { }
        });

        foreach(var segmented in new[] { false, true })
            await run($"Renewed link validates saved {(segmented ? "segments" : "prefix")} and completes exact bytes", async () => {
                var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, limits);
                var old = Url("segments?size=8388608&token=old");
                var task = owner.Add(old, Path.Combine(folder,"renew.bin"), singleConnection: !segmented);
                owner.Resume(task.Id); await Until(()=>owner.Get(task.Id).Committed > 1048576); await owner.PauseAsync(task.Id);
                var committed = owner.Get(task.Id).Committed;
                var fresh = Url("segments?size=8388608&token=new"); await owner.ReplaceLinkAsync(task.Id,fresh);
                Check(owner.Get(task.Id).Committed == committed && owner.Get(task.Id).Url == fresh.AbsoluteUri, "Renewal discarded saved progress.");
                owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id); await Verify(owner.Get(task.Id),8388608);
            });

        await run("Renewed link rejects changed bytes even with the same strong ETag and size", async () => {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, limits);
            var task = owner.Add(Url("segments?size=8388608"),Path.Combine(folder,"reject.bin"));
            owner.Resume(task.Id); await Until(()=>owner.Get(task.Id).Committed>1048576); await owner.PauseAsync(task.Id);
            var prior = owner.Get(task.Id);
            try { await owner.ReplaceLinkAsync(task.Id,Url("segments?size=8388608&mode=sameetag-different")); throw new Exception("Mismatched bytes were accepted."); } catch(InvalidDataException) { }
            Check(owner.Get(task.Id).Url == prior.Url && owner.Get(task.Id).Committed == prior.Committed && !File.Exists(task.Destination), "Rejected renewal mutated the task.");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await owner.ReplaceLinkAsync(task.Id,Url("segments?size=8388608&token=new"),cancellation.Token); throw new Exception("Cancelled renewal succeeded."); } catch(OperationCanceledException) { }
            Check(owner.Get(task.Id).Url == prior.Url, "Cancelled renewal replaced the URL.");
        });

        await run("Expired untouched link can be replaced without fabricating a completed task", async () => {
            var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder, limits);
            var task = owner.Add(Url("segments?size=100003&expired=1"),Path.Combine(folder,"expired.bin")); owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
            Check(owner.Get(task.Id).State == DownloadState.Failed && owner.Get(task.Id).Committed == 0, "Expired link did not fail cleanly.");
            await owner.ReplaceLinkAsync(task.Id,Url("segments?size=100003&token=fresh"));
            Check(owner.Get(task.Id).State == DownloadState.Paused, "Replacement unexpectedly started a request worker.");
            owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id); await Verify(owner.Get(task.Id),100003);
        });
    }
}
