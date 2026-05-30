namespace MyBlog.Tests;

public sealed class ObservabilityAlertingConfigurationTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    [Fact]
    public void PrometheusConfig_LoadsMyBlogAlertRuleFile()
    {
        var prometheusConfigPath = Path.Combine(
            RepoRoot,
            "Deployment",
            "observability",
            "prometheus",
            "prometheus.yml");
        var config = File.ReadAllText(prometheusConfigPath);

        Assert.Contains("rule_files:", config);
        Assert.Contains("/etc/prometheus/rules/myblog-alerts.yml", config);
    }

    [Fact]
    public void DockerCompose_MountsPrometheusRuleDirectory()
    {
        var composePath = Path.Combine(
            RepoRoot,
            "Deployment",
            "observability",
            "docker-compose.yml");
        var compose = File.ReadAllText(composePath);

        Assert.Contains("./prometheus/rules:/etc/prometheus/rules:ro", compose);
    }

    [Fact]
    public void AlertRules_DefineSloBurnLatencyAndBackgroundFailureAlerts()
    {
        var alertRulesPath = Path.Combine(
            RepoRoot,
            "Deployment",
            "observability",
            "prometheus",
            "rules",
            "myblog-alerts.yml");
        var rules = File.ReadAllText(alertRulesPath);

        Assert.Contains("alert: MyBlogSloFastBurn", rules);
        Assert.Contains("alert: MyBlogSloSlowBurn", rules);
        Assert.Contains("alert: MyBlogHighP95Latency", rules);
        Assert.Contains("alert: MyBlogBackgroundJobFailures", rules);
        Assert.Contains("myblog:slo_error_budget_burn_rate:5m > 14.4", rules);
        Assert.Contains("myblog:slo_error_budget_burn_rate:30m > 6", rules);
    }
}
