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
