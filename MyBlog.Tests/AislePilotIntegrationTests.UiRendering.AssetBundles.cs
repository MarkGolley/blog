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
            "/css/aisle-pilot-overview-balance.css"
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
    public async Task AislePilotStylesheet_StackedDayMode_UsesEditorialListWithSidePreview()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-tabs\s*\{[\s\S]*display:\s*grid;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Contains(
            ".aislepilot-day-carousel[data-day-stacked-mode=\"true\"]:not([data-day-reorder-mode=\"true\"]) .aislepilot-day-meal-tabs[data-day-meal-tab-count=\"1\"]",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ".aislepilot-day-carousel[data-day-stacked-mode=\"true\"]:not([data-day-reorder-mode=\"true\"]) .aislepilot-day-meal-tabs[data-day-meal-tab-count=\"2\"]",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ".aislepilot-day-carousel[data-day-stacked-mode=\"true\"]:not([data-day-reorder-mode=\"true\"]) .aislepilot-day-meal-tabs[data-day-meal-tab-count=\"3\"]",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-tabs\s*\{[\s\S]*max-width:\s*calc\(100%\s*-\s*clamp\(12\.5rem,\s*26vw,\s*16rem\)\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card-body\s*\{[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*\{[\s\S]*display:\s*block;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*grid-template-areas:\s*[\s\S]*""details image"";",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s*clamp\(12\.5rem,\s*26vw,\s*16rem\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-meal-image-shell\s*\{[\s\S]*min-height:\s*12\.2rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*align-items:\s*stretch;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*padding:\s*0;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*background:\s*transparent;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*overflow:\s*visible;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-meal-image-summary\s*\{[\s\S]*height:\s*100%;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-slider\s*\{[\s\S]*margin-top:\s*-0\.88rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-panel\s*>\s*h3\s*\{[\s\S]*display:\s*none;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-summary\s*\{[\s\S]*display:\s*none;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-panel\[aria-hidden=""true""\]\s*\{[\s\S]*display:\s*none;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-panel\s*>\s*\.aislepilot-meal-details-panel\s*\{[\s\S]*grid-template-columns:\s*clamp\(5\.8rem,\s*9vw,\s*7rem\)\s*minmax\(0,\s*1fr\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-panel\s*>\s*\.aislepilot-meal-details-panel\s*\{[\s\S]*margin-top:\s*0\.88rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-tabs\s*\{[\s\S]*flex-direction:\s*column;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Contains(".aislepilot-inspector-tab.is-active", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-inspector-panel.is-active", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-inspector-panel :is(.aislepilot-card-reason, .aislepilot-meal-section-content, .aislepilot-meal-section-content :is(.aislepilot-nutrition-list, .case-list), .aislepilot-meal-section-content :is(.aislepilot-nutrition-list, .case-list) li)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("font-size: 0.76rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*760px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card-body\s*\{[\s\S]*grid-template-columns:\s*1fr;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*760px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-tab\s*\{[\s\S]*grid-template-areas:\s*[\s\S]*""eyebrow meta""[\s\S]*""title title"";",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*760px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-tab\s*\{[\s\S]*min-height:\s*2\.9rem\s*!important;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*760px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-panel\s*>\s*\.aislepilot-meal-details-panel\s*\{[\s\S]*margin-top:\s*0\.24rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.DoesNotMatch(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-summary\s*\{[^}]*display:\s*flex;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*760px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-tab\s*\{[\s\S]*font-size:\s*0\.66rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*760px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-panel\s+\.aislepilot-card-reason\s*\{[\s\S]*-webkit-line-clamp:\s*unset;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-meal-details-image-toggle\s*>\s*\.aislepilot-meal-image-summary\s*\{[\s\S]*pointer-events:\s*auto;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*760px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-meal-details-image-toggle\s*>\s*\.aislepilot-meal-image-summary\s*\{[\s\S]*pointer-events:\s*auto;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-reorder-toggle,\s*\.aislepilot-day-view-toggle\s*\{[\s\S]*min-height:\s*2\.2rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Contains(
            ".aislepilot-day-reorder-toggle[aria-pressed=\"true\"]",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "background: linear-gradient(135deg, var(--ap-refresh-primary, #0f6d78) 0%, var(--ap-refresh-primary-strong, #103f65) 100%);",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ":root[data-theme=\"dark\"] .aislepilot-day-reorder-toggle[aria-pressed=\"true\"]",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "background: linear-gradient(135deg, rgb(var(--ap-refresh-primary-rgb, 46 141 171) / 0.9), rgb(var(--ap-refresh-primary-strong-rgb, 31 85 124) / 0.96));",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s*\{[\s\S]*--ap-stacked-media-column-width:\s*clamp\(11rem,\s*20vw,\s*12\.4rem\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-tabs\s*\{[\s\S]*max-width:\s*calc\(100%\s*-\s*clamp\(11rem,\s*20vw,\s*12\.4rem\)\s*-\s*0\.8rem\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s*var\(--ap-stacked-media-column-width\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*align-items:\s*start;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-meal-image-shell\s*\{[\s\S]*min-height:\s*8\.8rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-panel\s*>\s*\.aislepilot-meal-details-panel\s*\{[\s\S]*min-height:\s*0;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-tabs\s*\{[\s\S]*flex-direction:\s*row;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-panels\s*\{[\s\S]*min-height:\s*0;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-panel\.is-active\s*\{[\s\S]*min-height:\s*0;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Contains(
            ".aislepilot-day-meal-panel:has(> .aislepilot-meal-details-panel[hidden])",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ".aislepilot-day-card:not([data-day-card-expanded=\"true\"]) .aislepilot-day-meal-slider",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ".aislepilot-day-card[data-day-card-expanded=\"true\"] .aislepilot-day-meal-slider",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card:not\(\[data-day-card-expanded=""true""\]\)\s+\.aislepilot-day-meal-tabs\s*\{[\s\S]*max-width:\s*100%;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card:not\(\[data-day-card-expanded=""true""\]\)\s+\.aislepilot-day-meal-tab-meta\s*\{[\s\S]*display:\s*none;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Contains(
            "grid-template-areas: \"image\";",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-tabs\s*\{[\s\S]*border:\s*none;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-panel\s*>\s*\.aislepilot-meal-details-panel\s*\{[\s\S]*border:\s*none;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-panels\s*\{[\s\S]*border:\s*1px\s*solid\s*rgba\(15,\s*23,\s*42,\s*0\.11\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-meal-details-image-toggle\s*>\s*\.aislepilot-meal-image-summary\s*\{[\s\S]*pointer-events:\s*none;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s*\{[\s\S]*--ap-stacked-panel-radius:\s*12px;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-carousel-status\s*\{[\s\S]*display:\s*inline-flex;[\s\S]*border-radius:\s*999px;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-card\[data-day-card-expanded=""true""\]\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*border:\s*1px\s*solid\s*rgba\(15,\s*23,\s*42,\s*0\.13\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-card\[data-day-card-expanded=""true""\]\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*background:\s*linear-gradient\(180deg,\s*rgba\(251,\s*253,\s*255,\s*0\.95\),\s*rgba\(246,\s*250,\s*253,\s*0\.9\)\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-panels\s*\{[\s\S]*padding:\s*0\.8rem\s*0\.86rem;",
                RegexOptions.IgnoreCase),
            css);

        var script = await GetCombinedAislePilotScriptAsync(client);
        Assert.Contains("const ensureStackedInspectorTabs = detailsPanel => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const syncDayMealTabCountForCard = card => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tabsRoot.dataset.dayMealTabCount = `${normalizedCount}`;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (tabs.length <= 1 || panels.length <= 1) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("card.dataset.dayCardExpanded = \"true\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("singlePanel.setAttribute(\"aria-hidden\", \"false\");", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const stackedInspectorTabInteractionState = new WeakSet();", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const wireStackedInspectorTabInteractions = (detailsPanel, tabButtons, tabPanels) => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (detailsPanel.dataset.inspectorTabsWired === \"true\") {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wireStackedInspectorTabInteractions(detailsPanel, existingButtons, existingPanels);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const isCompactStackedInspectorModeForCard = card =>", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const compactStackedInspectorQuery = \"(max-width: 980px), (pointer: coarse)\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("window.matchMedia(compactStackedInspectorQuery).matches", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const isCompactStackedInspectorViewport = () => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const resetCompactStackedInlineDetailsTouchState = scope => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const syncMealSectionExpansionStateForScope = (scope, shouldExpand) => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resetCompactStackedInlineDetailsTouchState(carousel);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("syncMealSectionExpansionStateForScope(carousel, isStacked);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const dayMealExpandedState = new Map();", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("let expandedStackedDayKey = \"\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const resetRememberedDayMealExpandedState = scope => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const collapseOtherStackedDayCards = activeCard => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const rememberDayMealExpandedState = scope => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const shouldShowInlineDetails = isStackedCardExpanded", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const shouldExpandMealSections = isStackedInspectorModeForCard(owningCard);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("section.node.open = shouldExpandMealSections;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("} else if (isVisible && inlineDetailsPanel instanceof HTMLElement) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("} else if (isActive && inlineDetailsPanel instanceof HTMLElement) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("summary.addEventListener(\"click\", event => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (!isStackedInspectorModeForCard(card))", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const isHidden = detailsPanel.hasAttribute(\"hidden\")", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setRememberedDayMealExpanded(card, isHidden);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("collapseOtherStackedDayCards(card);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const shouldExpand = !(isSameSlot && isExpanded);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shouldExpandStacked: shouldExpand", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("collapseOtherStackedCards: shouldExpand", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toggle.dataset.inlineDetailsTouched = \"true\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sections = [", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{ key: \"overview\", label: \"Overview\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{ key: \"nutrition\", label: \"Macros\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const initialKey =", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activateInspectorTab(initialKey);", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_StackedDayMode_LaunchPolish_IsScopedToStackedSelectors()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var refreshCss = await client.GetStringAsync("/css/aisle-pilot-refresh.css");

        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s*\{[\s\S]*--ap-stacked-media-column-width:\s*clamp\(11\.8rem,\s*22vw,\s*13\.8rem\);",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card-head-main\s+\.aislepilot-day-card-meta\s*\{[\s\S]*min-height:\s*2\.08rem;",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-panel\s+:is\(\.aislepilot-card-reason,\s*\.aislepilot-meal-section-content,\s*\.aislepilot-meal-section-content\s+:is\(\.aislepilot-nutrition-list,\s*\.case-list\),\s*\.aislepilot-meal-section-content\s+:is\(\.aislepilot-nutrition-list,\s*\.case-list\)\s+li\)\s*\{[\s\S]*font-size:\s*0\.81rem;",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-tab\s*\{[\s\S]*border-radius:\s*6px\s*!important;",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-tab\.is-active\s*\{[\s\S]*inset\s+3px\s+0\s+0\s+rgb\(var\(--ap-refresh-primary-rgb\)\s*/\s*0\.92\)\s*,",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-inspector-tab\.is-active\s*\{[\s\S]*inset\s+3px\s+0\s+0\s+rgb\(var\(--ap-refresh-primary-rgb\)\s*/\s*0\.9\)\s*,",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card\[data-day-card-expanded=""true""\]\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*border:\s*none;[\s\S]*background:\s*transparent;",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+:is\(\.aislepilot-day-reorder-toggle,\s*\.aislepilot-day-view-toggle\)\s*\{[\s\S]*border-radius:\s*8px;",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card\[data-day-card-expanded=""true""\]\s+\.aislepilot-meal-image-shell\s*\{[\s\S]*max-height:\s*10\.4rem;",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card\[data-day-card-expanded=""true""\]\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*grid-template-areas:\s*[\s\S]*""details title""[\s\S]*""details image"";",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card\[data-day-card-expanded=""true""\]\s+\.aislepilot-day-meal-panel\s*>\s*h3\s*\{[\s\S]*display:\s*block;",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-card\[data-day-card-expanded=""true""\]\s+\.aislepilot-meal-image-hint\s*\{[\s\S]*display:\s*none;",
                RegexOptions.IgnoreCase),
            refreshCss);
        Assert.Contains(
            "right: calc(var(--ap-stacked-media-column-width) + var(--ap-stacked-column-gap) + 0.08rem);",
            refreshCss,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ".aislepilot-day-carousel:not([data-day-stacked-mode=\"true\"]):not([data-day-reorder-mode=\"true\"]) .aislepilot-inspector-tabs",
            refreshCss,
            StringComparison.OrdinalIgnoreCase);
    }
}
