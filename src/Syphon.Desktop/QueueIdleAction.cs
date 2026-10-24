namespace Syphon.Desktop;

// Policy only: tests can exercise countdowns without invoking a system power API.
public sealed class QueueIdleAction
{
    private bool sawWork;
    public DateTimeOffset? DueAt { get; private set; }
    public DateTimeOffset? Observe(bool busy, bool blocked, bool completed, DateTimeOffset now, int countdownSeconds)
    {
        if (busy) { sawWork = true; DueAt = null; }
        else if (blocked) { sawWork = false; DueAt = null; }
        else if (sawWork && completed) { DueAt ??= now.AddSeconds(Math.Clamp(countdownSeconds, 10, 300)); sawWork = false; }
        return DueAt;
    }
    public void Cancel() { sawWork = false; DueAt = null; }
}
