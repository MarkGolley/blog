namespace MyBlog.Services;

public static class AislePilotAiMealRetentionPolicy
{
    public static bool ShouldReuse(DateTime createdAtUtc, DateTime nowUtc, TimeSpan retention)
    {
        if (createdAtUtc == default || retention <= TimeSpan.Zero)
        {
            return false;
        }

        return createdAtUtc >= nowUtc - retention && createdAtUtc <= nowUtc;
    }
}
