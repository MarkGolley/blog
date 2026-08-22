using System.Text.Json;

namespace MyBlog.Tests;

public sealed class LighthouseConfigurationTests
{
    [Theory]
    [InlineData("lighthouserc.mobile.json", "mobile", 5000)]
    [InlineData("lighthouserc.desktop.json", "desktop", 3000)]
    public void Configuration_DefinesCoreWebVitalsBudgets(string fileName, string formFactor, int lcpBudget)
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, fileName)));
        var ci = config.RootElement.GetProperty("ci");
        var settings = ci.GetProperty("collect").GetProperty("settings");
        var assertions = ci.GetProperty("assert").GetProperty("assertions");

        Assert.Equal(formFactor, settings.GetProperty("formFactor").GetString());
        Assert.Equal(lcpBudget, assertions.GetProperty("largest-contentful-paint")[1].GetProperty("maxNumericValue").GetInt32());
        Assert.Equal(0.1, assertions.GetProperty("cumulative-layout-shift")[1].GetProperty("maxNumericValue").GetDouble());
        Assert.True(assertions.TryGetProperty("total-blocking-time", out _));
    }

    [Fact]
    public void Runner_EnforcesUnexpectedThirdPartyRequests()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "run-aislepilot-lighthouse.ps1"));
        var budget = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "check-lighthouse-third-parties.ps1"));
        Assert.Contains("check-lighthouse-third-parties.ps1", runner, StringComparison.Ordinal);
        Assert.Contains("Unexpected third-party hosts", budget, StringComparison.Ordinal);
    }

    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
