using Syphon.Core;
using Syphon.Engine;

if (args.Length != 2 || !Uri.TryCreate(args[0], UriKind.Absolute, out var url))
{
    Console.Error.WriteLine("Usage: Syphon.Cli <http-or-https-url> <destination-file>");
    return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using var engine = new HttpDownloadEngine();
try
{
    var result = await engine.DownloadAsync(new(url, args[1]), cancellationToken: cancellation.Token);
    Console.WriteLine($"Saved: {result.DestinationPath}");
    Console.WriteLine($"Bytes: {result.Length}");
    Console.WriteLine($"SHA-256: {result.Sha256}");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled. Unfinished data is retained as a .syphon-part file; resume is not available in this build.");
    return 130;
}
catch (DownloadException ex)
{
    Console.Error.WriteLine($"{ex.Failure}: {ex.Message}");
    return 1;
}
