using System.Diagnostics;
using Syphon.Engine;
using Syphon.TestServer;

internal static class ManifestValidationTests
{
    public static async Task RunAsync(FixtureServer server, Func<string, Func<Task>, Task> run)
    {
        await run("On-demand manifests finish validation normally", () =>
            MediaTools.ValidateManifestAsync(new Uri(server.BaseAddress, "manifest.m3u8")));

        await run("Media conversion bounds stalled manifest bodies before launching tools", async () =>
        {
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var clock = Stopwatch.StartNew();
            try
            {
                await MediaTools.ConvertAsync(new StoredDownload {
                    Url = new Uri(server.BaseAddress, "manifest.m3u8?stall=1").AbsoluteUri,
                    MediaFormat = "mp4", VideoStream = 0, Destination = "unused.mp4", PartPath = "unused.syphon-part"
                }, _ => { }, watchdog.Token);
                throw new InvalidOperationException("Stalled manifest unexpectedly completed.");
            }
            catch (IOException ex) when (ex.Message.Contains("manifest in time"))
            {
                if (watchdog.IsCancellationRequested || clock.Elapsed > TimeSpan.FromSeconds(18))
                    throw new InvalidOperationException("Manifest deadline did not bound conversion.", ex);
            }
        });

        await run("User cancellation interrupts manifest validation without a timeout error", async () =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            try
            {
                await MediaTools.ValidateManifestAsync(new Uri(server.BaseAddress, "manifest.m3u8?stall=1"), cancellation.Token);
                throw new InvalidOperationException("Manifest ignored cancellation.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        });
    }
}
