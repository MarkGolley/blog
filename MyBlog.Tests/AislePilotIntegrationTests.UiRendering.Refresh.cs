using System.Text.RegularExpressions;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_Get_RendersStreamlinedHeaderWithPrimarySettingsAction()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("class=\"aislepilot-app-head-main\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-app-head-topline\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-head-primary-action\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"#aislepilot-setup\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start with settings", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-head-action-note\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_RendersSetupSnapshotAndGuidedModeChoices()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.DoesNotContain("class=\"aislepilot-app-head-metrics\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-setup-track\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Choose your output", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Best for weekly shops", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Best for use-what-you-have", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-setup-mode-toggle-detail\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<div class=""aislepilot-brand-row"">[\s\S]*?</div>\s*<div class=""aislepilot-app-head-content"">",
                RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task Index_Get_RendersGroupedDietarySelectorWithSelectionRules()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("data-dietary-selector", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-dietary-max-selections=\"2\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Core style", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Extra filter", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-dietary-group=\"core\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-dietary-group=\"overlay\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Choose up to 2.", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotRefreshStylesheet_UsesStackedHeroLayoutWithoutStretchingBrandPanel()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await client.GetStringAsync("/css/aisle-pilot-refresh.css");

        Assert.Matches(
            new Regex(
                @"\.app-shell-main\.aislepilot-main\.container\s*\{[\s\S]*padding-top:\s*0\.85rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-app-head\s*\{[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-app-head-main\s*\{[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-app-head-actions\s*\{[\s\S]*justify-self:\s*start;[\s\S]*width:\s*min\(100%,\s*15rem\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-brand-lockup\s*\{[\s\S]*min-height:\s*auto;[\s\S]*width:\s*100%;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-app-head-content\s*\{[\s\S]*min-width:\s*0;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-setup-mode-shell\s*\{[\s\S]*padding:\s*0;[\s\S]*border:\s*0;[\s\S]*background:\s*transparent;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-setup-mode-toggle:not\(\.is-active\)\s*\{[\s\S]*border-color:\s*rgba\(148,\s*163,\s*184,\s*0\.22\)",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*767px\)\s*\{[\s\S]*\.app-shell-main\.aislepilot-main\.container\s*\{[\s\S]*padding-top:\s*2\.35rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*767px\)\s*\{[\s\S]*\.aislepilot-collapsible\s*>\s*summary\s*\{[\s\S]*display:\s*grid;[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s*auto;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*767px\)\s*\{[\s\S]*\.aislepilot-collapsible-value\s*\{[\s\S]*grid-column:\s*1\s*/\s*-1;[\s\S]*text-align:\s*left;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*767px\)\s*\{[\s\S]*\.aislepilot-mobile-context\s*\{[\s\S]*top:\s*calc\(var\(--ap-mobile-context-top\)\s*\+\s*0\.38rem\);[\s\S]*padding:\s*0\.44rem\s*0\.46rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*767px\)\s*\{[\s\S]*\.aislepilot-day-carousel-controls\s*\{[\s\S]*display:\s*none;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*767px\)\s*\{[\s\S]*\.aislepilot-day-carousel-pagination\s*\{[\s\S]*padding:\s*0\s*0\.68rem\s*0\.16rem\s*0\.12rem;[\s\S]*scroll-padding-inline:\s*0\.68rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(max-width:\s*767px\)\s*\{[\s\S]*\.aislepilot-meal-details-image-toggle\s*>\s*\.aislepilot-meal-image-summary,\s*[\s\S]*\.aislepilot-meal-details-image-toggle\s+\.aislepilot-meal-image-shell\s*\{[\s\S]*touch-action:\s*pan-y\s+pinch-zoom;",
                RegexOptions.IgnoreCase),
            css);
        Assert.DoesNotContain(".aislepilot-app-head-metrics", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotRefreshStylesheet_DefinesSubBrandTypographyWithoutImportingFontsInline()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await client.GetStringAsync("/css/aisle-pilot-refresh.css");

        Assert.DoesNotContain("@import url('https://fonts.googleapis.com", css, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-app\s*\{[\s\S]*--ap-font-sans:\s*""Plus Jakarta Sans"",\s*""Segoe UI"",\s*sans-serif;[\s\S]*--brand:\s*var\(--ap-refresh-primary\);[\s\S]*--brand-deep:\s*var\(--ap-refresh-accent\);[\s\S]*font-family:\s*var\(--ap-font-sans\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-app\s*:where\([\s\S]*\.aislepilot-app-title,[\s\S]*\.aislepilot-window-tab,[\s\S]*\.aislepilot-day-carousel-dot,[\s\S]*\.aislepilot-head-menu-item,[\s\S]*\.aislepilot-head-menu-section-title[\s\S]*\)\s*\{[\s\S]*font-family:\s*var\(--ap-font-display\);",
                RegexOptions.IgnoreCase),
            css);
    }

    [Fact]
    public async Task Index_Post_RendersResultSnapshotCardsWithDesktopPriorityClasses()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "200"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "3"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "2"),
            new("Request.SelectedMealTypes", "Lunch"),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        }));

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(
            2,
            Regex.Matches(html, @"class=""aislepilot-stat-card aislepilot-stat-card--featured""", RegexOptions.IgnoreCase).Count);
        Assert.Contains(
            "class=\"aislepilot-stat-card aislepilot-stat-card--status is-on-budget\"",
            html,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "class=\"aislepilot-stat-card aislepilot-stat-card--support\"",
            html,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "class=\"aislepilot-stat-card aislepilot-stat-card--meta\"",
            html,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "class=\"aislepilot-stat-card aislepilot-stat-card--meta aislepilot-stat-card--supermarket\"",
            html,
            StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<article class=""aislepilot-stat-card aislepilot-stat-card--status is-on-budget"">[\s\S]*?<p class=""aislepilot-stat-label"">Budget difference</p>",
                RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task AislePilotRefreshStylesheet_AddsDesktopResultsHierarchyAndCarouselPolish()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await client.GetStringAsync("/css/aisle-pilot-refresh.css");

        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*68rem\)\s*\{[\s\S]*#aislepilot-overview\.aislepilot-shell\s*\{[\s\S]*padding:\s*1rem\s*1\.05rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*68rem\)\s*\{[\s\S]*\.aislepilot-budget-grid\s*\{[\s\S]*grid-template-columns:\s*repeat\(12,\s*minmax\(0,\s*1fr\)\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*68rem\)\s*\{[\s\S]*\.aislepilot-budget-grid\s*\{[\s\S]*grid-auto-flow:\s*dense;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-budget-grid\s*>\s*\.aislepilot-stat-card\s*\{[\s\S]*align-self:\s*start;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-budget-grid\s*>\s*\.aislepilot-stat-card--supermarket\s*\{[\s\S]*grid-row:\s*span\s*2;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-stat-card--featured,\s*\.aislepilot-stat-card--status\s*\{[\s\S]*grid-column:\s*span\s*3;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*68rem\)\s*\{[\s\S]*\.aislepilot-day-carousel\s*\{[\s\S]*--ap-day-card-slide-width:\s*min\(100%,\s*41rem\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel-track\s*>\s*\.aislepilot-day-card\[data-day-carousel-position=""prev""\]\s*\{[\s\S]*opacity:\s*0\.44;[\s\S]*rotateY\(8deg\)\s*scale\(0\.92\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel-track\s*>\s*\.aislepilot-day-card\[data-day-carousel-position=""prev""\]\s+\.aislepilot-day-card-head,[\s\S]*\.aislepilot-day-carousel-track\s*>\s*\.aislepilot-day-card\[data-day-carousel-position=""next""\]\s+\.aislepilot-day-meal-tabs\s*\{[\s\S]*opacity:\s*0\.18;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-meal-tab\.is-active\s*\{[\s\S]*border-color:\s*rgba\(21,\s*128,\s*61,\s*0\.22\)\s*!important;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-carousel-dot\s*\{[\s\S]*min-width:\s*3\.85rem;[\s\S]*min-height:\s*2\.1rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-window-tab\.is-active,\s*\.aislepilot-window-tab\[aria-selected=""true""\],\s*\.aislepilot-window-tab\[aria-current=""page""\]\s*\{[\s\S]*background:\s*linear-gradient\(135deg,\s*var\(--ap-refresh-primary\)\s*0%,\s*var\(--ap-refresh-accent\)\s*100%\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-day-meal-panel\s*>\s*\.aislepilot-meal-details-panel\s*\{[\s\S]*padding:\s*0\.78rem\s*0\.86rem;[\s\S]*display:\s*grid;[\s\S]*gap:\s*0\.62rem;",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*68rem\)\s*\{[\s\S]*\.aislepilot-stat-card--meta\s*\{[\s\S]*background:\s*rgba\(243,\s*248,\s*245,\s*0\.78\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-shop-card-count\s*\{[\s\S]*border:\s*1px\s*solid\s*var\(--ap-refresh-border\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"\.aislepilot-export-intro\s*\{[\s\S]*border-color:\s*rgba\(21,\s*128,\s*61,\s*0\.16\);[\s\S]*\.aislepilot-export-btn\.is-primary\s*\{[\s\S]*min-height:\s*3\.2rem;[\s\S]*background:\s*linear-gradient\(135deg,\s*var\(--ap-refresh-primary\)\s*0%,\s*var\(--ap-refresh-accent\)\s*100%\);",
                RegexOptions.IgnoreCase),
            css);
        Assert.Matches(
            new Regex(
                @"@media\s*\(min-width:\s*68rem\)\s*\{[\s\S]*\.aislepilot-app\.has-results\s+\.aislepilot-app-head-actions\s*\{[\s\S]*width:\s*min\(100%,\s*12\.75rem\);",
                RegexOptions.IgnoreCase),
            css);
    }
}
