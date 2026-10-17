namespace Syphon.Engine;

public sealed partial class DownloadCoordinator
{
    private readonly CancellationTokenSource scheduleShutdown = new();
    private Task? scheduleLoop;
    private QueueSchedule? observedSchedule;
    private bool? observedWindow;
    private string? scheduleError;
    private QueueSchedule CurrentSchedule => (pendingOptions ?? options).Schedule ?? new();
    private bool ScheduleAllowsAdmission() => CurrentSchedule.IsOpen(timeProvider.GetLocalNow());
    public string ScheduleStatus { get { lock (gate) return scheduleError ?? (!CurrentSchedule.Enabled ? "Schedule off" : ScheduleAllowsAdmission() ? "Scheduled window open" : "Waiting for scheduled start"); } }

    public void SetSpeedLimit(Guid id, long bytesPerSecond)
    {
        BandwidthLimiter.ValidateRate(bytesPerSecond);
        lock (gate) {
            ObjectDisposedException.ThrowIf(closing, this);
            var task = tasks[id];
            if (task.MediaFormat is not null) throw new InvalidOperationException("Speed caps currently apply to ordinary file downloads. Media tools manage their own bandwidth.");
            Set(task with { SpeedLimitBytesPerSecond = bytesPerSecond }); bandwidth.SetTask(id, bytesPerSecond);
        }
    }

    public async Task RefreshScheduleAsync()
    {
        Task? pause = null;
        lock (gate)
        {
            if (closing || queueSuspended) return;
            var schedule = CurrentSchedule;
            if (!schedule.Enabled) { observedSchedule = schedule; observedWindow = null; scheduleError = null; PumpQueue(); return; }
            var open = schedule.IsOpen(timeProvider.GetLocalNow());
            if (schedule == observedSchedule && open == observedWindow) return;
            observedSchedule = schedule; observedWindow = open; scheduleError = null;
            if (open) { ResumeAll(); PumpQueue(); }
            else pause = PauseAllAsync();
        }
        try { if (pause is not null) await pause.ConfigureAwait(false); }
        catch (Exception) { lock (gate) { scheduleError = "Some downloads could not be paused. Check their errors before resuming."; observedWindow = null; } }
    }

    private async Task RunScheduleAsync()
    {
        try {
            while (true) {
                await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, scheduleShutdown.Token).ConfigureAwait(false);
                try { await RefreshScheduleAsync().ConfigureAwait(false); }
                catch (Exception) { lock (gate) { scheduleError = "The schedule could not update downloads. Check storage and task errors."; observedWindow = null; } }
            }
        } catch (OperationCanceledException) when (scheduleShutdown.IsCancellationRequested) { }
    }
}
