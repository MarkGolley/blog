using System.Text.Json;

namespace MyBlog.Tests;

public sealed class ObservabilityDashboardTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void OperationalDashboard_RecentFailuresPanel_FiltersForServerErrorsOrExceptions()
    {
        using var dashboard = LoadOperationalDashboard();
        var panel = dashboard.RootElement
            .GetProperty("panels")
            .EnumerateArray()
            .First(p => string.Equals(
                p.GetProperty("title").GetString(),
                "Recent Failures (Correlate To TraceId)",
                StringComparison.Ordinal));
        var query = panel
            .GetProperty("targets")[0]
            .GetProperty("expr")
            .GetString();

        Assert.Equal(
            "{service_name=~\"$service\"} |~ \"StatusCode=5[0-9]{2}|ExceptionType=[A-Za-z0-9_]+\"",
            query);
    }

    [Fact]
    public void OperationalDashboard_IncludesModerationAuthCacheAndBackgroundDurationPanels()
    {
        using var dashboard = LoadOperationalDashboard();
        var panels = dashboard.RootElement.GetProperty("panels").EnumerateArray().ToList();

        Assert.Contains(panels, p => string.Equals(
            p.GetProperty("title").GetString(),
            "Comment Moderation Outcomes (15m)",
            StringComparison.Ordinal));
        Assert.Contains(panels, p => string.Equals(
            p.GetProperty("title").GetString(),
            "Auth Events (15m)",
            StringComparison.Ordinal));
        Assert.Contains(panels, p => string.Equals(
            p.GetProperty("title").GetString(),
            "AislePilot Cache Hit Ratio (15m)",
            StringComparison.Ordinal));
        Assert.Contains(panels, p => string.Equals(
            p.GetProperty("title").GetString(),
            "Background Job P95 Duration (s)",
            StringComparison.Ordinal));
    }

    [Fact]
    public void OperationalDashboard_IncludesSloAlertAndFunnelPanels()
    {
        using var dashboard = LoadOperationalDashboard();
        var panels = dashboard.RootElement.GetProperty("panels").EnumerateArray().ToList();

        Assert.Contains(panels, p => string.Equals(
            p.GetProperty("title").GetString(),
            "SLO Success (30d, target 99.5%)",
            StringComparison.Ordinal));
        Assert.Contains(panels, p => string.Equals(
            p.GetProperty("title").GetString(),
            "Error Budget Remaining (30d %)",
            StringComparison.Ordinal));
        Assert.Contains(panels, p => string.Equals(
            p.GetProperty("title").GetString(),
            "Error Budget Burn Rate (5m)",
            StringComparison.Ordinal));
        Assert.Contains(panels, p => string.Equals(
            p.GetProperty("title").GetString(),
            "Active Alerts (Prometheus)",
            StringComparison.Ordinal));
        Assert.Contains(panels, p => string.Equals(
            p.GetProperty("title").GetString(),
            "AislePilot User Journey Funnel (30m)",
            StringComparison.Ordinal));
    }

    [Fact]
    public void OperationalDashboard_AislePilotFunnelPanel_UsesHttpMethodDimensionForStageQueries()
    {
        using var dashboard = LoadOperationalDashboard();
        var funnelPanel = dashboard.RootElement
            .GetProperty("panels")
            .EnumerateArray()
            .First(p => string.Equals(
                p.GetProperty("title").GetString(),
                "AislePilot User Journey Funnel (30m)",
                StringComparison.Ordinal));
        var queries = funnelPanel
            .GetProperty("targets")
            .EnumerateArray()
            .Select(target => target.GetProperty("expr").GetString() ?? string.Empty)
            .ToList();

        Assert.Contains(queries, query => query.Contains("method=\"GET\"", StringComparison.Ordinal));
        Assert.True(queries.Count(query => query.Contains("method=\"POST\"", StringComparison.Ordinal)) >= 3);
    }

    [Fact]
    public void OperationalDashboard_IncludesAislePilotPlanStageAndBrowserJourneyPanels()
    {
        using var dashboard = LoadOperationalDashboard();
        var panels = dashboard.RootElement.GetProperty("panels").EnumerateArray().ToList();

        var planPanel = Assert.Single(panels, panel => string.Equals(
            panel.GetProperty("title").GetString(),
            "AislePilot Plan Stage Percentiles (s)",
            StringComparison.Ordinal));
        var browserPanel = Assert.Single(panels, panel => string.Equals(
            panel.GetProperty("title").GetString(),
            "AislePilot Browser Journey Percentiles (ms)",
            StringComparison.Ordinal));

        var planQueries = planPanel.GetProperty("targets").EnumerateArray()
            .Select(target => target.GetProperty("expr").GetString() ?? string.Empty).ToList();
        var browserQueries = browserPanel.GetProperty("targets").EnumerateArray()
            .Select(target => target.GetProperty("expr").GetString() ?? string.Empty).ToList();
        Assert.Contains(planQueries, query => query.Contains("histogram_quantile(0.50", StringComparison.Ordinal));
        Assert.Contains(planQueries, query => query.Contains("histogram_quantile(0.95", StringComparison.Ordinal));
        Assert.Contains(planQueries, query => query.Contains("histogram_quantile(0.99", StringComparison.Ordinal));
        Assert.Contains(browserQueries, query => query.Contains("histogram_quantile(0.50", StringComparison.Ordinal));
        Assert.Contains(browserQueries, query => query.Contains("histogram_quantile(0.75", StringComparison.Ordinal));
        Assert.Contains(browserQueries, query => query.Contains("histogram_quantile(0.95", StringComparison.Ordinal));
        Assert.Contains(browserQueries, query => query.Contains("histogram_quantile(0.99", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationalDashboard_ExposesBoundedReplenishmentProfilesAndPlanSources()
    {
        using var dashboard = LoadOperationalDashboard();
        var panels = dashboard.RootElement.GetProperty("panels").EnumerateArray().ToList();

        var replenishmentPanel = Assert.Single(panels, panel => string.Equals(
            panel.GetProperty("title").GetString(),
            "AislePilot Replenishment by Request Profile (30m)",
            StringComparison.Ordinal));
        var sourcePanel = Assert.Single(panels, panel => string.Equals(
            panel.GetProperty("title").GetString(),
            "AislePilot Plan Sources (30m)",
            StringComparison.Ordinal));
        var replenishmentQuery = replenishmentPanel.GetProperty("targets")[0]
            .GetProperty("expr").GetString() ?? string.Empty;
        var sourceQuery = sourcePanel.GetProperty("targets")[0]
            .GetProperty("expr").GetString() ?? string.Empty;

        Assert.Contains("dietary_complexity", replenishmentQuery, StringComparison.Ordinal);
        Assert.Contains("meal_slot_count", replenishmentQuery, StringComparison.Ordinal);
        Assert.Contains("plan_length", replenishmentQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("allergen", replenishmentQuery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pantry", replenishmentQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plans_completed", sourceQuery, StringComparison.Ordinal);
        Assert.Contains("sum by (source)", sourceQuery, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationalDashboard_ExposesManagedQueueThroughputDepthAndWaitTime()
    {
        using var dashboard = LoadOperationalDashboard();
        var panels = dashboard.RootElement.GetProperty("panels").EnumerateArray().ToList();
        var throughput = Assert.Single(panels, panel => string.Equals(
            panel.GetProperty("title").GetString(),
            "AislePilot Managed Queue Throughput (15m)",
            StringComparison.Ordinal));
        var health = Assert.Single(panels, panel => string.Equals(
            panel.GetProperty("title").GetString(),
            "AislePilot Managed Queue Depth and P95 Wait",
            StringComparison.Ordinal));

        var throughputQuery = throughput.GetProperty("targets")[0].GetProperty("expr").GetString() ?? string.Empty;
        var healthQueries = health.GetProperty("targets").EnumerateArray()
            .Select(target => target.GetProperty("expr").GetString() ?? string.Empty)
            .ToList();
        Assert.Contains("background_queue_events", throughputQuery, StringComparison.Ordinal);
        Assert.Contains("sum by (job, event)", throughputQuery, StringComparison.Ordinal);
        Assert.Contains(healthQueries, query => query.Contains("queue_depth", StringComparison.Ordinal));
        Assert.Contains(healthQueries, query => query.Contains("background_queue_wait", StringComparison.Ordinal));
        Assert.Contains(healthQueries, query => query.Contains("histogram_quantile(0.95", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationalDashboard_ExposesCacheRefreshFreshnessAndOutcomes()
    {
        using var dashboard = LoadOperationalDashboard();
        var panels = dashboard.RootElement.GetProperty("panels").EnumerateArray().ToList();
        var agePanel = Assert.Single(panels, panel => string.Equals(
            panel.GetProperty("title").GetString(),
            "AislePilot Cache Refresh Age (s)",
            StringComparison.Ordinal));
        var outcomePanel = Assert.Single(panels, panel => string.Equals(
            panel.GetProperty("title").GetString(),
            "AislePilot Cache Refresh Outcomes (30m)",
            StringComparison.Ordinal));
        var ageQuery = agePanel.GetProperty("targets")[0].GetProperty("expr").GetString() ?? string.Empty;
        var outcomeQuery = outcomePanel.GetProperty("targets")[0].GetProperty("expr").GetString() ?? string.Empty;

        Assert.Contains("cache_refresh_age", ageQuery, StringComparison.Ordinal);
        Assert.Contains("cache, outcome", ageQuery, StringComparison.Ordinal);
        Assert.Contains("cache_refreshes", outcomeQuery, StringComparison.Ordinal);
        Assert.Contains("sum by (cache, outcome)", outcomeQuery, StringComparison.Ordinal);
    }

    private static JsonDocument LoadOperationalDashboard()
    {
        var dashboardPath = Path.Combine(
            RepoRoot,
            "Deployment",
            "observability",
            "grafana",
            "dashboards",
            "myblog-operational-overview.json");

        return JsonDocument.Parse(File.ReadAllText(dashboardPath));
    }
}
