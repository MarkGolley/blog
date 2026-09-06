using System.Text.RegularExpressions;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    private static async Task<string> GetCombinedAislePilotCssAsync(HttpClient client)
    {
        var assetPaths = new[]
        {
            "/css/aisle-pilot-shell.css",
            "/css/aisle-pilot-overview.css",
            "/css/aisle-pilot-setup.css",
            "/css/aisle-pilot-results.css",
            "/css/aisle-pilot-actions.css",
            "/css/aisle-pilot-responsive.css",
            "/css/aisle-pilot-dark.css",
            "/css/aisle-pilot-refresh.css",
            "/css/aisle-pilot-header-compact.css",
            "/css/aisle-pilot-overview-balance.css",
            "/css/aisle-pilot-kitchen.css",
            "/css/aisle-pilot-kitchen-results.css"
        };

        var cssChunks = new List<string>(assetPaths.Length);
        foreach (var assetPath in assetPaths)
        {
            cssChunks.Add(await client.GetStringAsync(assetPath));
        }

        return string.Join(Environment.NewLine, cssChunks);
    }

    private static async Task<string> GetCombinedAislePilotScriptAsync(HttpClient client)
    {
        var assetPaths = new[]
        {
            "/js/aisle-pilot/core.js",
            "/js/aisle-pilot/action-menus.js",
            "/js/aisle-pilot/shopping.js",
            "/js/aisle-pilot.js"
        };

        var scriptChunks = new List<string>(assetPaths.Length);
        foreach (var assetPath in assetPaths)
        {
            scriptChunks.Add(await client.GetStringAsync(assetPath));
        }

        return string.Join(Environment.NewLine, scriptChunks);
    }

    [Fact]
    public async Task AislePilotActions_ProvideImmediatePressedAndSubmittingFeedback()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);
        var script = await GetCombinedAislePilotScriptAsync(client);

        Assert.Contains(".aislepilot-app :where(button:not(:disabled), a[href], summary):active", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filter: brightness(0.86);", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const planLoadingShellDelayMs = 0;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setSubmitButtonLoadingState(submitButton);", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_RefreshPalette_UsesLogoBrandTokensInsteadOfLegacyGreen()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var refreshCss = await client.GetStringAsync("/css/aisle-pilot-refresh.css");
        Assert.Contains("--ap-refresh-primary: #0f6d78;", refreshCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-refresh-primary-strong: #103f65;", refreshCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-refresh-primary-rgb: 15 109 120;", refreshCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-refresh-primary-strong-rgb: 16 63 101;", refreshCss, StringComparison.OrdinalIgnoreCase);

        var css = await GetCombinedAislePilotCssAsync(client);
        Assert.DoesNotContain("#15724f", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#115c4e", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("21 114 79", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("17 92 78", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_WeeklyStatusMenu_UsesCurrentThemeTokens()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Contains(":root[data-theme] .aislepilot-app .aislepilot-overview-actions-trigger", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("color: var(--ap-refresh-accent);", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background: var(--ap-refresh-surface-strong);", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_PrimaryResultsTabs_UseSingleAlignedDivider()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Contains(":root[data-theme] .aislepilot-app .aislepilot-window-tabs", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("border: 0;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("border-bottom: 1px solid var(--ap-refresh-border);", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("width: 100%;", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_MealTitles_UseAvailableCardWidth()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var kitchenCss = await client.GetStringAsync("/css/aisle-pilot-kitchen-results.css");

        Assert.Contains(".aislepilot-app .aislepilot-day-meal-title", kitchenCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("width: 100%;", kitchenCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-width: none;", kitchenCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("padding-right: 0;", kitchenCss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_MealPhotoLoader_UsesDelayedRevealAndCrossFade()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var kitchenCss = await client.GetStringAsync("/css/aisle-pilot-kitchen-results.css");

        Assert.Contains("transition: opacity 420ms cubic-bezier(0.22, 1, 0.36, 1);", kitchenCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-meal-image-loading 2.4s ease-in-out infinite", kitchenCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-meal-image-loader-reveal 320ms ease-out 160ms both", kitchenCss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@keyframes aislepilot-meal-image-loader-reveal", kitchenCss, StringComparison.OrdinalIgnoreCase);
    }


}
