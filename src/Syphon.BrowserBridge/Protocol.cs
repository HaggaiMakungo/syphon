using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Syphon.Core;

namespace Syphon.BrowserBridge;

public sealed record BrowserRequest(int Version, string Action, Guid RequestId, string? Url = null,
    string? Format = null, int? VideoStream = null, int? AudioStream = null, string? YoutubeFormat = null, string? Title = null,
    bool ShowOptions = false, string? Quality = null, long? EstimatedBytes = null, DownloadRequestContext? Context = null, int? QualityHeight = null);
public sealed record BrowserReply(int Version, Guid RequestId, string Status, Guid? TaskId, string Message, string? TaskState = null, string? ErrorCode = null, object? Media = null);

public static class BrowserProtocol
{
    public const string HostName = "com.syphon.downloads";
    public const int MaximumFrameBytes = 16384;
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
    public static string PipeName => "Syphon.Browser." + Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value)))[..24];

    public static void Validate(BrowserRequest request)
    {
        if (request.Context is not null) {
            if (request.Action != "download" || request.Format is not null) throw new InvalidDataException("Browser sign-in is supported for direct file downloads.");
            try { request.Context.Validate(request.Url ?? ""); } catch (ArgumentException) { throw new InvalidDataException("Invalid browser download context."); }
        }
        if ((request.ShowOptions || request.Quality is not null || request.EstimatedBytes is not null) && request.Action != "download" ||
            request.Quality?.Length > 120 || request.EstimatedBytes is < 1)
            throw new InvalidDataException("Invalid download options.");
        if (request.Title is not null && (request.Action != "download" || request.Title.Length > 512))
            throw new InvalidDataException("Invalid video title.");
        if (request.Version != 1 || request.RequestId == Guid.Empty || request.Action is not ("download" or "status" or "ping" or "media-inspect"))
            throw new InvalidDataException("Unsupported Syphon browser request.");
        if (request.QualityHeight is not null && (request.Action != "download" || request.Format != "mp4" || request.VideoStream is not null || request.YoutubeFormat is not null || request.QualityHeight is not (144 or 240 or 360 or 480 or 720 or 1080 or 1440 or 2160 or 4320)))
            throw new InvalidDataException("Invalid advertised video quality.");
        if (request.Format is not (null or "mp3" or "mp4") || request.VideoStream is < 0 or > 255 || request.AudioStream is < 0 or > 255 ||
            (request.VideoStream is not null || request.AudioStream is not null) && request.Format is null ||
            request.Action != "download" && (request.Format is not null || request.VideoStream is not null || request.AudioStream is not null))
            throw new InvalidDataException("Unsupported media output or stream selection.");
        if (request.YoutubeFormat is string selected && (request.Action != "download" || request.Format is null || selected.Length is < 1 or > 64 ||
            !char.IsAsciiLetterOrDigit(selected[0]) || selected.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))))
            throw new InvalidDataException("Unsupported YouTube format identifier.");
        if (request.Action is "download" or "media-inspect" && (request.Url is null || request.Url.Length > 8192 ||
            !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidDataException("Syphon this supports HTTP and HTTPS links without embedded credentials.");
    }

    public static async Task<T?> ReadAsync<T>(Stream input, CancellationToken token = default) where T : class
    {
        var header = new byte[4];
        var first = await input.ReadAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
        if (first == 0) return null;
        await input.ReadExactlyAsync(header.AsMemory(1), token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length is 0 or > MaximumFrameBytes) throw new InvalidDataException("Syphon browser message exceeds its size limit.");
        var bytes = new byte[(int)length];
        await input.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(new UTF8Encoding(false, true).GetString(bytes), JsonOptions)
            ?? throw new InvalidDataException("Invalid Syphon browser message.");
    }

    public static async Task WriteAsync<T>(Stream output, T value, CancellationToken token = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (bytes.Length is 0 or > MaximumFrameBytes) throw new InvalidDataException("Syphon browser response exceeds its size limit.");
        var header = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)bytes.Length);
        await output.WriteAsync(header, token).ConfigureAwait(false);
        await output.WriteAsync(bytes, token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }
}
