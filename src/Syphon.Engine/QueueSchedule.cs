namespace Syphon.Engine;

public sealed record QueueSchedule(bool Enabled = false, int StartMinute = 0, int StopMinute = 360)
{
    public void Validate()
    {
        if (StartMinute is < 0 or > 1439 || StopMinute is < 0 or > 1439 || Enabled && StartMinute == StopMinute)
            throw new ArgumentException("Choose different start and stop times within the day.");
    }
    public bool IsOpen(DateTimeOffset localTime)
    {
        if (!Enabled) return true;
        var minute = localTime.Hour * 60 + localTime.Minute;
        return StartMinute < StopMinute ? minute >= StartMinute && minute < StopMinute : minute >= StartMinute || minute < StopMinute;
    }
}
