using System.Security.Cryptography;
using System.Text.Json;
using Syphon.Core;
using Syphon.Engine;

internal static class YoutubeTests
{
    private const string Id = "BaW_jenozKc";
    private const string Url = "https://www.youtube.com/watch?v=" + Id;
    private const string Info = """
        {"title":"A video: title?","live_status":"not_live","duration":10,"formats":[
         {"format_id":"137","height":1080,"width":1920,"vcodec":"avc1","acodec":"none","tbr":1500,"has_drm":false},
         {"format_id":"136","height":720,"width":1280,"vcodec":"avc1","acodec":"none","tbr":900},
         {"format_id":"18","height":360,"width":640,"vcodec":"avc1","acodec":"aac","tbr":400},
         {"format_id":"140","height":null,"vcodec":"none","acodec":"aac","tbr":null},
         {"format_id":"999","height":2160,"vcodec":"avc1","acodec":"none","has_drm":true}]}
        """;
    public static async Task<int> RunAsync(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "Syphon.Youtube.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var results = new List<object>(); int failed = 0;
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        async Task Run(string name, Func<Task> action) { try { await action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine($"FAIL {name}: {ex}"); } }
        try
        {
            await Run("Selected media gains an advisory size while bytes arrive, then publishes an exact total", async () => {
                var reported=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                YoutubeTools.TestRunner=async(command,progress,token)=>{
                    if(command.Contains("--print"))return Info.Replace("\"tbr\":null","\"tbr\":128");
                    progress!(65536);reported.TrySetResult();await finish.Task.WaitAsync(token);await File.WriteAllBytesAsync(command[Array.IndexOf(command,"--output")+1].Replace("%(ext)s","mp4"),[1,2,3,4],token);return "";
                };
                await using var owner=await DownloadCoordinator.OpenAsync(Path.Combine(root,"size-estimate"));var id=owner.AcceptBrowserDownload(Guid.NewGuid(),new(Url),Path.Combine(root,"estimated-download"),"mp4",youtubeFormat:"137").TaskId;
                await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));await Task.Delay(350);var active=owner.Get(id);Check(active.EstimatedTotal is >65536&&active.Total is null&&active.Committed==0,"Advisory media size became an exact total or checkpoint.");
                finish.TrySetResult();await owner.WaitForIdleAsync(id);var done=owner.Get(id);Check(done.State==DownloadState.Completed&&done.Total==4&&done.EstimatedTotal is null,"Final media size did not replace its estimate.");YoutubeTools.TestRunner=null;
            });
            await Run("Damaged history is rejected explicitly without replacing the database", async () => {
                var folder = Path.Combine(root,"damaged-store"); Directory.CreateDirectory(folder);
                var database = Path.Combine(folder,"tasks.db"); var bytes = Enumerable.Repeat((byte)0x6d,4096).ToArray(); await File.WriteAllBytesAsync(database,bytes);
                try { await using var owner = await DownloadCoordinator.OpenAsync(folder); throw new Exception("Damaged database was accepted."); }
                catch (InvalidDataException ex) { Check(ex.Message.Contains("database is damaged"),ex.Message); }
                Check((await File.ReadAllBytesAsync(database)).SequenceEqual(bytes),"Damaged original was modified.");
            });
            await Run("Media diagnostics classify split errors without retaining URLs, cookies or arbitrary tool output", () => {
                foreach (var sample in new[] {
                    ("HTTP Error 403: Forbidden", "http-403"), ("HTTP Error 429: Too Many Requests", "http-429"),
                    ("Sign in to confirm you're not a bot", "account-required"), ("Requested format is not available", "format-unavailable"),
                    ("signature solving failed", "extractor-runtime"), ("Connection timed out", "network"),
                    ("No space left on device", "disk-full"), ("Permission denied", "file-permission"),
                    ("unrecognized failure", "unclassified") }) {
                    var classifier = new MediaErrorClassifier();
                    var output = "https://example.test/?token=secret Cookie: private-value " + sample.Item1;
                    for (var n = 0; n < output.Length; n += 5) classifier.Observe(output.Substring(n, Math.Min(5, output.Length - n)));
                    var error = classifier.Failure("yt-dlp", 1);
                    Check(error.Diagnostic.Contains("Category: " + sample.Item2), error.Diagnostic);
                    Check(!error.Message.Contains("secret") && !error.Message.Contains("private-value") && !error.Diagnostic.Contains("example.test"), "Sensitive raw output was retained.");
                }
                return Task.CompletedTask;
            });
            await Run("Classified YouTube failure survives inspection, coordinator persistence and reopening", async () => {
                var classifier = new MediaErrorClassifier(); classifier.Observe("HTTP Error 403: Forbidden");
                YoutubeTools.TestRunner = (_, _, _) => throw classifier.Failure("yt-dlp", 1);
                var folder = Path.Combine(root, "diagnostics"); Guid id;
                await using (var owner = await DownloadCoordinator.OpenAsync(folder)) {
                    id = owner.AcceptBrowserDownload(Guid.NewGuid(), new(Url), Path.Combine(folder, "downloads"), "mp4").TaskId;
                    await owner.WaitForIdleAsync(id);
                    Check(owner.Get(id).State == DownloadState.Failed && owner.Get(id).Message.Contains("HTTP 403"), owner.Get(id).Message);
                }
                await using var reopened = await DownloadCoordinator.OpenAsync(folder);
                Check(reopened.Get(id).Diagnostic?.Contains("Category: http-403") == true && reopened.Get(id).Received == 0, "Failure diagnostics did not persist.");
                YoutubeTools.TestRunner = null;
            });
            await Run("YouTube URL normalization supports watch, short links, Shorts and embeds", () => {
                foreach (var url in new[] { Url + "&list=PLignored&t=10", "https://youtu.be/" + Id + "?si=tracking", "https://m.youtube.com/shorts/" + Id, "https://www.youtube-nocookie.com/embed/" + Id, "https://music.youtube.com/watch?v=" + Id })
                    Check(YoutubeTools.CanonicalUrl(new(url)).AbsoluteUri == Url, "Canonical URL differs.");
                return Task.CompletedTask;
            });
            await Run("YouTube rejects playlists, channels, credentials, ports and deceptive hosts", () => {
                foreach (var url in new[] { "https://youtube.com/playlist?list=123", "https://youtube.com/@channel", "https://youtube.com.evil.test/watch?v=" + Id, "https://user:secret@youtube.com/watch?v=" + Id, "https://youtube.com:123/watch?v=" + Id, Url + "&v=aaaaaaaaaaa", "file:///watch?v=" + Id }) {
                    try { YoutubeTools.CanonicalUrl(new(url)); throw new Exception("Invalid page accepted."); } catch (Exception ex) when (ex is ArgumentException or DownloadException) { }
                }
                return Task.CompletedTask;
            });
            await Run("YouTube metadata filters DRM and preserves quality format identifiers", () => {
                var info = YoutubeTools.ParseInfo(Info); Check(info.HasAudio && info.Qualities.Length == 3, "Invalid format count.");
                Check(info.Qualities[0].Height == 1080 && info.Qualities[0].YoutubeFormat == "137", "Format selection was lost."); return Task.CompletedTask;
            });
            await Run("Video size estimates include separate audio, reject invalid sizes and preserve titles", () => {
                var info = YoutubeTools.ParseInfo(Info.Replace("\"tbr\":1500", "\"tbr\":1500,\"filesize\":1000000")
                    .Replace("\"tbr\":null", "\"ext\":\"m4a\",\"tbr\":128,\"filesize_approx\":null"));
                Check(info.Title == "A video: title?", "Title was lost.");
                Check(info.Qualities[0].EstimatedBytes == 1160000, "Separate audio was not included.");
                Check(info.Qualities.Single(c => c.YoutubeFormat == "136").EstimatedBytes == 1285000, "Bitrate estimate differs.");
                Check(info.Qualities.Single(c => c.YoutubeFormat == "18").EstimatedBytes == 500000, "Muxed audio was counted twice.");
                Check(YoutubeTools.ParseInfo(Info).Qualities[0].EstimatedBytes is null, "Unknown audio size became a misleading estimate.");
                Check(MediaTools.EstimateBytes(double.PositiveInfinity, 1000) is null && MediaTools.EstimateBytes(-1, 1000) is null, "Invalid estimate accepted.");
                return Task.CompletedTask;
            });
            await Run("YouTube display tiers use platform labels and height, never encoded width", () => {
                var info = YoutubeTools.ParseInfo("""
                  {"formats":[
                   {"format_id":"401","width":3840,"height":1920,"format_note":"2160p60","vcodec":"avc1"},
                   {"format_id":"400","width":2560,"height":1280,"format_note":"1440p","vcodec":"avc1"},
                   {"format_id":"399","width":1920,"height":960,"format_note":"1080p","vcodec":"avc1"},
                   {"format_id":"398","width":1280,"height":640,"format_note":"720p","vcodec":"avc1"},
                   {"format_id":"397","width":960,"height":428,"format_note":"480p","vcodec":"avc1"},
                   {"format_id":"396","width":640,"height":320,"format_note":"360p","vcodec":"avc1"},
                   {"format_id":"395","width":256,"height":128,"format_note":"144p","vcodec":"avc1"}]}
                  """);
                Check(info.Qualities.Select(q => q.QualityHeight).SequenceEqual(new int?[] { 2160, 1440, 1080, 720, 480, 360, 144 }), "Cropped dimensions leaked into quality tiers.");
                Check(info.Qualities[0].Height == 1920 && info.Qualities[0].YoutubeFormat == "401", "Original dimensions or selected format changed.");
                Check(YoutubeTools.QualityHeight("1080p", 1920) == 1080, "Platform label did not take precedence for portrait media.");
                return Task.CompletedTask;
            });
            await Run("YouTube metadata rejects live, upcoming and account-only sources", () => {
                foreach (var info in new[] { Info.Replace("not_live", "is_live"), Info.Replace("not_live", "is_upcoming"), Info.Replace("\"duration\":10", "\"duration\":10,\"availability\":\"needs_auth\"") })
                    try { YoutubeTools.ParseInfo(info); throw new Exception("Restricted source accepted."); } catch (IOException) { }
                return Task.CompletedTask;
            });
            await Run("YouTube adapter disables configuration, plugins, playlists and remote components", () => {
                var flags = YoutubeTools.CommonArguments(); foreach (var flag in new[] { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--no-remote-components" }) Check(flags.Contains(flag), "Missing controlled-extractor flag.");
                Check(!flags.Any(a => a.Contains("cookies")), "Browser cookies requested.");
                Check(YoutubeTools.FormatSelector("137", "mp4").Contains("137[acodec=none]+bestaudio"), "Separate audio was not selected.");
                Check(YoutubeTools.OutputTemplate(new StoredDownload { Url = Url, Destination = "unused", PartPath = @"C:\folder\%(title)s.part" }).Contains("%%(title)s"), "Literal output paths were not escaped.");
                try { YoutubeTools.FormatSelector("137+--exec", "mp4"); throw new Exception("Injected format accepted."); } catch (ArgumentException) { }
                return Task.CompletedTask;
            });
            foreach (var output in new[] { "mp4", "mp3" }) await Run($"YouTube {output} queue preserves bytes, options, duplicate receipts and reopened completion", async () => {
                var folder = Path.Combine(root, Guid.NewGuid().ToString("N")); var payload = Enumerable.Range(0, 8193).Select(i => (byte)i).ToArray();
                string[]? invoked = null;
                YoutubeTools.TestRunner = async (command, progress, token) => {
                    if (command.Contains("--print")) return Info;
                    invoked = command; var index = Array.IndexOf(command, "--output");
                    await File.WriteAllBytesAsync(command[index + 1].Replace("%(ext)s", output), payload, token); progress?.Invoke(payload.Length); return "";
                };
                Guid id; var request = Guid.NewGuid();
                await using (var owner = await DownloadCoordinator.OpenAsync(folder)) {
                    var accepted = owner.AcceptBrowserDownload(request, new(Url), Path.Combine(folder, "downloads"), output, youtubeFormat: output == "mp4" ? "137" : null); id = accepted.TaskId;
                    await owner.WaitForIdleAsync(id); var task = owner.Get(id); Check(task.State == DownloadState.Completed, task.Message);
                    Check(task.Sha256 == Convert.ToHexStringLower(SHA256.HashData(payload)), "Published bytes differ.");
                    Check(task.Filename == "A video_ title_." + output, "Video title did not become the filename.");
                    Check(invoked is not null && invoked.Last() == Url, "Extractor did not receive canonical page.");
                    var downloadCommand = invoked ?? throw new Exception("Download command missing.");
                    Check(downloadCommand.Contains("--embed-thumbnail") == (output == "mp3"), "Only MP3 downloads should embed source artwork.");
                    if (output == "mp3") {
                        Check(downloadCommand.Contains("--convert-thumbnails") && downloadCommand[Array.IndexOf(downloadCommand, "--convert-thumbnails") + 1] == "jpg", "Cover art must use a broadly supported image format.");
                        Check(!downloadCommand.Contains("--write-thumbnail") && !downloadCommand.Contains("--write-all-thumbnails"), "Embedded artwork must not leave a separate image.");
                    }
                    Check(!Directory.EnumerateFiles(Path.GetDirectoryName(task.PartPath)!).Any(f => Path.GetFileName(f).Contains(".yt.")), "Owned scratch files were left behind.");
                    Check(owner.AcceptBrowserDownload(request, new(Url), Path.Combine(folder, "downloads"), output, youtubeFormat: output == "mp4" ? "137" : null).TaskId == id, "Duplicate conversion created.");
                    if (output == "mp4") try { owner.AcceptBrowserDownload(request, new(Url), folder, output, youtubeFormat: "136"); throw new Exception("Changed format accepted for same request."); } catch (InvalidDataException) { }
                }
                await using var reopened = await DownloadCoordinator.OpenAsync(folder); Check(reopened.Get(id).State == DownloadState.Completed, "Completion did not reopen.");
                Check(reopened.Get(id).Filename == "A video_ title_." + output, "Title filename did not persist.");
            });
            await Run("Video titles stay inside the destination, reserve unique names and preserve explicit destinations", async () => {
                YoutubeTools.TestRunner = async (command, progress, token) => {
                    if (command.Contains("--print")) return Info;
                    await File.WriteAllBytesAsync(command[Array.IndexOf(command, "--output") + 1].Replace("%(ext)s", "mp4"), [1, 2, 3], token); return "";
                };
                var folder = Path.Combine(root, "names"); var downloads = Path.Combine(folder, "downloads"); Directory.CreateDirectory(downloads);
                await File.WriteAllTextAsync(Path.Combine(downloads, "A video_ title_.mp4"), "keep");
                await using var owner = await DownloadCoordinator.OpenAsync(folder);
                var first = owner.AcceptBrowserDownload(Guid.NewGuid(), new(Url), downloads, "mp4", youtubeFormat: "137");
                var second = owner.AcceptBrowserDownload(Guid.NewGuid(), new(Url), downloads, "mp4", youtubeFormat: "137");
                await Task.WhenAll(owner.WaitForIdleAsync(first.TaskId), owner.WaitForIdleAsync(second.TaskId));
                var filenames = new[] { owner.Get(first.TaskId).Filename, owner.Get(second.TaskId).Filename };
                Check(filenames.Order().SequenceEqual(new[] { "A video_ title_ (2).mp4", "A video_ title_ (3).mp4" }), "Colliding title filenames were not reserved.");
                Check(await File.ReadAllTextAsync(Path.Combine(downloads, "A video_ title_.mp4")) == "keep", "Existing file was overwritten.");
                var explicitTask = owner.Add(new(Url), Path.Combine(downloads, "My filename.mp4")); owner.Resume(explicitTask.Id); await owner.WaitForIdleAsync(explicitTask.Id);
                Check(owner.Get(explicitTask.Id).Filename == "My filename.mp4", "Explicit destination was replaced.");
                Check(DownloadCoordinator.SafeFilename("../CON: title?  ") == "_CON_ title_", "Traversal title was not sanitized.");
                Check(DownloadCoordinator.SafeFilename("CON") == "_CON", "Windows device name was not escaped.");
                Check(DownloadCoordinator.SafeFilename(new string('a', 119) + "😀").Length == 119, "Title was cut inside a Unicode character.");
            });
            await Run("YouTube removed qualities fail without silently downloading another format", async () => {
                YoutubeTools.TestRunner = (command, _, _) => command.Contains("--print") ? Task.FromResult(Info) : throw new Exception("Download must not begin.");
                await using var owner = await DownloadCoordinator.OpenAsync(Path.Combine(root, "missing"));
                var id = owner.AcceptBrowserDownload(Guid.NewGuid(), new(Url), Path.Combine(root, "downloads"), "mp4", youtubeFormat: "999").TaskId;
                await owner.WaitForIdleAsync(id); Check(owner.Get(id).State == DownloadState.Failed && !File.Exists(owner.Get(id).Destination), "Removed format was published.");
            });
            await Run("Explicit video filenames and queued state survive reopening without downloading early", async () => {
                int downloads = 0;
                YoutubeTools.TestRunner = async (command, _, token) => {
                    if (command.Contains("--print")) return Info;
                    downloads++; await File.WriteAllBytesAsync(command[Array.IndexOf(command, "--output") + 1].Replace("%(ext)s", "mp4"), [1, 2, 3], token); return "";
                };
                var folder = Path.Combine(root, "options"); var request = Guid.NewGuid(); Guid id;
                var name = "youtube-" + Id + "-137.mp4";
                await using (var owner = await DownloadCoordinator.OpenAsync(folder)) {
                    var accepted = owner.AcceptBrowserDownload(request, new(Url), folder, "mp4", youtubeFormat: "137", filename: name, startImmediately: false); id = accepted.TaskId;
                    await Task.Delay(50); Check(downloads == 0 && owner.Get(id).State == DownloadState.Paused, "Queued video downloaded before Resume.");
                    foreach (var invalid in new[] { "../escape.mp4", "CON.mp4", "wrong.exe" }) {
                        try { owner.AcceptBrowserDownload(Guid.NewGuid(), new(Url), folder, "mp4", filename: invalid, startImmediately: false); throw new Exception("Unsafe filename accepted."); } catch (ArgumentException) { }
                    }
                }
                await using var reopened = await DownloadCoordinator.OpenAsync(folder); Check(reopened.Get(id).State == DownloadState.Paused && reopened.Get(id).PreserveFilename, "Queue options were not durable.");
                reopened.Resume(id); await reopened.WaitForIdleAsync(id);
                Check(reopened.Get(id).State == DownloadState.Completed && reopened.Get(id).Filename == name && downloads == 1, "Explicit filename was replaced by the extracted title.");
            });
            await Run("Media progress reports speed, accumulates separate streams and becomes idle when bytes stop", async () => {
                var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                YoutubeTools.TestRunner = async (command, progress, token) => {
                    if (command.Contains("--print")) return Info;
                    progress!(65536); await Task.Delay(350, token); progress(131072); progress(32768);
                    reported.TrySetResult(); await finish.Task.WaitAsync(token);
                    var path = command[Array.IndexOf(command, "--output") + 1].Replace("%(ext)s", "mp4");
                    await File.WriteAllBytesAsync(path, [1, 2, 3, 4], token); return "";
                };
                var folder = Path.Combine(root, "live-progress");
                await using var owner = await DownloadCoordinator.OpenAsync(folder);
                var id = owner.AcceptBrowserDownload(Guid.NewGuid(), new(Url), Path.Combine(folder, "downloads"), "mp4", youtubeFormat: "137").TaskId;
                await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (owner.Get(id).Received != 163840 || owner.Get(id).BytesPerSecond <= 0) {
                    if (DateTime.UtcNow > deadline) throw new Exception("Media byte changes did not reach the live speed model.");
                    await Task.Delay(25);
                }
                Check(owner.Get(id).Total is null && owner.Get(id).Committed == 0, "Transient media progress became a false size or resume checkpoint.");
                await Task.Delay(2300);
                Check(owner.Get(id).BytesPerSecond == 0 && owner.Get(id).Received == 163840, "Stalled media still reports an active speed or lost its byte count.");
                finish.TrySetResult(); await owner.WaitForIdleAsync(id);
                Check(owner.Get(id).State == DownloadState.Completed && owner.Get(id).Received == 4 && owner.Get(id).BytesPerSecond == 0, "Final media output did not replace transient progress.");
            });
            await Run("YouTube pause cleans scratch files and reopens with the same selected format", async () => {
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); bool pause = true;
                YoutubeTools.TestRunner = async (command, _, token) => {
                    if (command.Contains("--print")) return Info;
                    var path = command[Array.IndexOf(command, "--output") + 1].Replace("%(ext)s", "mp4");
                    await File.WriteAllBytesAsync(path, [1, 2, 3, 4], token); started.TrySetResult();
                    if (pause) await Task.Delay(Timeout.Infinite, token); return "";
                };
                var folder = Path.Combine(root, "paused"); Guid id;
                await using (var owner = await DownloadCoordinator.OpenAsync(folder)) {
                    id = owner.AcceptBrowserDownload(Guid.NewGuid(), new(Url), Path.Combine(folder, "downloads"), "mp4", youtubeFormat: "137").TaskId;
                    await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); await owner.PauseAsync(id);
                    Check(owner.Get(id).State == DownloadState.Paused && !Directory.EnumerateFiles(Path.Combine(folder, "downloads")).Any(), "Pause retained extractor scratch files.");
                }
                pause = false;
                await using var reopened = await DownloadCoordinator.OpenAsync(folder); Check(reopened.Get(id).YoutubeFormat == "137", "Selected YouTube quality was lost.");
                reopened.Resume(id); await reopened.WaitForIdleAsync(id); Check(reopened.Get(id).State == DownloadState.Completed, reopened.Get(id).Message);
            });
        }
        finally { YoutubeTools.TestRunner = null; Directory.Delete(root, true); }
        if (args.Length > 0) { var path = Path.GetFullPath(args[0]); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { failed, results }, new JsonSerializerOptions { WriteIndented = true })); }
        Console.WriteLine($"{results.Count - failed}/{results.Count} YouTube adapter checks passed"); return failed == 0 ? 0 : 1;
    }
    public static async Task<int> LiveAsync(string[] args)
    {
        try {
            var url = new Uri(args.FirstOrDefault() ?? "https://www.youtube.com/watch?v=YE7VzlLtp-4");
            var info = await YoutubeTools.InspectAsync(url); Console.WriteLine(JsonSerializer.Serialize(info));
            if (args.Contains("--download")) {
                var root = Path.Combine(Path.GetTempPath(), "Syphon.Youtube.Live", Guid.NewGuid().ToString("N"));
                try {
                    await using var owner = await DownloadCoordinator.OpenAsync(root);
                    var low = info.Qualities.OrderBy(c => c.Height).ThenBy(c => c.Bitrate).First();
                    foreach (var output in new[] { "mp4", "mp3" }) {
                        var id = owner.AcceptBrowserDownload(Guid.NewGuid(), url, Path.Combine(root, "downloads"), output, youtubeFormat: output == "mp4" ? low.YoutubeFormat : null).TaskId;
                        try { await owner.WaitForIdleAsync(id).WaitAsync(TimeSpan.FromSeconds(120)); }
                        finally { await owner.PauseAsync(id); }
                        var task = owner.Get(id); if (task.State != DownloadState.Completed) throw new IOException(task.Message);
                        var start = new System.Diagnostics.ProcessStartInfo(MediaTools.FindTool("ffprobe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                        foreach (var argument in new[] { "-v", "error", "-show_entries", "stream=codec_type,codec_name,height", "-of", "json", task.Destination }) start.ArgumentList.Add(argument);
                        using var process = System.Diagnostics.Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
                        if (process.ExitCode != 0) throw new IOException("Live output did not pass FFprobe validation.");
                        Console.WriteLine($"LIVE {output}: {task.Committed} bytes, SHA256 {task.Sha256}, streams {await stdout}"); await stderr;
                    }
                } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            }
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("Live YouTube inspection: " + ex.Message); if (ex.InnerException is not null) Console.WriteLine(ex.InnerException.Message); return 1; }
    }
}
