using System.Diagnostics;
using Syphon.Core;

namespace Syphon.Engine;

/// <summary>One budget across every HTTP worker, plus one shared budget per task.</summary>
internal sealed class BandwidthLimiter : IDisposable
{
    internal const long MaximumRate = 1024L * 1024 * 1024;
    private readonly object gate = new();
    private readonly Bucket global = new();
    private readonly Dictionary<Guid, Bucket> tasks = new();
    private readonly LinkedList<Waiter> waiting = new();
    private readonly CancellationTokenSource shutdown = new();
    private bool pumping, disposed;
    private sealed class Bucket
    {
        public long Rate;
        public double Tokens;
        public long Tick = Stopwatch.GetTimestamp();
        public double Capacity => Math.Max(1024, Rate / 10d);
        public int Quantum => Rate == 0 ? 16384 : (int)Math.Clamp(Rate / 10, 1024, 16384);
        public void Refill(long now) { Tokens = Math.Min(Capacity, Tokens + Stopwatch.GetElapsedTime(Tick, now).TotalSeconds * Rate); Tick = now; }
        public void Set(long rate) { Rate = rate; Tokens = 0; Tick = Stopwatch.GetTimestamp(); }
    }
    private sealed record Waiter(Guid Id, int Requested, TaskCompletionSource<int> Completion)
    { public LinkedListNode<Waiter>? Node { get; set; } }

    public static void ValidateRate(long rate)
    {
        if (rate != 0 && (rate < 1024 || rate > MaximumRate))
            throw new ArgumentOutOfRangeException(nameof(rate), "Choose Unlimited (0), or a speed between 1 KB/s and 1 GB/s.");
    }
    public void SetGlobal(long rate) { ValidateRate(rate); lock (gate) { if (global.Rate != rate) global.Set(rate); Pump(); } }
    public void SetTask(Guid id, long rate)
    {
        ValidateRate(rate);
        lock (gate) {
            if (rate == 0) tasks.Remove(id);
            else { if (!tasks.TryGetValue(id, out var bucket)) tasks[id] = bucket = new(); if (bucket.Rate != rate) bucket.Set(rate); }
            Pump();
        }
    }
    private async ValueTask<int> AcquireAsync(Guid id, int requested, CancellationToken token)
    {
        Waiter waiter;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); token.ThrowIfCancellationRequested();
            if (requested == 0) return 0;
            if (global.Rate == 0 && !tasks.ContainsKey(id)) return requested;
            waiter = new(id, requested, new(TaskCreationOptions.RunContinuationsAsynchronously));
            waiter.Node = waiting.AddLast(waiter); Pump();
            if (!pumping && waiting.Count > 0) { pumping = true; _ = RunPumpAsync(); }
        }
        using var registration = token.Register(() => { lock (gate) { if (waiter.Node?.List is not null) waiting.Remove(waiter.Node); waiter.Completion.TrySetCanceled(token); } });
        return await waiter.Completion.Task.ConfigureAwait(false);
    }
    // Called under gate. Skip a task waiting on its own cap so it cannot block other jobs.
    private void Pump()
    {
        var now = Stopwatch.GetTimestamp(); global.Refill(now);
        var node = waiting.First;
        while (node is not null)
        {
            var next = node.Next; var item = node.Value; tasks.TryGetValue(item.Id, out var task);
            task?.Refill(now);
            var count = global.Rate == 0 && task is null ? item.Requested : Math.Min(item.Requested, Math.Min(global.Quantum, task?.Quantum ?? 16384));
            if ((global.Rate == 0 || global.Tokens >= count) && (task is null || task.Tokens >= count))
            {
                if (global.Rate > 0) global.Tokens -= count;
                if (task is not null) task.Tokens -= count;
                waiting.Remove(node); item.Completion.TrySetResult(count);
            }
            node = next;
        }
    }
    private async Task RunPumpAsync()
    {
        try {
            while (true) {
                await Task.Delay(25, shutdown.Token).ConfigureAwait(false);
                lock (gate) { if (disposed) return; Pump(); if (waiting.Count == 0) { pumping = false; return; } }
            }
        } catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    }
    public async ValueTask<int> ReadAsync(Stream input, Memory<byte> buffer, Guid id, TimeSpan idleTimeout, CancellationToken token)
    {
        var allowed = await AcquireAsync(id, buffer.Length, token).ConfigureAwait(false);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(idleTimeout);
        int count;
        try { count = await input.ReadAsync(buffer[..allowed], idle.Token).ConfigureAwait(false); }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new DownloadException(DownloadFailure.Timeout, "The server stopped sending data.", ex); }
        // EOF and short reads spend only the bytes actually read, not the reservation.
        lock (gate) {
            if (global.Rate > 0) global.Tokens = Math.Min(global.Capacity, global.Tokens + allowed - count);
            if (tasks.TryGetValue(id, out var task)) task.Tokens = Math.Min(task.Capacity, task.Tokens + allowed - count);
        }
        return count;
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; shutdown.Cancel(); foreach (var waiter in waiting) waiter.Completion.TrySetCanceled(); waiting.Clear(); }
    }
}
