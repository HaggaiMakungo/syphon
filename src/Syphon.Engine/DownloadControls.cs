using Syphon.Core;

namespace Syphon.Engine;

public sealed partial class DownloadCoordinator
{
    public TransferOptions Options { get { lock (gate) return pendingOptions ?? options; } }
    public bool SettingsPending { get { lock (gate) return pendingOptions is not null; } }
    private long NextQueueOrder() => tasks.Count == 0 ? 1 : checked(tasks.Values.Max(t => t.QueueOrder) + 1);

    public void Configure(TransferOptions value)
    {
        value.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (value == (pendingOptions ?? options)) return;
            bandwidth.SetGlobal(value.GlobalBytesPerSecond);
            // Scheduling and retry settings need no new HTTP client or origin semaphores.
            var transportUnchanged = value with { MaximumConcurrentDownloads = options.MaximumConcurrentDownloads,
                MaximumRetries = options.MaximumRetries, GlobalBytesPerSecond = options.GlobalBytesPerSecond, Schedule = options.Schedule };
            if (transportUnchanged == options) { options = value; pendingOptions = null; }
            else pendingOptions = value;
            PumpQueue();
        }
    }

    public int ClearHistory(IEnumerable<Guid> selection)
    {
        var ids = selection.Distinct().ToArray();
        lock (gate) {
            ObjectDisposedException.ThrowIf(closing, this);
            foreach (var id in ids) {
                if (!tasks.TryGetValue(id, out var task) || task.State is not (DownloadState.Completed or DownloadState.Cancelled or DownloadState.Failed) ||
                    workers.ContainsKey(id) || updatingLinks.Contains(id) || removing.Contains(id))
                    throw new InvalidOperationException("A selected record changed or is busy. Reopen Clear downloads and try again.");
            }
            store.RemoveHistory(ids);
            foreach (var id in ids) { tasks.Remove(id); browserContexts.Remove(id); bandwidth.SetTask(id, 0); }
            return ids.Length;
        }
    }

    public void SetPriority(Guid id, int priority)
    {
        if (priority is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(priority));
        lock (gate) { ObjectDisposedException.ThrowIf(closing, this); Set(tasks[id] with { Priority = priority }); }
    }

    public void MoveInQueue(Guid id, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            var task = tasks[id];
            var queue = tasks.Values.Where(t => t.Priority == task.Priority && t.State is DownloadState.Queued or DownloadState.Paused)
                .OrderBy(t => t.QueueOrder).ThenBy(t => t.Id).ToArray();
            var index = Array.FindIndex(queue, t => t.Id == id); var target = index + direction;
            if (index < 0 || target < 0 || target >= queue.Length) return;
            var positions = queue.Select(t => t.QueueOrder).ToArray();
            (queue[index], queue[target]) = (queue[target], queue[index]);
            var changed = queue.Select((t, i) => t with { QueueOrder = positions[i] }).ToArray();
            store.SaveMany(changed);
            foreach (var t in changed) tasks[t.Id] = t;
        }
    }

    public async Task PauseAllAsync()
    {
        Task[] completions;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (queueSuspended) return;
            queueSuspended = true;
            completions = workers.Keys.ToArray().Select(id => PauseAsync(id)).ToArray();
        }
        try { await Task.WhenAll(completions).ConfigureAwait(false); }
        finally { lock (gate) { queueSuspended = false; PumpQueue(); } }
    }

    public void ResumeAll()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (queueSuspended) return;
            foreach (var t in GetTasks().Where(t => !t.RestartRequired && !updatingLinks.Contains(t.Id) &&
                (!t.RequiresBrowserContext || browserContexts.ContainsKey(t.Id)) && t.State is DownloadState.Paused or DownloadState.Queued)) Resume(t.Id);
        }
    }
}
