using System.IO.Pipes;

namespace Syphon.BrowserBridge;

public static class BrowserPipeClient
{
    public static async Task<BrowserReply> SendAsync(string name, BrowserRequest request, TimeSpan timeout, CancellationToken token = default, TimeSpan? connectTimeout = null, string? expectedServerExecutable = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        while (true)
        {
            try { return await SendOnceAsync(name, request, deadline.Token, connectTimeout, expectedServerExecutable ?? Environment.ProcessPath ?? "").ConfigureAwait(false); }
            catch (IOException) when (!deadline.IsCancellationRequested)
            { await Task.Delay(25, deadline.Token).ConfigureAwait(false); }
        }
    }

    private static async Task<BrowserReply> SendOnceAsync(string name, BrowserRequest request, CancellationToken token, TimeSpan? connectTimeout, string expectedServerExecutable)
    {
        await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
        { if (connectTimeout is TimeSpan limit) connect.CancelAfter(limit); await pipe.ConnectAsync(connect.Token).ConfigureAwait(false); }
        using var verifiedServer = PipeServerIdentity.Verify(pipe, expectedServerExecutable);
        await BrowserProtocol.WriteAsync(pipe, request, token).ConfigureAwait(false);
        var reply = await BrowserProtocol.ReadAsync<BrowserReply>(pipe, token).ConfigureAwait(false)
            ?? throw new IOException("Syphon closed the connection before acknowledging this request.");
        if (reply.Version != 1 || reply.RequestId != request.RequestId) throw new InvalidDataException("Syphon returned an incompatible acknowledgment.");
        return reply;
    }
}

public sealed class BrowserPipeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim slots = new(4);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> clients = new();
    private int nextClient;
    private readonly Task listening;
    public BrowserPipeServer(string name, Func<BrowserRequest, CancellationToken, Task<BrowserReply>> handle)
    { listening = Task.Run(() => ListenAsync(name, handle)); }

    private async Task ListenAsync(string name, Func<BrowserRequest, CancellationToken, Task<BrowserReply>> handle)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await slots.WaitAsync(stopping.Token).ConfigureAwait(false);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 4,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(stopping.Token).ConfigureAwait(false);
                }
                catch { pipe?.Dispose(); slots.Release(); throw; }
                var id = Interlocked.Increment(ref nextClient);
                var task = ServeAndReleaseAsync(pipe, handle);
                clients[id] = task;
                _ = task.ContinueWith(_ => { clients.TryRemove(id, out var ignored); }, TaskScheduler.Default);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { await Task.Delay(25).ConfigureAwait(false); }
        }
    }

    private async Task ServeAndReleaseAsync(NamedPipeServerStream pipe, Func<BrowserRequest, CancellationToken, Task<BrowserReply>> handle)
    {
        try { await ServeAsync(pipe, handle).ConfigureAwait(false); }
        finally { slots.Release(); }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, Func<BrowserRequest, CancellationToken, Task<BrowserReply>> handle)
    {
        await using (pipe)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(15));
                var request = await BrowserProtocol.ReadAsync<BrowserRequest>(pipe, deadline.Token).ConfigureAwait(false);
                if (request is null) return;
                if (request.Action == "media-inspect") deadline.CancelAfter(TimeSpan.FromSeconds(45));
                if (request.ShowOptions) deadline.CancelAfter(TimeSpan.FromMinutes(10));
                BrowserReply reply;
                try { BrowserProtocol.Validate(request); reply = await handle(request, deadline.Token).ConfigureAwait(false); }
                catch (InvalidDataException ex) { reply = new(1, request.RequestId, "rejected", null, ex.Message); }
                catch (Exception) { reply = new(1, request.RequestId, "unavailable", null, "Syphon could not save this request. Retry the same request from the extension."); }
                await BrowserProtocol.WriteAsync(pipe, reply, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or System.Text.DecoderFallbackException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stopping.Cancel();
        try { await listening.ConfigureAwait(false); await Task.WhenAll(clients.Values).ConfigureAwait(false); }
        finally { stopping.Dispose(); slots.Dispose(); }
    }
}
