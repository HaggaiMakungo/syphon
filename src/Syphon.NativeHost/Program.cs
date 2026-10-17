using System.Diagnostics;
using System.Text.Json;
using Syphon.BrowserBridge;

BrowserRequest? request = null;
try
{
    // Only binary framed JSON goes to stdout; never log links or browser context.
    var settingsPath = Path.Combine(AppContext.BaseDirectory, "host-settings.json");
    var settings = JsonSerializer.Deserialize<HostSettings>(await File.ReadAllTextAsync(settingsPath), BrowserProtocol.JsonOptions)
        ?? throw new InvalidDataException("Syphon browser connection is not configured.");
    if (args.Length == 0 || !settings.AllowedOrigins.Contains(args[0], StringComparer.Ordinal))
        throw new InvalidDataException("This browser extension is not allowed to connect to Syphon.");
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    request = await BrowserProtocol.ReadAsync<BrowserRequest>(Console.OpenStandardInput(), deadline.Token);
    if (request is null) return 0;
    BrowserProtocol.Validate(request);
    var inspection = request.Action == "media-inspect";
    if (inspection) deadline.CancelAfter(TimeSpan.FromSeconds(70));
    if (request.ShowOptions) deadline.CancelAfter(TimeSpan.FromMinutes(11));
    BrowserReply reply;
    var pipeName = settings.PipeName ?? BrowserProtocol.PipeName;
    try { reply = await BrowserPipeClient.SendAsync(pipeName, request, request.ShowOptions ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(inspection ? 45 : 3), deadline.Token,
        inspection || request.ShowOptions ? TimeSpan.FromSeconds(3) : null); }
    catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException)
    {
        if (!File.Exists(settings.DesktopPath)) throw new IOException("The Syphon desktop app could not be found. Reconnect the browser integration.");
        var start = new ProcessStartInfo(settings.DesktopPath) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(settings.DesktopPath)! };
        foreach (var argument in settings.DesktopArguments ?? []) start.ArgumentList.Add(argument);
        if (settings.DotnetRoot is not null)
        { start.Environment["DOTNET_ROOT"] = settings.DotnetRoot; start.Environment["DOTNET_ROOT_X64"] = settings.DotnetRoot; }
        // The GUI must not inherit Chrome's framed stdin/stdout handles or keep that port open.
        foreach (var standard in new[] { -10, -11, -12 })
            NativeHandles.SetHandleInformation(NativeHandles.GetStdHandle(standard), 1, 0);
        using (var desktop = Process.Start(start)) { desktop?.StandardInput.Close(); }
        reply = await BrowserPipeClient.SendAsync(pipeName, request, request.ShowOptions ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(inspection ? 60 : 20), deadline.Token);
    }
    await BrowserProtocol.WriteAsync(Console.OpenStandardOutput(), reply);
    return reply.Status is "accepted" or "ready" or "removed" or "not_found" or "cancelled" ? 0 : 1;
}
catch (Exception ex)
{
    var message = ex is InvalidDataException ? ex.Message : "Syphon could not confirm this handoff. Open the desktop app, then retry the same request from the extension.";
    try { await BrowserProtocol.WriteAsync(Console.OpenStandardOutput(), new BrowserReply(1, request?.RequestId ?? Guid.Empty, "unavailable", null, message,
        ErrorCode: $"{ex.GetType().Name}:{ex.HResult:X8}")); }
    catch (IOException) { }
    return 1;
}

internal sealed record HostSettings(string DesktopPath, string[] AllowedOrigins, string? DotnetRoot = null, string? PipeName = null, string[]? DesktopArguments = null);

internal static class NativeHandles
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint GetStdHandle(int handle);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool SetHandleInformation(nint handle, uint mask, uint flags);
}
