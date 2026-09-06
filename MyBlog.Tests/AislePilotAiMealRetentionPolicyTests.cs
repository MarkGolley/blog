using MyBlog.Services;

namespace MyBlog.Tests;

public sealed class AislePilotAiMealRetentionPolicyTests
{
    [Fact]
    public void ShouldReuse_UsesMealsWithinThirtyDayWindow()
    {
        var nowUtc = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var retention = TimeSpan.FromDays(30);

        Assert.True(AislePilotAiMealRetentionPolicy.ShouldReuse(nowUtc.AddDays(-29), nowUtc, retention));
        Assert.True(AislePilotAiMealRetentionPolicy.ShouldReuse(nowUtc.AddDays(-30), nowUtc, retention));
        Assert.False(AislePilotAiMealRetentionPolicy.ShouldReuse(nowUtc.AddDays(-31), nowUtc, retention));
        Assert.False(AislePilotAiMealRetentionPolicy.ShouldReuse(default, nowUtc, retention));
        Assert.False(AislePilotAiMealRetentionPolicy.ShouldReuse(nowUtc.AddMinutes(1), nowUtc, retention));
    }
}
