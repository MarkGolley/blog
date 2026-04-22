namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_Get_ReferencesOverviewBalanceStylesheet()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("/css/aisle-pilot-overview-balance.css", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_OverviewBalance_RemovesDesktopSupermarketRowSpan()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await client.GetStringAsync("/css/aisle-pilot-overview-balance.css");

        Assert.Contains(".aislepilot-budget-grid--primary > .aislepilot-stat-card--supermarket", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grid-row: span 1;", css, StringComparison.OrdinalIgnoreCase);
    }
}
