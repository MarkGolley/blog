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
}
