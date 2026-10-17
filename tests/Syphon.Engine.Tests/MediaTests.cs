using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Syphon.Core;
using Syphon.Engine;

internal static class MediaTests
{
    public static async Task<int> RunAsync(string[] args)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Syphon.Media.Tests", Guid.NewGuid().ToString("N"));
        var assets = Path.Combine(folder, "assets"); Directory.CreateDirectory(assets);
        var results = new List<object>(); var failed = 0; int keyRequests = 0, slowRequests = 0; bool slow = true;
        MediaTools.TestToolError = text => Console.WriteLine("Fixture media tool: " + text);
        async Task Run(string name, Func<Task> action)
        {
            try { await action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine($"FAIL {name}: {ex}"); }
        }
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        async Task Tool(string tool, params string[] arguments)
        {
            var info = new ProcessStartInfo(MediaTools.FindTool(tool)) { WorkingDirectory = assets, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); await output;
            if (process.ExitCode != 0) throw new InvalidOperationException(await error);
        }
        async Task<JsonDocument> Probe(string path)
        {
            var info = new ProcessStartInfo(MediaTools.FindTool("ffprobe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "-v", "error", "-show_streams", "-of", "json", path }) info.ArgumentList.Add(a);
            using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (process.ExitCode != 0) throw new InvalidOperationException(await error);
            return JsonDocument.Parse(await output);
        }
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapGet("/{**file}", async context =>
        {
            var route = context.Request.RouteValues["file"]?.ToString() ?? "";
            if (route.StartsWith("paced/", StringComparison.Ordinal)) {
                route = route[6..];
                if (route.EndsWith(".ts", StringComparison.Ordinal)) await Task.Delay(200, context.RequestAborted);
            }
            if (route == "key") { Interlocked.Increment(ref keyRequests); context.Response.StatusCode = 403; return; }
            var delayed = route.StartsWith("slow/", StringComparison.Ordinal);
            if (delayed) route = route[5..];
            var path = Path.GetFullPath(Path.Combine(assets, route));
            if (!path.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) { Console.WriteLine("Fixture missing route: " + route + " available: " + string.Join(",", Directory.GetFiles(assets).Select(Path.GetFileName))); context.Response.StatusCode = 404; return; }
            var type = Path.GetExtension(path) switch { ".mpd" => "application/dash+xml", ".m3u8" => "application/vnd.apple.mpegurl", ".ts" => "video/mp2t", _ => "video/mp4" };
            if (delayed && slow)
            {
                Interlocked.Increment(ref slowRequests); context.Response.ContentType = type; context.Response.ContentLength = new FileInfo(path).Length;
                await context.Response.StartAsync();
                await Task.Delay(5000, context.RequestAborted);
                await context.Response.SendFileAsync(path, context.RequestAborted);
            }
            else await Results.File(path, type, enableRangeProcessing: true).ExecuteAsync(context);
        });
        try
        {
            foreach (var (name, size) in new[] { ("low", "320x180"), ("high", "640x360") })
            {
                await Tool("ffmpeg", "-nostdin", "-v", "error", "-f", "lavfi", "-i", $"testsrc2=size={size}:rate=10", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", "-g", "10", "-c:a", "aac", Path.Combine(assets, name + ".mp4"));
                await Tool("ffmpeg", "-nostdin", "-v", "error", "-i", Path.Combine(assets, name + ".mp4"), "-c", "copy", "-hls_time", "1", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(assets, name + "_%d.ts"), "-f", "hls", Path.Combine(assets, name + ".m3u8"));
            }
            await File.WriteAllTextAsync(Path.Combine(assets, "master.m3u8"), "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=320x180\nlow.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=600000,RESOLUTION=640x360\nhigh.m3u8\n");
            await Tool("ffmpeg", "-nostdin", "-v", "error", "-i", Path.Combine(assets, "low.mp4"), "-i", Path.Combine(assets, "high.mp4"), "-map", "0:v", "-map", "1:v", "-map", "1:a", "-c", "copy", "-seg_duration", "1", "-adaptation_sets", "id=0,streams=v id=1,streams=a", "-f", "dash", Path.Combine(assets, "stream.mpd"));
            await File.WriteAllTextAsync(Path.Combine(assets, "protected.mpd"), "<MPD xmlns='urn:mpeg:dash:schema:mpd:2011'><Period><AdaptationSet><ContentProtection schemeIdUri='urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed'/></AdaptationSet></Period></MPD>");
            await File.WriteAllTextAsync(Path.Combine(assets, "encrypted.m3u8"), "#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"key\"\n#EXTINF:1,\nlow_0.ts\n#EXT-X-ENDLIST\n");
            await File.WriteAllTextAsync(Path.Combine(assets, "live.m3u8"), "#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1,\nlow_0.ts\n");
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            Uri Url(string name) => new(address + "/" + name);
            string Store() => Path.Combine(folder, Guid.NewGuid().ToString("N"));
            var downloads = Path.Combine(folder, "downloads");
            await Run("Media: incomplete and inconsistent duration metadata remains unknown", async () => {
                await File.WriteAllTextAsync(Path.Combine(assets, "invalid-duration.m3u8"), "#EXTM3U\n#EXTINF:NaN,\nlow_0.ts\n#EXT-X-ENDLIST\n");
                await File.WriteAllTextAsync(Path.Combine(assets, "different-duration.m3u8"), "#EXTM3U\n#EXTINF:40,\nlow_0.ts\n#EXT-X-ENDLIST\n");
                await File.WriteAllTextAsync(Path.Combine(assets, "mixed-duration.m3u8"), "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=200000\nlow.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=600000\ndifferent-duration.m3u8\n");
                Check(await MediaTools.InspectDurationAsync(Url("invalid-duration.m3u8")) is null, "Invalid segment duration accepted.");
                Check(await MediaTools.InspectDurationAsync(Url("mixed-duration.m3u8")) is null, "Inconsistent variant durations accepted.");
                await File.WriteAllTextAsync(Path.Combine(assets, "overflow-duration.mpd"), "<MPD mediaPresentationDuration='P999999999999999999999999999999D'><Period/></MPD>");
                Check(await MediaTools.InspectDurationAsync(Url("overflow-duration.mpd")) is null, "Overflowing DASH duration accepted.");
            });
            await Run("Media: playback estimation guards and refinement", () => {
                Check(DownloadCoordinator.EstimateFromPlayback(100, 1048576, 10) == 10485760, "Playback extrapolation is incorrect.");
                Check(DownloadCoordinator.EstimateFromPlayback(100, 3145728, 20) == 15728640, "A later bitrate sample did not refine the estimate.");
                foreach (var duration in new double?[] { null, 0, double.NaN, double.PositiveInfinity })
                    Check(DownloadCoordinator.EstimateFromPlayback(duration, 1048576, 10) is null, "Invalid duration accepted.");
                foreach (var seconds in new[] { 0d, 9, 100, 101, double.NaN, double.PositiveInfinity })
                    Check(DownloadCoordinator.EstimateFromPlayback(100, 1048576, seconds) is null, "Unusable playback sample accepted.");
                Check(DownloadCoordinator.EstimateFromPlayback(100, 262143, 10) is null && DownloadCoordinator.EstimateFromPlayback(double.MaxValue, 1048576, 10) is null, "Small/overflowing estimate accepted.");
                return Task.CompletedTask;
            });
            await Run("Media: duration and live playback estimate become exact on completion", async () => {
                await Tool("ffmpeg", "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=10", "-t", "40", "-c:v", "libx264", "-preset", "ultrafast", "-g", "20", "-hls_time", "2", "-hls_list_size", "0", "-hls_segment_filename", Path.Combine(assets, "long_%d.ts"), "-f", "hls", Path.Combine(assets, "long.m3u8"));
                var duration = await MediaTools.InspectDurationAsync(Url("long.m3u8"));
                Check(duration == 40, "VOD playlist duration was not recovered without probing segments.");
                var store = Store(); Guid id;
                await using (var owner = await DownloadCoordinator.OpenAsync(store)) {
                    id = owner.AcceptBrowserDownload(Guid.NewGuid(), Url("paced/long.m3u8"), downloads, "mp4", 0, null).TaskId;
                    var idle = owner.WaitForIdleAsync(id); var estimated = false;
                    while (!idle.IsCompleted) {
                        var current = owner.Get(id);
                        if (current.EstimatedTotal is > 0) {
                            Check(current.Total is null && current.MediaDurationSeconds == 40 && current.SizeEstimateSource!.Contains("playback"), "Estimate became an exact total or lost its basis."); estimated = true;
                        }
                        await Task.Delay(25);
                    }
                    await idle;
                    var completed = owner.Get(id);
                    Check(estimated, "No playback-based estimate appeared during conversion.");
                    Check(completed.State == DownloadState.Completed && completed.EstimatedTotal is null && completed.Total == new FileInfo(completed.Destination).Length, "Final exact size did not replace the estimate.");
                }
                await using var reopened = await DownloadCoordinator.OpenAsync(store);
                Check(reopened.Get(id).MediaDurationSeconds == 40, "Duration was not persisted with the completed record.");
            });
            MediaInfo? hls = null, dash = null;
            await Run("Media: direct quality and audio detection", async () => {
                var info = await MediaTools.InspectAsync(Url("high.mp4")); Check(info.Qualities.Single().Height == 360 && info.HasAudio, "Incorrect direct quality.");
                var estimate = info.Qualities.Single().EstimatedBytes;
                Check(estimate is > 0 && Math.Abs(estimate.Value - new FileInfo(Path.Combine(assets, "high.mp4")).Length) < 10000, "Direct media size estimate did not include both streams.");
            });
            await Run("Media: HLS hidden variant quality inspection", async () => {
                hls = await MediaTools.InspectAsync(Url("master.m3u8")); Check(hls.Qualities.Select(q => q.Height).SequenceEqual(new[] { 360, 180 }), "Missing HLS variants.");
            });
            await Run("Media: DASH representation inspection", async () => {
                dash = await MediaTools.InspectAsync(Url("stream.mpd")); Check(dash.Qualities.Select(q => q.Height).SequenceEqual(new[] { 360, 180 }) && dash.HasAudio, "Missing DASH representations.");
            });
            await Run("Media: MP3 conversion, duplicate options, final codec and reopen", async () => {
                var store = Store(); Guid id; var request = Guid.NewGuid();
                await using (var owner = await DownloadCoordinator.OpenAsync(store)) {
                    var accepted = owner.AcceptBrowserDownload(request, Url("high.mp4"), downloads, "mp3"); id = accepted.TaskId;
                    await owner.WaitForIdleAsync(id); var task = owner.Get(id); Check(task.State == DownloadState.Completed, task.Message);
                    using var info = await Probe(task.Destination); Check(info.RootElement.GetProperty("streams").EnumerateArray().All(s => s.GetProperty("codec_name").GetString() == "mp3"), "Output is not MP3 audio.");
                    Check(owner.AcceptBrowserDownload(request, Url("high.mp4"), downloads, "mp3").TaskId == id, "Duplicate created a new conversion.");
                    try { owner.AcceptBrowserDownload(request, Url("high.mp4"), downloads, "mp4"); throw new Exception("Option conflict accepted."); } catch (InvalidDataException) { }
                }
                await using var reopened = await DownloadCoordinator.OpenAsync(store); Check(reopened.Get(id).State == DownloadState.Completed, "Completed media did not reopen.");
            });
            foreach (var kind in new[] { "HLS", "DASH" }) await Run($"Media: selected {kind} quality download, mux and digest", async () => {
                var info = kind == "HLS" ? hls! : dash!; var choice = info.Qualities.Single(q => q.Height == (kind == "HLS" ? 180 : 360));
                await using var owner = await DownloadCoordinator.OpenAsync(Store());
                var accept = owner.AcceptBrowserDownload(Guid.NewGuid(), Url(kind == "HLS" ? "master.m3u8" : "stream.mpd"), downloads, "mp4", qualityHeight: choice.QualityHeight);
                await owner.WaitForIdleAsync(accept.TaskId); var task = owner.Get(accept.TaskId); Check(task.State == DownloadState.Completed, task.Message);
                using var probe = await Probe(task.Destination); var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                Check(streams.Any(s => s.TryGetProperty("height", out var height) && height.GetInt32() == choice.Height) && streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), "Quality/audio selection differs.");
                Check(task.Sha256 is { Length: 64 } && task.Committed == new FileInfo(task.Destination).Length, "Missing committed media digest.");
            });
            await Run("Media: protected, encrypted and live sources rejected without key requests", async () => {
                foreach (var file in new[] { "protected.mpd", "encrypted.m3u8", "live.m3u8" }) {
                    try { await MediaTools.InspectAsync(Url(file)); throw new Exception("Protected/live stream accepted."); }
                    catch (IOException ex) when (ex.Message.Contains("protected", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("Live", StringComparison.Ordinal)) { }
                }
                Check(keyRequests == 0, "Encryption key was requested.");
            });
            await Run("Media: automatic quality selects highest inspected resolution", async () => {
                await using var owner = await DownloadCoordinator.OpenAsync(Store());
                var id = owner.AcceptBrowserDownload(Guid.NewGuid(), Url("master.m3u8"), downloads, "mp4").TaskId;
                await owner.WaitForIdleAsync(id); var task = owner.Get(id); Check(task.State == DownloadState.Completed, task.Message);
                using var info = await Probe(task.Destination);
                Check(info.RootElement.GetProperty("streams").EnumerateArray().Any(s => s.TryGetProperty("height", out var height) && height.GetInt32() == 360), "Automatic selection did not choose the highest quality.");
            });
            await Run("Media: protected download fails without publishing or requesting keys", async () => {
                await using var owner = await DownloadCoordinator.OpenAsync(Store());
                var id = owner.AcceptBrowserDownload(Guid.NewGuid(), Url("encrypted.m3u8"), downloads, "mp4").TaskId;
                await owner.WaitForIdleAsync(id); var task = owner.Get(id);
                Check(task.State == DownloadState.Failed && !File.Exists(task.Destination) && keyRequests == 0, "Protected media was published or a key requested.");
            });
            await Run("Media: process cancellation, paused reopen and restart from beginning", async () => {
                var store = Store(); Guid id;
                await using (var owner = await DownloadCoordinator.OpenAsync(store)) {
                    id = owner.AcceptBrowserDownload(Guid.NewGuid(), Url("slow/high.mp4"), downloads, "mp3").TaskId;
                    var clock = Stopwatch.StartNew();
                    while (Volatile.Read(ref slowRequests) < 2 && clock.Elapsed < TimeSpan.FromSeconds(12)) await Task.Delay(20);
                    Check(slowRequests >= 2, "Media process did not begin reading.");
                    await owner.PauseAsync(id).WaitAsync(TimeSpan.FromSeconds(5)); Check(owner.Get(id).State == DownloadState.Paused, "Media job did not pause.");
                }
                slow = false;
                await using var reopened = await DownloadCoordinator.OpenAsync(store); Check(reopened.Get(id).MediaFormat == "mp3", "Media options were lost.");
                reopened.Resume(id); await reopened.WaitForIdleAsync(id); Check(reopened.Get(id).State == DownloadState.Completed, reopened.Get(id).Message);
            });
        }
        finally { MediaTools.TestToolError = null; await app.StopAsync(); Directory.Delete(folder, true); }
        if (args.Length > 0) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); await File.WriteAllTextAsync(args[0], JsonSerializer.Serialize(new { failed, results }, new JsonSerializerOptions { WriteIndented = true })); }
        Console.WriteLine($"{results.Count - failed}/{results.Count} media checks passed"); return failed == 0 ? 0 : 1;
    }
}
