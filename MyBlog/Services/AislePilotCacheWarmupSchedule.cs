namespace MyBlog.Services;

public static class AislePilotCacheWarmupSchedule
{
    public static TimeSpan CalculateInitialDelay(TimeSpan maximumJitter, double sample)
    {
        var boundedJitter = TimeSpan.FromSeconds(Math.Clamp(maximumJitter.TotalSeconds, 0, 600));
        var boundedSample = Math.Clamp(sample, 0, 1);
        return TimeSpan.FromMilliseconds(boundedJitter.TotalMilliseconds * boundedSample);
    }

    public static TimeSpan CalculateRecurringDelay(
        TimeSpan interval,
        TimeSpan maximumJitter,
        double sample)
    {
        var boundedInterval = TimeSpan.FromMinutes(Math.Clamp(interval.TotalMinutes, 1, 1440));
        var boundedJitter = TimeSpan.FromSeconds(Math.Clamp(maximumJitter.TotalSeconds, 0, 600));
        var boundedSample = Math.Clamp(sample, 0, 1);
        var offsetMilliseconds = (boundedSample * 2 - 1) * boundedJitter.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(Math.Max(
            TimeSpan.FromSeconds(1).TotalMilliseconds,
            boundedInterval.TotalMilliseconds + offsetMilliseconds));
    }
}
