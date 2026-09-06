using MyBlog.Services;

namespace MyBlog.Tests;

public sealed class AislePilotCacheWarmupScheduleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5, 30)]
    [InlineData(1, 60)]
    public void CalculateInitialDelay_StaggersStartupWithinConfiguredWindow(
        double sample,
        double expectedSeconds)
    {
        var delay = AislePilotCacheWarmupSchedule.CalculateInitialDelay(
            TimeSpan.FromSeconds(60),
            sample);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 3);
    }

    [Theory]
    [InlineData(0, 540)]
    [InlineData(0.5, 600)]
    [InlineData(1, 660)]
    public void CalculateRecurringDelay_AppliesSymmetricJitter(
        double sample,
        double expectedSeconds)
    {
        var delay = AislePilotCacheWarmupSchedule.CalculateRecurringDelay(
            TimeSpan.FromMinutes(10),
            TimeSpan.FromSeconds(60),
            sample);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 3);
    }

    [Fact]
    public void CalculateRecurringDelay_ClampsUnsafeConfiguration()
    {
        var delay = AislePilotCacheWarmupSchedule.CalculateRecurringDelay(
            TimeSpan.Zero,
            TimeSpan.FromHours(1),
            sample: 0);

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
    }
}
