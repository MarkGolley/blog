namespace MyBlog.Services;

public sealed record AislePilotBackgroundJobPolicy(
    string JobName,
    bool RequiresDurableQueue,
    string RecoveryTrigger);

public static class AislePilotBackgroundJobCatalog
{
    private static readonly IReadOnlyDictionary<string, AislePilotBackgroundJobPolicy> Policies =
        new Dictionary<string, AislePilotBackgroundJobPolicy>(StringComparer.Ordinal)
        {
            ["plan_pool_replenishment"] = new(
                "plan_pool_replenishment", false, "A later plan request or scheduled cache warmup replenishes the pool."),
            ["special_treat_generation"] = new(
                "special_treat_generation", false, "A later eligible plan request queues another treat."),
            ["dessert_addon_recovery"] = new(
                "dessert_addon_recovery", false, "A later dessert request or cache warmup retries recovery."),
            ["meal_image_generation"] = new(
                "meal_image_generation", false, "A later image lookup observes the missing image and queues generation again."),
            ["ai_meal_persistence"] = new(
                "ai_meal_persistence", false, "The delivered plan remains valid; later generation can repopulate persistent meals."),
            ["supermarket_layout_hydration"] = new(
                "supermarket_layout_hydration", false, "A later layout request or scheduled cache warmup hydrates the cache."),
            ["supermarket_layout_refresh"] = new(
                "supermarket_layout_refresh", false, "A stale-cache request or scheduled cache warmup queues another refresh.")
        };

    public static IEnumerable<AislePilotBackgroundJobPolicy> All => Policies.Values;

    public static bool TryGet(string jobName, out AislePilotBackgroundJobPolicy policy)
    {
        return Policies.TryGetValue(jobName, out policy!);
    }
}
