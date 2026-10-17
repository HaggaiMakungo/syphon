using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Syphon.Core;

namespace Syphon.Engine;

/// <summary>A fresh, single-connection GET transfer. The coordinator owns durable tasks and resume.</summary>
public sealed class HttpDownloadEngine : IDownloadEngine, IDisposable
{
    private const int BufferSize = 64 * 1024;
    private readonly HttpClient client;
    private readonly TransferOptions options;
    private readonly BandwidthLimiter bandwidth = new();
    private readonly ConcurrentDictionary<Guid, byte> active = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> origins = new(StringComparer.OrdinalIgnoreCase);

    public HttpDownloadEngine(TransferOptions? options = null)
    {
        this.options = options ?? new TransferOptions();
        this.options.Validate();
        bandwidth.SetGlobal(this.options.GlobalBytesPerSecond);
        client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = this.options.ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request, IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateUrl(request.Url);
        if (request.Id == Guid.Empty || string.IsNullOrWhiteSpace(request.DestinationPath))
            throw new DownloadException(DownloadFailure.InvalidRequest, "Choose a destination file.");

        string destination;
        try { destination = Path.GetFullPath(request.DestinationPath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw new DownloadException(DownloadFailure.InvalidRequest, "Choose a valid destination filename.", ex);
        }
        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(Path.GetFileName(destination)))
            throw new DownloadException(DownloadFailure.InvalidRequest, "Choose a destination filename, not a drive or folder root.");
        // An exclusive file handle prevents two workers from writing the same part.
        // CreateNew and a no-overwrite move protect both unfinished and existing files.
        var partPath = Path.Combine(directory, $".{Path.GetFileName(destination)}.{request.Id:N}.syphon-part");
        var clock = Stopwatch.StartNew();
        long received = 0;
        long? total = null;
        var state = DownloadState.Starting;
        void Report() => progress?.Report(new(request.Id, state, received, total, clock.Elapsed,
            clock.Elapsed.TotalSeconds > 0 ? received / clock.Elapsed.TotalSeconds : 0));

        if (!active.TryAdd(request.Id, 0))
            throw new DownloadException(DownloadFailure.AlreadyRunning, "This download is already running.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new DownloadException(DownloadFailure.DestinationExists, "The destination already exists. Choose a different filename.");
            Directory.CreateDirectory(directory);
            Report();
            using var response = await GetResponseAsync(request.Url, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new DownloadException(DownloadFailure.HttpStatus, $"The server returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new DownloadException(DownloadFailure.UnexpectedResponse, "The server returned an unsupported content encoding.");
            total = response.Content.Headers.ContentLength;

            string digest;
            await using (var output = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var buffer = new byte[BufferSize];
                var lastReport = TimeSpan.Zero;
                state = DownloadState.Downloading;
                Report();
                while (true)
                {
                    int count;
                    try
                    {
                        count = await bandwidth.ReadAsync(input, buffer, request.Id, options.BodyIdleTimeout, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new DownloadException(DownloadFailure.Timeout, "The server stopped sending data.", ex);
                    }
                    catch (IOException ex)
                    {
                        throw new DownloadException(total.HasValue ? DownloadFailure.IncompleteResponse : DownloadFailure.Network,
                            "The network stream ended unexpectedly. The unfinished file has been retained.", ex);
                    }
                    finally { idle.CancelAfter(Timeout.InfiniteTimeSpan); }
                    if (count == 0) break;
                    if (total.HasValue && count > total.Value - received)
                        throw new DownloadException(DownloadFailure.UnexpectedResponse, "The response exceeded its declared size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, count);
                    received = checked(received + count);
                    if (clock.Elapsed - lastReport >= TimeSpan.FromMilliseconds(100))
                    {
                        Report();
                        lastReport = clock.Elapsed;
                    }
                }
                if (total.HasValue && received != total.Value)
                    throw new DownloadException(DownloadFailure.IncompleteResponse, "The transfer ended before the complete file arrived.");
                cancellationToken.ThrowIfCancellationRequested();
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
                digest = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            cancellationToken.ThrowIfCancellationRequested();
            state = DownloadState.Publishing;
            Report();
            try { File.Move(partPath, destination, overwrite: false); }
            catch (IOException ex) when (File.Exists(destination) || Directory.Exists(destination))
            {
                throw new DownloadException(DownloadFailure.DestinationExists, "A file appeared at the destination. It has been preserved.", ex);
            }
            state = DownloadState.Completed;
            Report();
            return new(request.Id, destination, received, digest, clock.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            state = DownloadState.Cancelled;
            Report();
            throw;
        }
        catch (DownloadException)
        {
            state = DownloadState.Failed;
            Report();
            throw;
        }
        catch (HttpRequestException ex)
        {
            state = DownloadState.Failed;
            Report();
            throw new DownloadException(DownloadFailure.Network, "The network transfer failed. Any unfinished data has been retained.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state = DownloadState.Failed;
            Report();
            throw new DownloadException(DownloadFailure.FileSystem, "The file could not be saved. Check the destination and available disk space.", ex);
        }
        finally { active.TryRemove(request.Id, out _); }
    }

    internal async Task<HttpResponseMessage> GetResponseAsync(Uri initial, CancellationToken cancellationToken,
        long offset = 0, string? etag = null, long? end = null, DownloadRequestContext? context = null)
    {
        var current = initial;
        for (var redirect = 0; ; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            request.Headers.UserAgent.ParseAdd("Syphon/0.1");
            if (context is not null && DownloadRequestContext.SameOrigin(new(context.Url), current))
            {
                var cookie = context.CookieHeader(current);
                if (cookie.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookie);
                if (!string.IsNullOrWhiteSpace(context.UserAgent)) { request.Headers.UserAgent.Clear(); request.Headers.TryAddWithoutValidation("User-Agent", context.UserAgent); }
                if (context.Referrer is not null) {
                    var referrer = new Uri(context.Referrer);
                    if (referrer.Scheme != "https" || current.Scheme == "https")
                        request.Headers.Referrer = DownloadRequestContext.SameOrigin(referrer, current) ? new UriBuilder(referrer) { Fragment = "" }.Uri : new Uri(referrer.GetLeftPart(UriPartial.Authority) + "/");
                }
            }
            if (offset > 0 || end.HasValue)
            {
                request.Headers.Range = new RangeHeaderValue(offset, end);
                if (etag is not null) request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(etag));
            }
            var origin = origins.GetOrAdd(current.GetLeftPart(UriPartial.Authority), _ => new(options.MaximumConnectionsPerOrigin));
            await origin.WaitAsync(cancellationToken).ConfigureAwait(false);
            using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            headerTimeout.CancelAfter(options.HeaderTimeout);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                    headerTimeout.Token).ConfigureAwait(false);
                response.Content = new LeasedContent(response.Content, origin);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                origin.Release();
                throw new DownloadException(DownloadFailure.Timeout, "The server did not respond in time.", ex);
            }
            catch { origin.Release(); throw; }
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (redirect >= options.MaximumRedirects || location is null)
                throw new DownloadException(DownloadFailure.UnexpectedResponse, "The server returned an invalid or excessive redirect chain.");
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            ValidateUrl(next);
            if (current.Scheme == "https" && next.Scheme != "https")
                throw new DownloadException(DownloadFailure.UnexpectedResponse, "The server tried to redirect a secure download to an insecure connection.");
            current = next;
        }
    }

    internal static void ValidateUrl(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(url.UserInfo))
            throw new DownloadException(DownloadFailure.InvalidRequest, "Enter an HTTP or HTTPS link without embedded credentials.");
    }

    public void Dispose() { client.Dispose(); bandwidth.Dispose(); }

    // A connection slot follows the response body, including redirects and failed validation.
    private sealed class LeasedContent : HttpContent
    {
        private readonly HttpContent inner;
        private readonly SemaphoreSlim origin;
        private int disposed;
        public LeasedContent(HttpContent inner, SemaphoreSlim origin)
        {
            this.inner = inner; this.origin = origin;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => inner.CopyToAsync(stream);
        protected override bool TryComputeLength(out long length) { length = inner.Headers.ContentLength ?? 0; return inner.Headers.ContentLength.HasValue; }
        protected override Task<Stream> CreateContentReadStreamAsync() => inner.ReadAsStreamAsync();
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken token) => inner.ReadAsStreamAsync(token);
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref disposed, 1) == 0)
                try { inner.Dispose(); } finally { origin.Release(); }
            base.Dispose(disposing);
        }
    }
}
