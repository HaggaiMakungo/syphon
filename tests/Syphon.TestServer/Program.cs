using Syphon.TestServer;

var port = args.Length == 2 && args[0] == "--port" && int.TryParse(args[1], out var parsed) ? parsed : 8878;
if (port is < 0 or > 65535) return 2;
await using var server = await FixtureServer.StartAsync(port);
Console.WriteLine($"Syphon test server: {server.BaseAddress}");
Console.WriteLine("Routes: /file, /unknown, /slow, /range, /wrong-range, /truncate, /stall, /headers-delay, /redirect, /status/503");
Console.WriteLine("Use ?size=<bytes> for generated payloads. Ctrl+C stops the server.");
await server.WaitForShutdownAsync();
return 0;
