namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task AislePilotStylesheet_CompactResultHeader_HighlightsUseSecondaryPillStyling()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Contains(".aislepilot-app-head.is-compact .aislepilot-app-highlights {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-app-head.is-compact .aislepilot-app-highlights li {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("color: var(--ap-refresh-text-muted);", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("font-size: 0.78rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("border-color: var(--ap-refresh-border-strong);", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_ResultHeader_DoesNotRepeatWeeklySummaryPills()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Contains(
            ".aislepilot-app.has-results .aislepilot-app-head.is-compact .aislepilot-app-highlights",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            @"\.aislepilot-app\.has-results\s+\.aislepilot-app-head\.is-compact\s+\.aislepilot-app-highlights\s*\{\s*display:\s*none;",
            css);
    }
}
