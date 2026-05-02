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
    public async Task AislePilotStylesheet_StackedDayMode_UsesEditorialListWithSidePreview()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-tabs\s*\{[\s\S]*display:\s*grid;",
                RegexOptions.IgnoreCase),
            css);
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
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s*clamp\(10\.5rem,\s*22vw,\s*13rem\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-day-meal-track\s*>\s*\.aislepilot-day-meal-panel\s*\{[\s\S]*align-items:\s*start;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-meal-image-shell\s*\{[\s\S]*min-height:\s*9\.8rem;",
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
            "grid-template-areas: \"image\";",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*981px\)\s*\{[\s\S]*\.aislepilot-day-carousel\[data-day-stacked-mode=""true""\]:not\(\[data-day-reorder-mode=""true""\]\)\s+\.aislepilot-meal-details-image-toggle\s*>\s*\.aislepilot-meal-image-summary\s*\{[\s\S]*pointer-events:\s*none;",
                RegexOptions.IgnoreCase),
            css);

        var script = await GetCombinedAislePilotScriptAsync(client);
        Assert.Contains("const ensureStackedInspectorTabs = detailsPanel => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const isCompactStackedInspectorModeForCard = card =>", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const compactStackedInspectorQuery = \"(max-width: 980px), (pointer: coarse)\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("window.matchMedia(compactStackedInspectorQuery).matches", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const isCompactStackedInspectorViewport = () => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const resetCompactStackedInlineDetailsTouchState = scope => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const syncMealSectionExpansionStateForScope = (scope, shouldExpand) => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resetCompactStackedInlineDetailsTouchState(carousel);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("syncMealSectionExpansionStateForScope(carousel, isStacked);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const shouldShowInlineDetails = isCompactStackedCardPresentation", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const shouldExpandMealSections = isStackedInspectorModeForCard(owningCard);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("section.node.open = shouldExpandMealSections;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("} else if (isVisible && inlineDetailsPanel instanceof HTMLElement) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("} else if (isActive && inlineDetailsPanel instanceof HTMLElement) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("summary.addEventListener(\"click\", event => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (!isCompactStackedInspectorModeForCard(card))", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const isHidden = detailsPanel.hasAttribute(\"hidden\")", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toggle.open = isHidden;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inlineToggle.dataset.inlineDetailsTouched !== \"true\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toggle.dataset.inlineDetailsTouched = \"true\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sections = [", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{ key: \"overview\", label: \"Overview\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{ key: \"nutrition\", label: \"Macros\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activateInspectorTab(\"overview\")", script, StringComparison.OrdinalIgnoreCase);
    }
}
