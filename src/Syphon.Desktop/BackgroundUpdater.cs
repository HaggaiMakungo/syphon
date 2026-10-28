using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Syphon.Desktop;

public static class BackgroundUpdater
{
    public static async Task<string> DownloadAsync(UpdatePackage package, string cacheRoot, IProgress<(long Received, long? Total)> progress,
        CancellationToken token, HttpClient? client = null, Action<string>? verifyPublisher = null)
    {
        if (!Regex.IsMatch(package.Name, @"^Syphon-Setup-\d+\.\d+\.\d+-beta\.\d+-win-x64\.exe$") ||
            package.Size is <= 0 or > 2_000_000_000 || package.Sha256.Length != 64 || !package.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid update metadata.");
        var version = package.Name[13..^12];
        var expected = $"https://github.com/HaggaiMakungo/syphon/releases/download/v{version}/{package.Name}";
        if (package.Url != expected) throw new InvalidDataException("The update is not a matching Syphon release asset.");
        token.ThrowIfCancellationRequested();
        cacheRoot = Path.GetFullPath(cacheRoot);
        for (var part = new DirectoryInfo(cacheRoot); part is not null; part = part.Parent)
            if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("The update cache cannot use linked folders.");
        Directory.CreateDirectory(cacheRoot);
        if (new DriveInfo(Path.GetPathRoot(cacheRoot)!).AvailableFreeSpace < package.Size + 16 * 1024 * 1024)
            throw new IOException("Free additional space for the update download, then try again.");
        var directory = Path.Combine(cacheRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, package.Name + ".part");
        var destination = Path.Combine(directory, package.Name);
        var ownsClient = client is null;
        client ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var response = await GetResponseAsync(client, new Uri(package.Url), timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode != System.Net.HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0 ||
                response.Content.Headers.ContentLength is long length && length != package.Size) throw new InvalidDataException("The update response does not match its expected size.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan)) {
                var buffer = new byte[131072];
                while (true) {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token); idle.CancelAfter(TimeSpan.FromSeconds(45));
                    var count = await input.ReadAsync(buffer, idle.Token).ConfigureAwait(false); if (count == 0) break;
                    received += count; if (received > package.Size) throw new InvalidDataException("The update exceeded its expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, count); progress.Report((received, package.Size));
                }
                await output.FlushAsync(timeout.Token).ConfigureAwait(false); output.Flush(true);
            }
            if (received != package.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update integrity check failed. Your installed version has not changed.");
            token.ThrowIfCancellationRequested();
            await Task.Run(() => {
                if (verifyPublisher is not null) verifyPublisher(temporary);
                else InstallerSignature.VerifyPackage(temporary, version);
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(temporary, destination, false);
            return destination;
        } catch (OperationCanceledException ex) when (!token.IsCancellationRequested) {
            try { if (File.Exists(temporary)) File.Delete(temporary); if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw new IOException("The update download timed out. Your installed version has not changed; retry when the connection is available.", ex);
        } catch {
            try { if (File.Exists(temporary)) File.Delete(temporary); if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        } finally { if (ownsClient) client.Dispose(); }
    }
    private static async Task<HttpResponseMessage> GetResponseAsync(HttpClient client, Uri url, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 5; redirects++) {
            if (url.Scheme != "https" || url.Port != 443 || !string.IsNullOrEmpty(url.UserInfo) ||
                url.Host is not ("github.com" or "release-assets.githubusercontent.com")) throw new InvalidDataException("The update redirected outside GitHub's release service.");
            using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.UserAgent.ParseAdd("Syphon/" + ReleaseUpdates.CurrentVersion);
            using var headers = CancellationTokenSource.CreateLinkedTokenSource(token); headers.CancelAfter(TimeSpan.FromSeconds(30));
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location is null) throw new InvalidDataException("The update redirect is missing its destination.");
            url = new Uri(url, location);
        }
        throw new InvalidDataException("Too many update redirects.");
    }
}
