using Syphon.Core;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Syphon.BrowserBridge;
using Syphon.Engine;
using Syphon.TestServer;

if (args.FirstOrDefault() == "--fixture-server")
{
    Directory.CreateDirectory(args[2]);
    await using var coordinator = await DownloadCoordinator.OpenAsync(args[2]);
    await using var server = new BrowserPipeServer(args[1], (request, _) => Task.FromResult(Handle(coordinator, request, Path.Combine(args[2], "downloads"))));
    await File.WriteAllTextAsync(Path.Combine(args[2], "ready"), Environment.ProcessId.ToString());
    var until = Stopwatch.StartNew();
    while (!File.Exists(Path.Combine(args[2], "stop")) && until.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(25);
    return 0;
}

var root = Path.Combine(Path.GetTempPath(), "Syphon.Browser.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
var hostBuild = Path.GetFullPath(args.FirstOrDefault() ?? $"src/Syphon.NativeHost/bin/{configuration}/net10.0-windows");
const string origin = "chrome-extension://aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/";
var results = new List<object>(); int failed = 0;
await using var fixture = await FixtureServer.StartAsync();

async Task Run(string name, Func<Task> test)
{
    try { await test(); results.Add(new { name, passed = true }); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine($"FAIL {name}: {ex}"); }
}
string Folder() { var p = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
string Pipe() => "Syphon.Test." + Guid.NewGuid().ToString("N");
BrowserRequest Request(string route = "file?size=100003", Guid? id = null) => new(1, "download", id ?? Guid.NewGuid(), new Uri(fixture.BaseAddress, route).AbsoluteUri);

async Task<string> HostFolder(string pipe, object? settings = null)
{
    var folder = Folder();
    foreach (var file in Directory.GetFiles(hostBuild)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
    await File.WriteAllTextAsync(Path.Combine(folder, "host-settings.json"), JsonSerializer.Serialize(settings ?? new
    { desktopPath = Environment.ProcessPath!, allowedOrigins = new[] { origin }, pipeName = pipe }, BrowserProtocol.JsonOptions));
    return folder;
}

async Task<BrowserReply> Native(string folder, BrowserRequest request, string caller = origin, bool fragmented = false)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(Path.Combine(folder, "Syphon.NativeHost.dll")); start.ArgumentList.Add(caller);
    using var process = Process.Start(start)!;
    var stderr = process.StandardError.ReadToEndAsync();
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    try
    {
        if (fragmented)
        {
            using var buffer = new MemoryStream(); await BrowserProtocol.WriteAsync(buffer, request);
            foreach (var b in buffer.ToArray()) { await process.StandardInput.BaseStream.WriteAsync(new[] { b }, deadline.Token); await process.StandardInput.BaseStream.FlushAsync(deadline.Token); }
        }
        else await BrowserProtocol.WriteAsync(process.StandardInput.BaseStream, request, deadline.Token);
        process.StandardInput.Close();
        var reply = await BrowserProtocol.ReadAsync<BrowserReply>(process.StandardOutput.BaseStream, deadline.Token)
            ?? throw new InvalidOperationException("Native host returned no frame.");
        await process.WaitForExitAsync(deadline.Token);
        var tail = new byte[1];
        Check(await process.StandardOutput.BaseStream.ReadAsync(tail, deadline.Token) == 0, "Native stdout contains unframed data.");
        Check((await stderr).Length == 0, "Native host leaked diagnostic output.");
        return reply;
    }
    finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
}

try
{
    await Run("Activation is a bounded command without download or session payload", () =>
    {
        var activation=new BrowserRequest(1,"activate",Guid.NewGuid());BrowserProtocol.Validate(activation);
        foreach(var invalid in new[]{activation with{Url="https://example.test/private"},activation with{ShowOptions=true},activation with{Title="unexpected"},activation with{Format="mp3"},activation with{Context=new("https://example.test/private")}})
        {
            try{BrowserProtocol.Validate(invalid);throw new InvalidOperationException("Activation accepted a download payload.");}
            catch(InvalidDataException){}
        }
        return Task.CompletedTask;
    });
    await Run("Media inspection separates quick connection admission from processing deadline", async () =>
    {
        var pipe = Pipe();
        await using var server = new BrowserPipeServer(pipe, async (request, token) => {
            await Task.Delay(4000, token);
            return new BrowserReply(1, request.RequestId, "ready", null, "Media inspected.", Media: new { qualities = new[] { new { height = 1080, youtubeFormat = "137" } } });
        });
        var host = await HostFolder(pipe);
        var reply = await Native(host, new BrowserRequest(1, "media-inspect", Guid.NewGuid(), "https://www.youtube.com/watch?v=YE7VzlLtp-4"));
        Check(reply.Status == "ready" && reply.Media is not null, "Slow inspection lost its acknowledgment.");
    });
    await Run("Binary protocol handles fragmented Unicode JSON", async () =>
    {
        using var stream = new MemoryStream(); var request = Request("file?name=é&size=1234");
        await BrowserProtocol.WriteAsync(stream, request); stream.Position = 0;
        Check(await BrowserProtocol.ReadAsync<BrowserRequest>(stream) == request, "Frame roundtrip differs.");
        var media = request with { Format = "mp4", VideoStream = 2, AudioStream = 3, YoutubeFormat = "248-sr", Title = "Vidéo: title?", ShowOptions = true, Quality = "1080p", EstimatedBytes = 125000000 };
        BrowserProtocol.Validate(media); stream.SetLength(0); stream.Position = 0;
        await BrowserProtocol.WriteAsync(stream, media); stream.Position = 0;
        Check(await BrowserProtocol.ReadAsync<BrowserRequest>(stream) == media, "Media options were lost in the native frame.");
        var advertised = media with { VideoStream = null, AudioStream = null, YoutubeFormat = null, QualityHeight = 720 };
        BrowserProtocol.Validate(advertised); stream.SetLength(0); stream.Position = 0;
        await BrowserProtocol.WriteAsync(stream, advertised); stream.Position = 0;
        Check(await BrowserProtocol.ReadAsync<BrowserRequest>(stream) == advertised,"Advertised quality height was lost in the native frame.");
        foreach (var invalid in new[] { media with { Format = "exe" }, media with { QualityHeight = 1920 }, media with { QualityHeight = 720 }, media with { VideoStream = -1 }, media with { AudioStream = 256 }, media with { Action = "status" }, media with { YoutubeFormat = "137+--exec" }, media with { Title = new string('a', 513) }, media with { EstimatedBytes = -1 }, media with { Quality = new string('a', 121) } })
        {
            try { BrowserProtocol.Validate(invalid); throw new Exception("Invalid media options accepted."); }
            catch (InvalidDataException) { }
        }
    });
    await Run("Download options allow user decision time and return framed cancellation", async () => {
        var pipe = Pipe();
        await using var server = new BrowserPipeServer(pipe, async (request, token) => {
            await Task.Delay(4000, token);
            return new BrowserReply(1, request.RequestId, "cancelled", null, "Download cancelled.");
        });
        var host = await HostFolder(pipe);
        var reply = await Native(host, Request() with { ShowOptions = true, Quality = "1080p", EstimatedBytes = 125000000 });
        Check(reply.Status == "cancelled", "Options timed out before a user decision.");
    });
    foreach (var mode in new[] { "oversize", "empty", "truncated", "utf8", "json" })
        await Run($"Binary protocol rejects {mode}", async () =>
        {
            using var stream = new MemoryStream(); var header = new byte[4];
            var body = mode == "utf8" ? new byte[] { 0xFF } : mode == "json" ? Encoding.UTF8.GetBytes("{") : new byte[] { 1 };
            BinaryPrimitives.WriteUInt32LittleEndian(header, mode == "oversize" ? 1000000u : mode == "empty" ? 0u : mode == "truncated" ? 10u : (uint)body.Length);
            stream.Write(header); if (mode != "truncated") stream.Write(body); stream.Position = 0;
            try { await BrowserProtocol.ReadAsync<BrowserRequest>(stream); }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or DecoderFallbackException or JsonException) { return; }
            throw new InvalidOperationException("Invalid frame accepted.");
        });

    await Run("Native host to pipe accepts once despite concurrent duplicate requests", async () =>
    {
        var folder = Folder(); var pipe = Pipe();
        await using var coordinator = await DownloadCoordinator.OpenAsync(folder);
        await using var server = new BrowserPipeServer(pipe, (request, _) => Task.FromResult(Handle(coordinator, request, Path.Combine(folder, "downloads"))));
        var host = await HostFolder(pipe); var request = Request();
        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Native(host, request, fragmented: i == 0)));
        Check(replies.All(r => r.Status == "accepted" && r.RequestId == request.RequestId) && replies.Select(r => r.TaskId).Distinct().Count() == 1 && coordinator.GetTasks().Length == 1,
            $"Duplicate handoff result: tasks={coordinator.GetTasks().Length}; replies={string.Join("; ", replies.Select(r => r.Status + ":" + r.ErrorCode + ":" + r.Message))}");
        var task = coordinator.GetTasks().Single(); await coordinator.WaitForIdleAsync(task.Id);
        using var file = File.OpenRead(task.Destination);
        Check(Convert.ToHexStringLower(SHA256.HashData(file)) == Payload.ExpectedHash(100003), "Handoff output differs.");
    });

    await Run("Handoff receipts survive reopen, conflicts and task removal", async () =>
    {
        var folder = Folder(); var request = Request(); Guid id;
        await using (var owner = await DownloadCoordinator.OpenAsync(folder))
        {
            id = owner.AcceptBrowserDownload(request.RequestId, new(request.Url!), Path.Combine(folder, "downloads")).TaskId;
            await owner.WaitForIdleAsync(id);
        }
        await using (var owner = await DownloadCoordinator.OpenAsync(folder))
        {
            var replay = owner.AcceptBrowserDownload(request.RequestId, new(request.Url!), Path.Combine(folder, "downloads"));
            Check(replay.Duplicate && replay.TaskId == id && owner.GetTasks().Length == 1, "Reopen lost receipt.");
            try { owner.AcceptBrowserDownload(request.RequestId, new Uri(fixture.BaseAddress, "other"), folder); throw new Exception("Conflicting request accepted."); }
            catch (InvalidDataException) { }
            await owner.RemoveAsync(id, true);
        }
        await using var reopened = await DownloadCoordinator.OpenAsync(folder);
        var removed = reopened.AcceptBrowserDownload(request.RequestId, new(request.Url!), folder);
        Check(removed.Removed && reopened.GetTasks().Length == 0, "Removed request was recreated.");
    });

    await Run("Unapproved extension cannot reach desktop", async () =>
    {
        var pipe = Pipe(); var calls = 0;
        await using var server = new BrowserPipeServer(pipe, (r, _) => { calls++; return Task.FromResult(new BrowserReply(1, r.RequestId, "ready", null, "Ready")); });
        var host = await HostFolder(pipe);
        var reply = await Native(host, Request(), "chrome-extension://bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb/");
        Check(reply.Status == "unavailable" && calls == 0, "Unapproved origin reached pipe.");
    });

    await Run("Impersonated pipe receives no browser context before server verification", async () =>
    {
        var pipe=Pipe();var request=Request();
        request=request with {Context=new(request.Url!,Cookies:[new("SyphonFixtureSession","fixture-secret",fixture.BaseAddress.Host)])};
        using var impostor=new NamedPipeServerStream(pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        var expectedExecutable=Path.Combine(hostBuild,"Syphon.NativeHost.exe");Check(File.Exists(expectedExecutable),"The expected executable fixture is missing.");
        var host=await HostFolder(pipe,new {desktopPath=expectedExecutable,allowedOrigins=new[]{origin},pipeName=pipe});var attempt=Native(host,request);
        await impostor.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var received=new byte[1];using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var count=await impostor.ReadAsync(received,deadline.Token);
        Check(count==0,"An unverified server received browser request bytes.");
        var reply=await attempt;Check(reply.Status=="unavailable","An impersonated desktop was accepted.");
    });

    await Run("Unsupported link rejected before pipe connection", async () =>
    {
        var host = await HostFolder(Pipe());
        var reply = await Native(host, new(1, "download", Guid.NewGuid(), "file:///C:/private.txt"));
        Check(reply.Status == "unavailable" && reply.Message.Contains("HTTP"), "Unsupported scheme accepted.");
    });

    await Run("Unavailable desktop returns a framed uncertain outcome", async () =>
    {
        var request = Request(); var pipe=Pipe();var host = await HostFolder(pipe,new {desktopPath=Path.Combine(Folder(),"missing-desktop.exe"),allowedOrigins=new[]{origin},pipeName=pipe}); var reply = await Native(host, request);
        Check(reply.Status == "unavailable" && reply.RequestId == request.RequestId && reply.Message.Contains("retry", StringComparison.OrdinalIgnoreCase), "Missing app falsely acknowledged a task.");
    });

    await Run("Malformed local pipe client does not stop the listener", async () =>
    {
        var pipe = Pipe(); await using var server = new BrowserPipeServer(pipe, (r, _) => Task.FromResult(new BrowserReply(1, r.RequestId, "ready", null, "Ready")));
        using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        { await client.ConnectAsync(1000); await client.WriteAsync(new byte[] { 255, 255, 255, 127 }); }
        var reply = await BrowserPipeClient.SendAsync(pipe, new(1, "ping", Guid.NewGuid()), TimeSpan.FromSeconds(3));
        Check(reply.Status == "ready", "Malformed client killed listener.");
    });

    await Run("Host cold-starts a receiver and confirms only persisted tasks", async () =>
    {
        var pipe = Pipe(); var data = Folder();
        var host = await HostFolder(pipe, new { desktopPath = Environment.ProcessPath!, allowedOrigins = new[] { origin }, pipeName = pipe,
            desktopArguments = new[] { Assembly.GetExecutingAssembly().Location, "--fixture-server", pipe, data } });
        try
        {
            var reply = await Native(host, Request());
            Check(reply.Status == "accepted" && File.Exists(Path.Combine(data, "tasks.db")), "Cold start did not durably accept.");
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(data, "stop"), "stop");
            if (File.Exists(Path.Combine(data, "ready")))
            {
                try { using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(Path.Combine(data, "ready")))); await child.WaitForExitAsync(); }
                catch (ArgumentException) { }
            }
        }
    });

    await Run("Signed-in native handoff preserves scoped context and rejects header injection", async () =>
    {
        var pipe = Pipe(); var folder = await HostFolder(pipe); var data = Folder();
        await using var owner = await DownloadCoordinator.OpenAsync(data);
        await using var receiver = new BrowserPipeServer(pipe, (request, _) => Task.FromResult(Handle(owner, request, Path.Combine(data, "downloads"))));
        var request = Request("segments?size=100003&auth=1&referer=1");
        request = request with { Context = new(request.Url!, new Uri(fixture.BaseAddress,"source-page").AbsoluteUri,"FixtureBrowser/1.0",[new("SyphonFixtureSession","fixture-secret",fixture.BaseAddress.Host)]) };
        var accepted = await Native(folder, request); Check(accepted.Status == "accepted", "Authenticated handoff failed.");
        await owner.WaitForIdleAsync(accepted.TaskId!.Value);
        Check(owner.Get(accepted.TaskId.Value).State == Syphon.Core.DownloadState.Completed, "Session context was lost in the native pipe.");
        var invalid = await Native(folder, request with { RequestId = Guid.NewGuid(), Context = request.Context with { UserAgent = "fake\r\nCookie: injected" } });
        Check(invalid.Status != "accepted" && invalid.Message.Contains("Invalid browser download context"), "Header injection was accepted.");
    });

    await Run("Filename sanitization and collision choice stay inside Downloads", async () =>
    {
        var folder = Folder(); await using var owner = await DownloadCoordinator.OpenAsync(folder);
        foreach (var name in new[] { "CON.tar.gz", "..%2F..%2Fbad.exe", "COM1.txt", "x%3Ay.bin", "LPT%C2%B9.txt" })
        {
            var url = new Uri(fixture.BaseAddress, "file/" + name);
            var task = owner.AcceptBrowserDownload(Guid.NewGuid(), url, folder).Task!;
            Check(Path.GetDirectoryName(task.Destination) == folder && !Path.GetFileName(task.Destination).Contains(':'), "Unsafe browser filename.");
        }
        var a = owner.AcceptBrowserDownload(Guid.NewGuid(), new Uri(fixture.BaseAddress, "file"), folder).Task!;
        var b = owner.AcceptBrowserDownload(Guid.NewGuid(), new Uri(fixture.BaseAddress, "file"), folder).Task!;
        Check(a.Destination != b.Destination, "Browser filename collision reused a destination.");
        var titledRequest = Guid.NewGuid();
        var titled = owner.AcceptBrowserDownload(titledRequest, new Uri(fixture.BaseAddress, "file/video.mp4"), folder, title: "../A video: title?");
        Check(titled.Task!.Filename == "_A video_ title_.mp4" && Path.GetDirectoryName(titled.Task.Destination) == folder, "Page title escaped its folder or lost the file extension.");
        Check(owner.AcceptBrowserDownload(titledRequest, new Uri(fixture.BaseAddress, "file/video.mp4"), folder, title: "Changed page title").TaskId == titled.TaskId, "Cosmetic title changes broke receipt deduplication.");
    });
}
finally
{
    // Unique test-owned directory; all receivers/workers have been stopped before cleanup.
    Directory.Delete(root, true);
}
var report = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/browser-tests.json");
Directory.CreateDirectory(Path.GetDirectoryName(report)!);
await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { failed, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{results.Count - failed}/{results.Count} browser/native checks passed");
return failed == 0 ? 0 : 1;

static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static BrowserReply Handle(DownloadCoordinator owner, BrowserRequest request, string folder)
{
    if (request.Action == "ping") return new(1, request.RequestId, "ready", null, "Ready");
    var accepted = request.Action == "status" ? owner.BrowserStatus(request.RequestId) : owner.AcceptBrowserDownload(request.RequestId, new(request.Url!), folder, request.Format, request.VideoStream, request.AudioStream, request.YoutubeFormat, request.Title, context: request.Context, qualityHeight: request.QualityHeight);
    return accepted is null ? new(1, request.RequestId, "not_found", null, "Not found")
        : new(1, request.RequestId, accepted.Removed ? "removed" : "accepted", accepted.TaskId, "Added to Syphon", accepted.Task?.State.ToString());
}
