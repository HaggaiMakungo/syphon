using System.Security.Cryptography;
using Syphon.Core;
using Syphon.Engine;

internal static class InternetChecks
{
    // Opt-in only. Small public text resources; no installers, credentials, or personal files.
    public static async Task RunAsync(string root, Func<string, Func<Task>, Task> run)
    {
        foreach (var url in new[]
        {
            "https://www.python.org/robots.txt",
            "https://raw.githubusercontent.com/dotnet/runtime/main/LICENSE.TXT",
            "https://github.com/dotnet/runtime/raw/refs/heads/main/LICENSE.TXT",
            "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json"
        })
            await run($"Public HTTPS compatibility: {url}", async () =>
            {
                var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
                using var reference = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                reference.DefaultRequestHeaders.UserAgent.ParseAdd("Syphon-CompatibilityCheck/0.1");
                using var response = await reference.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                await response.Content.LoadIntoBufferAsync(1024 * 1024);
                var expected = Convert.ToHexStringLower(SHA256.HashData(await response.Content.ReadAsByteArrayAsync()));
                await using var owner = await DownloadCoordinator.OpenAsync(folder, new() { MaximumRetries = 1 });
                var task = owner.Add(new(url), Path.Combine(folder, "public-text.txt"));
                owner.Resume(task.Id); await owner.WaitForIdleAsync(task.Id);
                var result = owner.Get(task.Id);
                if (result.State != DownloadState.Completed || result.Sha256 != expected)
                    throw new InvalidOperationException($"HTTPS output differed or failed: {result.Message}");
            });

        await run("Public HTTPS range continuation from a verified checkpoint", async () =>
        {
            const string url = "https://raw.githubusercontent.com/dotnet/runtime/main/LICENSE.TXT";
            var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            using var reference = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await reference.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(1024 * 1024);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var etag = response.Headers.ETag;
            if (etag is null || etag.IsWeak || bytes.Length < 512) throw new InvalidOperationException("Public fixture no longer provides a strong validator and sufficient data.");
            var prefix = bytes[..256];
            var snapshot = new StoredDownload
            {
                Url = url, EffectiveUrl = response.RequestMessage!.RequestUri!.AbsoluteUri, ETag = etag.Tag,
                Destination = Path.Combine(folder, "license.txt"), PartPath = Path.Combine(folder, "license.syphon-part"),
                Committed = prefix.Length, Received = prefix.Length, Total = bytes.Length, State = DownloadState.Paused,
                Blocks = [new(0, prefix.Length, Convert.ToHexStringLower(SHA256.HashData(prefix)))]
            };
            using (var part = new FileStream(snapshot.PartPath, FileMode.CreateNew)) { part.Write(prefix); part.Flush(true); }
            using (var store = new DownloadStore(folder)) store.Save(snapshot);
            await using var owner = await DownloadCoordinator.OpenAsync(folder, new() { MaximumRetries = 1 });
            owner.Resume(snapshot.Id); await owner.WaitForIdleAsync(snapshot.Id);
            var completed = owner.Get(snapshot.Id);
            if (completed.State != DownloadState.Completed || completed.Sha256 != Convert.ToHexStringLower(SHA256.HashData(bytes)))
                throw new InvalidOperationException($"Public range continuation failed: {completed.Message}");
        });
    }
}
