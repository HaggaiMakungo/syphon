using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Syphon.TestServer;

/// <summary>Local deterministic HTTP fixture; never binds to a public interface.</summary>
public sealed class FixtureServer : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly X509Certificate2? certificate;
    public Uri BaseAddress { get; }

    private FixtureServer(WebApplication app, Uri baseAddress, X509Certificate2? certificate) { this.app = app; BaseAddress = baseAddress; this.certificate = certificate; }

    public static async Task<FixtureServer> StartAsync(int port = 0, bool https = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders();
        X509Certificate2? certificate = null;
        if (https)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port, listener => listener.UseHttps(certificate)));
        }
        else builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        app.Use(async (context, next) => {
            if (context.Request.Query["expired"] == "1") { context.Response.StatusCode = 403; return; }
            if (context.Request.Query["auth"] == "1" && context.Request.Cookies["SyphonFixtureSession"] != "fixture-secret") { context.Response.StatusCode = 401; return; }
            if (context.Request.Query["referer"] == "1" && (!context.Request.Headers.Referer.ToString().EndsWith("/source-page", StringComparison.Ordinal) ||
                context.Request.Headers.UserAgent != "FixtureBrowser/1.0")) { context.Response.StatusCode = 403; return; }
            await next();
        });
        app.MapMethods("/preview-head", ["HEAD"], (HttpContext c) => {
            c.Response.ContentLength = 12345; c.Response.Headers.ContentDisposition = "attachment; filename=preview.bin"; return Task.CompletedTask;
        });
        app.MapMethods("/preview-slow", ["HEAD"], async (HttpContext c) => {
            await Task.Delay(2500, c.RequestAborted); c.Response.ContentLength = 23456;
        });
        app.MapGet("/compat-redirect", (HttpContext c) => Results.Redirect(c.Request.Query["target"].ToString()));
        app.MapGet("/compat-login", () => Results.Content("<html>Sign in</html>","text/html"));
        app.MapGet("/compat-echo", (HttpContext c) => Results.Text("cookie=" + c.Request.Headers.Cookie.ToString() + "\nreferer=" + c.Request.Headers.Referer.ToString()));
        var attempts = new ConcurrentDictionary<string, int>();
        var segmentStats = new ConcurrentDictionary<string, SegmentStats>();
        app.MapGet("/segment-stats/{key}", (string key) =>
        {
            var stats = segmentStats.GetOrAdd(key, _ => new());
            lock (stats) return Results.Json(new { stats.Peak, stats.ActiveRequests, stats.RequestCount, Ranges = stats.Ranges.ToArray() });
        });
        app.MapGet("/segments", async (HttpContext c) =>
        {
            if (!TrySize(c, out var length)) return;
            var stats = segmentStats.GetOrAdd(c.Request.Query["key"].ToString(), _ => new());
            var mode = c.Request.Query["mode"].ToString();
            c.Response.Headers.AcceptRanges = "bytes";
            var etag = $"\"segments-{length}-v1\"";
            c.Response.Headers.ETag = mode == "weak" ? "W/" + etag : etag;
            var rangeText = c.Request.Headers.Range.ToString();
            if (rangeText.Length == 0 || mode == "ignoreprobe")
            {
                c.Response.ContentLength = length;
                await Payload.WriteAsync(c.Response.Body, 0, length, c.RequestAborted, 5);
                return;
            }
            var range = RangeHeaderValue.Parse(rangeText).Ranges.Single();
            var start = range.From!.Value;
            var end = range.To ?? length - 1;
            var probe = start == 0 && end == 0;
            if (!probe && mode == "ignore")
            { c.Response.ContentLength = length; await Payload.WriteAsync(c.Response.Body, 0, length, c.RequestAborted, 5); return; }
            if (c.Request.Headers.IfRange != etag) { c.Response.StatusCode = 400; return; }
            if (!probe && mode == "changed") c.Response.Headers.ETag = "\"changed\"";
            if (!probe && mode == "encoded") c.Response.Headers.ContentEncoding = "gzip";
            c.Response.StatusCode = 206;
            c.Response.Headers.ContentRange = $"bytes {start + (!probe && mode == "wrongstart" ? 1 : 0)}-{end + (!probe && mode == "wrongend" ? 1 : 0)}/{length + (!probe && mode == "wrongtotal" ? 1 : 0)}";
            c.Response.ContentLength = end - start + 1;
            if (mode == "sameetag-different") {
                var body = new byte[65536]; long position = start;
                while (position <= end) { var n = (int)Math.Min(body.Length, end - position + 1); Payload.Fill(body.AsSpan(0, n), position); body[0] ^= 1;
                    await c.Response.Body.WriteAsync(body.AsMemory(0, n), c.RequestAborted); position += n; }
                return;
            }
            if (probe) { await Payload.WriteAsync(c.Response.Body, 0, 1, c.RequestAborted); return; }
            Interlocked.Increment(ref stats.Calls);
            var active = Interlocked.Increment(ref stats.Active);
            lock (stats) { stats.Peak = Math.Max(stats.Peak, active); stats.Ranges.Add($"{start}-{end}"); }
            try
            {
                if (mode == "truncate" && start == 0 && Interlocked.CompareExchange(ref stats.Dropped, 1, 0) == 0)
                {
                    await Payload.WriteAsync(c.Response.Body, start, Math.Min(65536, end - start + 1), c.RequestAborted);
                    await c.Response.Body.FlushAsync(c.RequestAborted); await Task.Delay(50, c.RequestAborted); c.Abort(); return;
                }
                await Payload.WriteAsync(c.Response.Body, start, end - start + 1, c.RequestAborted, 5);
            }
            finally { Interlocked.Decrement(ref stats.Active); }
        });
        app.MapGet("/attempts/{key}", (string key) => Results.Text(attempts.GetValueOrDefault(key).ToString(CultureInfo.InvariantCulture)));
        app.MapGet("/retry", async (HttpContext c) =>
        {
            if (!TrySize(c, out var length)) return;
            var attempt = attempts.AddOrUpdate(c.Request.Query["key"].ToString(), 1, (_, n) => n + 1);
            var mode = c.Request.Query["mode"].ToString();
            var failures = int.TryParse(c.Request.Query["failures"], out var requested) ? requested : 2;
            var etag = $"\"retry-{length}\"";
            if (attempt <= failures && mode is not ("disconnect" or "weak"))
            {
                c.Response.StatusCode = int.TryParse(mode, out var status) ? status : 503;
                var retry = c.Request.Query["retry"].ToString();
                if (retry.Length > 0) c.Response.Headers.RetryAfter = retry == "date" ? DateTimeOffset.UtcNow.AddSeconds(2).ToString("R") : retry;
                return;
            }
            c.Response.Headers.ETag = mode == "weak" ? "W/" + etag : etag;
            var range = c.Request.Headers.Range.ToString();
            var start = range.Length > 0 ? RangeHeaderValue.Parse(range).Ranges.Single().From!.Value : 0;
            if (start > 0)
            {
                if (c.Request.Headers.IfRange != etag) { c.Response.StatusCode = 400; return; }
                c.Response.StatusCode = 206;
                c.Response.Headers.ContentRange = $"bytes {start}-{length - 1}/{length}";
            }
            c.Response.ContentLength = length - start;
            if (attempt <= failures && mode is "disconnect" or "weak")
            {
                await Payload.WriteAsync(c.Response.Body, start, 65536, c.RequestAborted);
                await c.Response.Body.FlushAsync(c.RequestAborted);
                await Task.Delay(100, c.RequestAborted);
                c.Abort();
                return;
            }
            await Payload.WriteAsync(c.Response.Body, start, length - start, c.RequestAborted);
        });
        var revision = 1;
        app.MapGet("/mutable", (HttpContext c) =>
        {
            var version = Volatile.Read(ref revision);
            var bytes = new byte[4096];
            Payload.Fill(bytes, 0);
            for (var i = 0; i < bytes.Length; i++) bytes[i] ^= (byte)version;
            c.Response.Headers.ETag = $"\"mutable-{version}\"";
            return Results.Bytes(bytes);
        });
        app.MapPost("/mutable/bump", () => Results.Ok(Interlocked.Increment(ref revision)));
        app.MapGet("/resume", async (HttpContext c) =>
        {
            if (!TrySize(c, out var length)) return;
            var mode = c.Request.Query["mode"].ToString();
            var continuation = c.Request.Headers.Range.Count > 0;
            var etag = $"\"resume-{length}-{(continuation && mode == "changed" ? 2 : 1)}\"";
            if (mode != "noetag" && !(continuation && mode == "missingetag"))
                c.Response.Headers.ETag = mode == "weak" ? "W/" + etag : etag;
            if (mode != "unknown") c.Response.ContentLength = length;
            if (!continuation || mode is "ignore" or "changed")
            {
                await Payload.WriteAsync(c.Response.Body, 0, length, c.RequestAborted, 10);
                return;
            }
            if (mode == "416") { c.Response.StatusCode = 416; c.Response.ContentLength = 0; return; }
            var range = RangeHeaderValue.Parse(c.Request.Headers.Range.ToString());
            var start = range.Ranges.Single().From!.Value;
            if (c.Request.Headers.IfRange != etag && mode != "missingetag")
            { c.Response.StatusCode = 400; c.Response.ContentLength = 0; return; }
            c.Response.StatusCode = 206;
            c.Response.Headers.ContentRange = $"bytes {start + (mode == "wrongstart" ? 1 : 0)}-{length - 1}/{length + (mode == "wrongtotal" ? 1 : 0)}";
            c.Response.ContentLength = length - start;
            if (mode == "encoded") c.Response.Headers.ContentEncoding = "gzip";
            await Payload.WriteAsync(c.Response.Body, start, length - start, c.RequestAborted, 10);
        });
        app.MapGet("/file", c => SendPayload(c, knownLength: true));
        app.MapGet("/unknown", c => SendPayload(c, knownLength: false));
        app.MapGet("/slow", c => SendPayload(c, knownLength: true, delayMs: 10));
        app.MapGet("/redirect", (HttpContext c) => Results.Redirect("/file" + c.Request.QueryString));
        app.MapGet("/redirect-loop", () => Results.Redirect("/redirect-loop"));
        app.MapGet("/headers-delay", async (HttpContext c) =>
        {
            await Task.Delay(2000, c.RequestAborted);
            await SendPayload(c, true);
        });
        app.MapGet("/stall", async (HttpContext c) =>
        {
            c.Response.ContentLength = 1024 * 1024;
            await c.Response.StartAsync(c.RequestAborted);
            await Payload.WriteAsync(c.Response.Body, 0, 16384, c.RequestAborted);
            await c.Response.Body.FlushAsync(c.RequestAborted);
            await Task.Delay(10000, c.RequestAborted);
        });
        app.MapGet("/manifest.m3u8", async (HttpContext c) =>
        {
            c.Response.ContentType = "application/vnd.apple.mpegurl";
            await c.Response.WriteAsync("#EXTM3U\n", c.RequestAborted);
            await c.Response.Body.FlushAsync(c.RequestAborted);
            if (c.Request.Query["stall"] == "1")
                await Task.Delay(Timeout.Infinite, c.RequestAborted);
            await c.Response.WriteAsync("#EXT-X-ENDLIST\n", c.RequestAborted);
        });
        app.MapGet("/truncate", async (HttpContext c) =>
        {
            c.Response.ContentLength = 1024 * 1024;
            await c.Response.StartAsync(c.RequestAborted);
            await Payload.WriteAsync(c.Response.Body, 0, 16384, c.RequestAborted);
            await c.Response.Body.FlushAsync(c.RequestAborted);
            c.Abort();
        });
        app.MapGet("/encoded", (HttpContext c) =>
        {
            c.Response.Headers.ContentEncoding = "gzip";
            return Results.Bytes(new byte[] { 1, 2, 3 });
        });
        app.MapGet("/status/{code:int}", (int code) => Results.StatusCode(Math.Clamp(code, 400, 599)));
        app.MapGet("/range", async (HttpContext c) =>
        {
            if (!TrySize(c, out var length)) return;
            var etag = $"\"fixture-{length}-v1\"";
            c.Response.Headers.ETag = etag;
            c.Response.Headers.AcceptRanges = "bytes";
            var header = c.Request.Headers.Range.ToString();
            var ifRange = c.Request.Headers.IfRange.ToString();
            if (header.Length == 0 || (ifRange.Length > 0 && ifRange != etag))
            {
                await SendPayload(c, true);
                return;
            }
            if (!RangeHeaderValue.TryParse(header, out var range) || range.Unit != "bytes" || range.Ranges.Count != 1)
            { c.Response.StatusCode = 400; return; }
            var item = range.Ranges.Single();
            long start, end;
            if (item.From is long from)
            { start = from; end = Math.Min(item.To ?? length - 1, length - 1); }
            else
            { start = Math.Max(0, length - (item.To ?? 0)); end = length - 1; }
            if (start >= length || start < 0 || end < start)
            {
                c.Response.StatusCode = 416;
                c.Response.Headers.ContentRange = $"bytes */{length}";
                return;
            }
            c.Response.StatusCode = 206;
            c.Response.Headers.ContentRange = $"bytes {start}-{end}/{length}";
            c.Response.ContentLength = end - start + 1;
            await Payload.WriteAsync(c.Response.Body, start, end - start + 1, c.RequestAborted);
        });
        app.MapGet("/wrong-range", async (HttpContext c) =>
        {
            c.Response.StatusCode = 206;
            c.Response.Headers.ContentRange = "bytes 100-199/1000";
            c.Response.ContentLength = 100;
            await Payload.WriteAsync(c.Response.Body, 0, 100, c.RequestAborted);
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new(app, new Uri(address), certificate);
    }

    private static bool TrySize(HttpContext context, out long size)
    {
        var text = context.Request.Query["size"].ToString();
        if (text.Length == 0) { size = 1024 * 1024; return true; }
        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out size) && size is >= 0 and <= 8L * 1024 * 1024 * 1024)
            return true;
        context.Response.StatusCode = 400;
        return false;
    }

    private static async Task SendPayload(HttpContext context, bool knownLength, int delayMs = 0)
    {
        if (!TrySize(context, out var length)) return;
        context.Response.ContentType = "application/octet-stream";
        context.Response.Headers.ETag = $"\"fixture-{length}-v1\"";
        if (knownLength) context.Response.ContentLength = length;
        await Payload.WriteAsync(context.Response.Body, 0, length, context.RequestAborted, delayMs);
    }

    public Task WaitForShutdownAsync() => app.WaitForShutdownAsync();
    public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); certificate?.Dispose(); }
}

public sealed class SegmentStats
{
    public int Active;
    public int Peak { get; set; }
    public int Calls;
    public int Dropped;
    public List<string> Ranges { get; set; } = [];
    public int ActiveRequests => Volatile.Read(ref Active);
    public int RequestCount => Volatile.Read(ref Calls);
}

public static class Payload
{
    public static void Fill(Span<byte> buffer, long offset)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var position = offset + i;
            buffer[i] = unchecked((byte)(position * 31 + (position >> 16) + 17));
        }
    }

    public static async Task WriteAsync(Stream stream, long offset, long length, CancellationToken token, int delayMs = 0)
    {
        var buffer = new byte[64 * 1024];
        long written = 0;
        while (written < length)
        {
            var count = (int)Math.Min(buffer.Length, length - written);
            Fill(buffer.AsSpan(0, count), offset + written);
            await stream.WriteAsync(buffer.AsMemory(0, count), token);
            written += count;
            if (delayMs > 0) await Task.Delay(delayMs, token);
        }
    }

    public static string ExpectedHash(long length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        for (long offset = 0; offset < length; offset += buffer.Length)
        {
            var count = (int)Math.Min(buffer.Length, length - offset);
            Fill(buffer.AsSpan(0, count), offset);
            hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
