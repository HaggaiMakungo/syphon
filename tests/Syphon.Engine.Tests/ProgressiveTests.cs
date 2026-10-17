using System.Text.Json;
using Syphon.Engine;
using Syphon.TestServer;

internal static class ProgressiveTests
{
    public static async Task<int> StandaloneAsync(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "Syphon.Progressive.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var results = new List<object>(); int failed = 0;
        await using var server = await FixtureServer.StartAsync();
        try { await RunAsync(root, server, async (name, action) => {
            try { await action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex); }
        }); } finally {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Syphon.Progressive.Tests")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid cleanup path.");
            Directory.Delete(root, true);
        }
        var index = Array.IndexOf(args,"--report"); if (index >= 0 && index + 1 < args.Length) {
            var report = Path.GetFullPath(args[index+1]); Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { failed, results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return failed == 0 ? 0 : 1;
    }

    public static async Task RunAsync(string root, FixtureServer server, Func<string,Func<Task>,Task> run)
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await run("Bulk history clearing preserves files, receipts and paused jobs across reopen", async () => {
            var folder=Path.Combine(root,Guid.NewGuid().ToString("N"));var destination=Path.Combine(folder,"kept.bin");Guid doneId,pausedId;var receipt=Guid.NewGuid();
            await using(var owner=await DownloadCoordinator.OpenAsync(folder)) {
                var done=owner.AcceptBrowserDownload(receipt,new Uri(server.BaseAddress,"file?size=1234"),folder);doneId=done.TaskId;
                await owner.WaitForIdleAsync(doneId);Check(owner.Get(doneId).State==Syphon.Core.DownloadState.Completed,"Fixture failed.");destination=owner.Get(doneId).Destination;
                var paused=owner.AcceptBrowserDownload(Guid.NewGuid(),new Uri(server.BaseAddress,"file?size=2345"),folder,startImmediately:false);pausedId=paused.TaskId;
                try { owner.ClearHistory([doneId,pausedId]);throw new Exception("Paused record was cleared."); } catch(InvalidOperationException) { }
                Check(owner.Get(doneId).State==Syphon.Core.DownloadState.Completed,"Partial clearing occurred on invalid selection.");
                Check(owner.ClearHistory([doneId,doneId])==1,"Selection was not deduplicated.");
                Check(File.Exists(destination)&&owner.BrowserStatus(receipt)!.Removed,"File deleted or receipt forgotten.");
            }
            await using var reopened=await DownloadCoordinator.OpenAsync(folder);
            Check(reopened.GetTasks().Count()==1&&reopened.Get(pausedId).State==Syphon.Core.DownloadState.Paused&&File.Exists(destination),"Clear did not persist safely.");
        });
        await run("HLS master advertises height tiers and captions without fetching child playlists", () => {
            var info = MediaTools.ParseManifestMetadata("#EXTM3U\n#EXT-X-MEDIA:TYPE=CLOSED-CAPTIONS,GROUP-ID=\"cc\",NAME=\"English CC\",INSTREAM-ID=\"CC1\"\n#EXT-X-STREAM-INF:BANDWIDTH=1000000,RESOLUTION=1280x720\nhttps://unreachable.invalid/720.m3u8\n#EXT-X-STREAM-INF:RESOLUTION=1920x1080,BANDWIDTH=2000000\nhttps://unreachable.invalid/1080.m3u8\n")!;
            Check(info.Qualities.Select(q=>q.QualityHeight).SequenceEqual(new int?[]{1080,720}),"Incorrect tiers.");
            Check(info.Qualities.All(q=>q.ResolveAtDownload) && info.Captions?.Single().Label == "English CC", "Stream IDs were invented or captions lost."); return Task.CompletedTask;
        });
        await run("DASH metadata inherits adaptation attributes and detects text captions", () => {
            var info = MediaTools.ParseManifestMetadata("<MPD type='static' mediaPresentationDuration='PT60S'><Period><AdaptationSet mimeType='video/mp4' codecs='avc1'><Representation width='1920' height='1080' bandwidth='1000000'/><Representation width='1280' height='720'/></AdaptationSet><AdaptationSet mimeType='audio/mp4'><Representation/></AdaptationSet><AdaptationSet contentType='text' lang='en'><Representation codecs='stpp'/></AdaptationSet></Period></MPD>")!;
            Check(info.Qualities.Length==2 && info.Duration==60 && info.HasAudio && info.Captions?.Single().Label=="en", "Missing DASH metadata."); return Task.CompletedTask;
        });
        await run("Protected/live metadata and XML entities are rejected before fetching payloads", () => {
            foreach (var manifest in new[]{"#EXTM3U\n#EXT-X-SESSION-KEY:METHOD=AES-128,URI=\"https://unreachable.invalid/key\"", "<MPD type='dynamic'/>", "<MPD><Period><ContentProtection/></Period></MPD>", "<!DOCTYPE x [<!ENTITY leak SYSTEM 'file:///C:/private'>]><MPD>&leak;</MPD>"}) {
                try { MediaTools.ParseManifestMetadata(manifest); throw new InvalidOperationException("Unsafe manifest accepted."); }
                catch(Exception ex) when(ex is IOException or System.Xml.XmlException) { }
            } return Task.CompletedTask;
        });
        await run("Advertised quality persists and receipt retry cannot silently select another tier", async () => {
            var folder=Path.Combine(root,Guid.NewGuid().ToString("N"));Guid taskId;var request=Guid.NewGuid();var url=new Uri(server.BaseAddress,"master.m3u8");
            await using(var owner=await DownloadCoordinator.OpenAsync(folder)) {
                var task=owner.AcceptBrowserDownload(request,url,folder,"mp4",startImmediately:false,qualityHeight:720);taskId=task.TaskId;
                Check(task.Task!.QualityHeight==720 && task.Task.VideoStream is null,"Selection lost.");
                Check(owner.AcceptBrowserDownload(request,url,folder,"mp4",startImmediately:false,qualityHeight:720).Duplicate,"Retry duplicated task.");
                try { owner.AcceptBrowserDownload(request,url,folder,"mp4",startImmediately:false,qualityHeight:1080);throw new Exception("Changed tier accepted."); } catch(InvalidDataException) { }
            }
            await using var reopened=await DownloadCoordinator.OpenAsync(folder);Check(reopened.Get(taskId).QualityHeight==720,"Selection did not survive reopening.");
        });
        await run("YouTube caption URLs remain direct VTT downloads, not video extraction jobs", async () => {
            var url=new Uri("https://www.youtube.com/api/timedtext?v=YE7VzlLtp-4&lang=en&fmt=vtt");
            Check(YoutubeTools.IsCaptionUrl(url)&&!YoutubeTools.IsYoutubeVideoTarget(url),"Caption misclassified as video.");
            var folder=Path.Combine(root,Guid.NewGuid().ToString("N"));await using var owner=await DownloadCoordinator.OpenAsync(folder);
            var accepted=owner.AcceptBrowserDownload(Guid.NewGuid(),url,folder,startImmediately:false);
            Check(accepted.Task!.MediaFormat is null && accepted.Task.Filename=="captions-en.vtt" && accepted.Task.Url==url.AbsoluteUri,"Caption URL or filename was changed into a video.");
        });
        await run("Headers-only previews and unavailable previews never require a download body", async () => {
            var info=await DownloadPreviewReader.ReadAsync(new(server.BaseAddress,"preview-head"));Check(info.Bytes==12345,"HEAD size missing.");
            var unavailable=await DownloadPreviewReader.ReadAsync(new(server.BaseAddress,"status/503"));Check(unavailable.Bytes is null && unavailable.Message.Contains("still queue"),"Preview failure became blocking.");
            var media=await DownloadPreviewReader.ReadAsync(new("https://www.youtube.com/watch?v=YE7VzlLtp-4"));Check(media.Bytes is null && media.Message.Contains("when downloading"),"Media preview performed extraction.");
        });
    }
}
