using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Services;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests : IClassFixture<TestWebApplicationFactory>
{

    [Fact]
    public async Task Index_Get_RendersPlanDaysSlider()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("name=\"Request.PlanDays\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-plan-days-slider", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-cook-days-slider", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<input[^>]*name=""Request\.CookDays""[^>]*type=""hidden""|<input[^>]*type=""hidden""[^>]*name=""Request\.CookDays""",
                RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task Index_Get_RendersSavedMealRepeatStrengthSliderWithPlanBasicsFrameStyle()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Matches(
            new Regex(
                @"<div[^>]*(?:class=""[^""]*aislepilot-slider-field--plan-basics-frame[^""]*""[^>]*data-saved-repeat-rate-field|data-saved-repeat-rate-field[^>]*class=""[^""]*aislepilot-slider-field--plan-basics-frame[^""]*"")",
                RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task Index_Get_RendersMealsPerDayControl_DefaultingToThree()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("name=\"Request.MealsPerDay\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                "name=\"Request\\.MealsPerDay\"[^>]*value=\"3\"|value=\"3\"[^>]*name=\"Request\\.MealsPerDay\"",
                RegexOptions.IgnoreCase),
            html);
        Assert.Contains("name=\"Request.SelectedMealTypes\" value=\"Breakfast\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"Request.SelectedMealTypes\" value=\"Lunch\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"Request.SelectedMealTypes\" value=\"Dinner\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_RendersPlanLoadingSkeletonShellAndPlannerTrigger()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("data-plan-loading-shell", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-setup-mode-submit=\"planner\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-show-plan-skeleton", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_UsesSimplifiedAislePilotLayoutWithoutDecorativeOverlays()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.DoesNotContain("class=\"page-aurora\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-scroll-progress", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_RendersStepBasedSetupWorkspaceWithSummaryCard()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("/css/aisle-pilot-refresh.css", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-setup-workspace\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Step 1", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Household and preferences", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plan rules", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-setup-summary\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plan snapshot", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ready to generate", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_RendersSplitAislePilotScriptBundles()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("/js/aisle-pilot/core.js", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/js/aisle-pilot/action-menus.js", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/js/aisle-pilot/shopping.js", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/js/aisle-pilot.js", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_RendersAislePilotSubBrandFontLinksAndThemeColor()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("rel=\"preconnect\" href=\"https://fonts.googleapis.com\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("family=Plus+Jakarta+Sans", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<meta name=\"theme-color\" content=\"#115C4E\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_UsesRecruiterFriendlyAislePilotMetaDescription()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains(
            "AislePilot: generate a weekly meal plan, track budget, and create a supermarket-ordered shopping list.",
            html,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AislePilot prototype", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_RendersDayMealSummaryRowsWithinDayCardCarousel()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "65"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("class=\"aislepilot-overview-glance\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-overview-glance-item", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-meal-summary", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-card aislepilot-shop-card\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-export-action is-", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-carousel", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-viewport", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-track", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-slide", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-status", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-pagination", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-dot", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-day-card-expander", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-meal-swipe-surface", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-meal-image-url=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-day-meal-title\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_RendersMealDetailSectionsAsCollapsedDisclosures()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "65"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Matches(
            new Regex(
                @"<details[^>]*class=""[^""]*aislepilot-meal-section[^""]*""[^>]*data-meal-section=""nutrition""[\s\S]*?<summary[^>]*data-meal-section-summary[^>]*>\s*Est\. calories \+ macros\s*</summary>[\s\S]*?<div[^>]*data-meal-section-content",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<details[^>]*class=""[^""]*aislepilot-meal-section[^""]*""[^>]*data-meal-section=""ingredients""[\s\S]*?<summary[^>]*data-meal-section-summary[^>]*>\s*Ingredients\s*</summary>[\s\S]*?<div[^>]*data-meal-section-content",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<details[^>]*class=""[^""]*aislepilot-meal-section[^""]*""[^>]*data-meal-section=""method""[\s\S]*?<summary[^>]*data-meal-section-summary[^>]*>\s*Method\s*</summary>[\s\S]*?<div[^>]*data-meal-section-content",
                RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task Index_Post_RendersPersistentPlanSnapshotWithoutOverviewToggle()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "65"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Plan snapshot", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Budget, spend, and weekly structure at a glance.", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-overview-toggle", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-setup-toggle-label", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-overview-content hidden", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Adjust settings", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithMultipleDayCards_RendersDayCardCarouselWithoutVisibleReorderHandles()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "90",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "3",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.LeftoverCookDayIndexesCsv"] = "0",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-day-card-carousel", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-prev", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-next", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-dot", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-day-reorder-handle", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-meal-names=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-ignored-flags=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-leftover-count=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_RendersPeopleSliderWithThumbFriendlyModifier()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("aislepilot-slider-field--thumb-friendly", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"Request.HouseholdSize\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_UsesLargerControlHeightTokensForReadableTapTargets()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Contains("--ap-control-height: 2.5rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-control-height-lg: 2.75rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-control-height-touch: 2.75rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-overview-button-size: 2.75rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min-height: var(--ap-overview-button-size) !important;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min-width: 8.8rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-meal-panel.has-open-actions", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-y: visible;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow: hidden;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-card-action-row :is(.aislepilot-meal-details > summary, .aislepilot-swap-btn, .aislepilot-more-actions-trigger):focus-visible", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inset 0 0 0 2px var(--ap-focus-ring-color)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-card-action-row :is(.aislepilot-meal-details > summary, .aislepilot-swap-btn, .aislepilot-more-actions-trigger):focus", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outline: none;", css, StringComparison.OrdinalIgnoreCase);
        var importantCount = Regex.Matches(css, "!important", RegexOptions.IgnoreCase).Count;
        Assert.True(importantCount <= 240, $"Expected CSS override count to stay under control, but found {importantCount} '!important' usages.");
    }

    [Fact]
    public async Task AislePilotStylesheet_PeopleSlider_UsesLargerThumbLatchTarget()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Contains(".aislepilot-slider-field--thumb-friendly .aislepilot-slider-input {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("height: 1.9rem !important;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-slider-field--thumb-friendly .aislepilot-slider-value {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min-width: 2.1rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-slider-field--thumb-friendly .aislepilot-slider-input::-webkit-slider-thumb {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("width: 1.7rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("height: 1.7rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-slider-field--thumb-friendly .aislepilot-slider-input::-moz-range-thumb {", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_UsesDayCardCarouselWithoutChevronAffordance()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Contains(".aislepilot-day-carousel", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-carousel-track", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scroll-snap-type: x mandatory;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-day-card-slide-width: min(100%, 33rem);", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-day-card-ghost-width: clamp(6.75rem, 13vw, 9.25rem);", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-day-carousel-stage-inset-inline: 1.2rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-day-carousel-stage-inset-block-start: 2.45rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gap: 1rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-card::before", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-meal-grid::before", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-x: auto;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) auto;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("width: min(100%, calc(var(--ap-day-card-slide-width) - 3rem));", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min-width: 3.2rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background: rgba(250, 253, 255, 0.72);", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rotateY(8deg)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-day-card-slide-width: 100%;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("justify-content: flex-start;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-meal-image-hint-chips {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-meal-title,", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-webkit-line-clamp: 2;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("height: clamp(8.8rem, 22vw, 11.6rem);", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-width: min(calc(100% - 1.4rem), 7rem);", css, StringComparison.OrdinalIgnoreCase);
        var script = await GetCombinedAislePilotScriptAsync(client);
        Assert.Contains("dayCarouselPosition", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("targetScrollLeft", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("computeTargetScrollLeft", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("targetCenter - viewportCenter", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-ghost", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ensureGhostSlides", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-carousel-ghost-side", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-day-card-ghost-shell", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-day-card-ghost-band", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-day-card-ghost-panel", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aislepilot-day-card-ghost-chip", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scrollPaginationToActiveDot", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activeIndex === 0 ? slides.length - 1 : activeIndex - 1", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activeIndex === slides.length - 1 ? 0 : activeIndex + 1", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prefers-reduced-motion: reduce", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pendingNavigationIndex", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scheduleNavigationSettle", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("viewport.scrollTo({", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scrollToIndex(targetIndex, \"smooth\", \"jump\")", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transform: none;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-card.is-carousel-ghost", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-card-ghost-shell", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-card-ghost-band", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-card-ghost-panel", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@keyframes aislepilot-day-card-settle", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".aislepilot-day-carousel-pagination::before", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@keyframes aislepilot-day-carousel-pill-pulse", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".aislepilot-day-card-ghost-chip", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".aislepilot-day-card-expander > summary::after", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".aislepilot-day-card-expander[open] > summary::after", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(":root[data-theme=\"dark\"] .aislepilot-day-card-expander > summary::after", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotScript_PreservesIconOnlyButtonsWhenSubmitLoadingStarts()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await GetCombinedAislePilotScriptAsync(client);

        Assert.Contains("if (submitButton.classList.contains(\"is-icon-only\"))", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("submitButton.setAttribute(\"aria-label\", nextAriaLabel);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (!button.classList.contains(\"is-icon-only\") && originalLabel && originalLabel.length > 0)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const visibleLabel = button.querySelector(\"[data-setup-toggle-label]\")", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (visibleLabel instanceof HTMLElement) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (!srOnlyLabel && !(visibleLabel instanceof HTMLElement)) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const closeOpenOverviewActionsMenus = except => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const wireOverviewActionsMenus = scope => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("window.AislePilotActionMenus?.wireOverviewActionsMenus(scope);", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotScript_LeftoverPlanner_SubmitsImmediatelyFromCardToggle()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await GetCombinedAislePilotScriptAsync(client);

        Assert.Contains("const leftoverRebalanceForms = scope instanceof Element", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const maxExtraRaw = Number.parseInt(", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const getZoneToggleButtons = zone => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-day-index=\"${dayIndex}\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("zone.classList.toggle(\"is-leftover-locked\", !isAssigned && !canAssignMore);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toggleButton.dataset.leftoverToggleMode = isAssigned ? \"remove\" : \"add\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("buttonText.textContent = nextActionLabel;", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toggleButton.textContent = \"++\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toggleButton.textContent = \"-\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (isLeftoverRebalanceForm) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clearPersistedSwapScroll();", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (getAssignedCount() >= maxExtraAllocations)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("submitLeftoverRebalance();", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("persistSwapScrollPosition(leftoverRebalanceForm);", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotScript_AjaxSwapResponse_RestoresMealImagesToReduceFlash()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await GetCombinedAislePilotScriptAsync(client);

        Assert.Contains("const captureRenderedMealImageSources = scope => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const restoreRenderedMealImageSources = (scope, imageSrcByMealName) => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("imageElement.replaceWith(preservedImageElement);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const preservedMealImageSources = captureRenderedMealImageSources(document);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restoreRenderedMealImageSources(document, preservedMealImageSources);", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotScript_DayCardReorder_RebuildsPlanStateBeforeAjaxSubmit()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await GetCombinedAislePilotScriptAsync(client);

        Assert.Contains("const syncDayReorderFormState = form => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const buildDayCardLeftoverSourceIndexesCsv = cards => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const buildDayCardIgnoredIndexesCsv = cards => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const wireDayCardReorder = scope => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("swapForm.hasAttribute(\"data-day-reorder-form\")", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("showToast(\"Meal day updated.\", \"success\");", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("form.requestSubmit();", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_MealMethodListsRenderMarkersInsidePanelBounds()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Contains(".aislepilot-day-meal-panel > .aislepilot-meal-details-panel ol.case-list", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list-style-position: inside;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("padding-left: 0;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-card:hover {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transform: none;", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_MoreActionsMenu_UsesReadableLabelledActionButtons()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Contains(".aislepilot-card-more-actions-menu {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min-width: 8.8rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("width: max-content;", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("width: 3.4rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ".aislepilot-card-more-action-form:not(.aislepilot-favorite-form):not(.aislepilot-ignore-form) .aislepilot-swap-btn.is-secondary.is-compact",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ".aislepilot-card-more-action-form.aislepilot-favorite-form .aislepilot-swap-btn.is-secondary.is-compact:not(.is-saved-meal)",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-card-more-actions-menu .aislepilot-swap-btn.is-compact .aislepilot-btn-label", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-mobile-meal-sheet-handle", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-card-more-actions.is-closing > .aislepilot-card-more-actions-menu", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-card-more-actions-menu.is-mobile-sheet", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-card-more-actions[open] > .aislepilot-card-more-actions-menu:not(.is-mobile-sheet)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("will-change: transform, opacity;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fill: currentColor;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stroke: none;", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_LeftoverCardToggle_IsPlainSymbolWithoutButtonChrome()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await GetCombinedAislePilotCssAsync(client);
        var stepButtonBlockMatch = Regex.Match(
            css,
            @"\.aislepilot-leftover-step-btn\s*\{(?<body>[\s\S]*?)\}",
            RegexOptions.IgnoreCase);

        Assert.True(stepButtonBlockMatch.Success, "Expected .aislepilot-leftover-step-btn CSS block.");
        var stepButtonBlock = stepButtonBlockMatch.Groups["body"].Value;
        Assert.Contains("border: none;", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("border-radius: 0;", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background: transparent;", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border: 1px solid rgba(16, 63, 101, 0.3);", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-radius: 999px;", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersMealSlotTabsOnDayCards()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.IncludeDessertAddOn"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-day-meal-tab", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Breakfast<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Lunch<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Dinner<", html, StringComparison.OrdinalIgnoreCase);

        var mealTabsMatches = Regex.Matches(
            html,
            @"<div class=""aislepilot-day-meal-tabs""[^>]*>(?<tabs>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        Assert.True(mealTabsMatches.Count > 0, "Expected meal slot tab list to be rendered.");

        string tabsMarkup = string.Empty;
        foreach (Match match in mealTabsMatches)
        {
            var candidate = match.Groups["tabs"].Value;
            if (Regex.IsMatch(candidate, @">\s*Breakfast\s*<", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(candidate, @">\s*Lunch\s*<", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(candidate, @">\s*Dinner\s*<", RegexOptions.IgnoreCase))
            {
                tabsMarkup = candidate;
                break;
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(tabsMarkup), "Expected at least one meal tab list to include Breakfast, Lunch, and Dinner.");
        var breakfastMatch = Regex.Match(tabsMarkup, @">\s*Breakfast\s*<", RegexOptions.IgnoreCase);
        var lunchMatch = Regex.Match(tabsMarkup, @">\s*Lunch\s*<", RegexOptions.IgnoreCase);
        var dinnerMatch = Regex.Match(tabsMarkup, @">\s*Dinner\s*<", RegexOptions.IgnoreCase);
        Assert.True(breakfastMatch.Success && lunchMatch.Success && dinnerMatch.Success, "Expected Breakfast, Lunch, and Dinner tabs.");
        Assert.True(
            breakfastMatch.Index < lunchMatch.Index && lunchMatch.Index < dinnerMatch.Index,
            $"Expected meal tabs in Breakfast -> Lunch -> Dinner order, but indexes were {breakfastMatch.Index}, {lunchMatch.Index}, {dinnerMatch.Index}.");
        Assert.DoesNotContain("class=\"aislepilot-day-card-meta\">3 meals</p>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"Selected meal cost and cook time\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-summary-value=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<section[^>]*class=""[^""]*aislepilot-day-meal-panel[^""]*""[^>]*>[\s\S]*?<h3>[^<]+</h3>[\s\S]*?<summary[^>]*class=""[^""]*aislepilot-meal-image-summary[^""]*""[^>]*>[\s\S]*?<div class=""aislepilot-meal-image-shell""[^>]*data-day-meal-swipe-surface",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<p class=""aislepilot-day-card-meta""[^>]*>[\s\S]*?(?:\u00A3|&#xA3;|&pound;)[\s\S]*?mins\s*</p>",
                RegexOptions.IgnoreCase),
            html);
        Assert.DoesNotContain("class=\"aislepilot-meal-details-grid\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-nutrition-details\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-recipe-details\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersSwapInsideMoreActionsMenu()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("class=\"aislepilot-swap-btn is-secondary is-compact\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-swap-action-label\">Swap</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-swap-action-label\">Actions</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-loading-delay-ms=\"320\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-header-actions", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-card-more-actions", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"Meal actions\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<div[^>]*class=""[^""]*aislepilot-card-more-actions-menu[^""]*""[^>]*>[\s\S]*?<form[^>]*action=""[^""]*/swap-meal[^""]*""",
                RegexOptions.IgnoreCase),
            html);
        Assert.DoesNotContain("data-leftover-toggle-sign", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-mobile-meal-sheet-head", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-card-more-actions-close", html, StringComparison.OrdinalIgnoreCase);
        var dayZoneMatches = Regex.Matches(
            html,
            @"<div[^>]*class=""[^""]*aislepilot-day-card-leftover-controls[^""]*""[^>]*>(?<zone>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        Assert.True(dayZoneMatches.Count > 0, "Expected at least one day-card leftover zone in generated markup.");
        foreach (Match dayZoneMatch in dayZoneMatches)
        {
            Assert.DoesNotContain("data-leftover-toggle-sign", dayZoneMatch.Groups["zone"].Value, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotMatch(
            new Regex(
                @"<form[^>]*action=""[^""]*/swap-meal[^""]*""[^>]*class=""[^""]*aislepilot-card-action-inline[^""]*""",
                RegexOptions.IgnoreCase),
            html);
        Assert.DoesNotContain("class=\"aislepilot-card-kicker\">Breakfast</p>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-card-kicker\">Lunch</p>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-card-kicker\">Dinner</p>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersCompactActionControlsWithMealDetailPreviewChips()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.IncludeDessertAddOn"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("class=\"aislepilot-overview-regenerate-btn is-secondary\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aislepilot-edit-setup-btn", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-setup-toggle-label", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-overview-actions-menu", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-overview-actions-trigger is-icon-only\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aislepilot-overview-regenerate-btn is-icon-only", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-more-actions-trigger\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-more-actions-trigger is-icon-only\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-haspopup=\"dialog\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-swap-btn is-secondary is-compact\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("is-secondary is-icon-only is-compact", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-swap-action-label\">Save</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-swap-action-label\">Ignore</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-meal-image-summary\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-card-more-actions-panel", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-mobile-meal-sheet-handle\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span>Refresh plan</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Adjust settings", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-meal-image-hint-primary\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-meal-image-hint-chip\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"sr-only\">View meal details: macros, ingredients and method</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"sr-only\">View dessert details: ingredients and method</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<span class=\"sr-only\">Swap meal</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"View meal details: macros, ingredients and method\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"View dessert details: ingredients and method\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Recipe</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">View details<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Macros</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">View recipe<", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersMobileStickyContextAndPlanRefreshSkeletonTriggers()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("class=\"aislepilot-mobile-context\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-mobile-context-jump is-active\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-mobile-context-jump\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-window-tab=\"aislepilot-meals\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-window-tab=\"aislepilot-shop\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-window-tab=\"aislepilot-export\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-mobile-context-jump-text\">Meals</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-mobile-context-jump-text\">Shop</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-mobile-context-jump-text\">Export</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Use tabs to switch between meals, shopping, and exports.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"Refresh plan\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-show-plan-skeleton", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersAccessibleQuickJumpTabsAndReadableSummarySeparator()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("class=\"aislepilot-mobile-context-jumps\" role=\"tablist\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<button[^>]*class=""[^""]*aislepilot-mobile-context-jump[^""]*""(?=[^>]*data-window-tab=""aislepilot-meals"")(?=[^>]*role=""tab"")(?=[^>]*aria-controls=""aislepilot-meals"")(?=[^>]*aria-selected=""true"")(?=[^>]*aria-current=""page"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<button[^>]*class=""[^""]*aislepilot-mobile-context-jump[^""]*""(?=[^>]*data-window-tab=""aislepilot-shop"")(?=[^>]*role=""tab"")(?=[^>]*aria-controls=""aislepilot-shop"")(?=[^>]*aria-selected=""false"")(?=[^>]*aria-current=""false"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<button[^>]*class=""[^""]*aislepilot-mobile-context-jump[^""]*""(?=[^>]*data-window-tab=""aislepilot-export"")(?=[^>]*role=""tab"")(?=[^>]*aria-controls=""aislepilot-export"")(?=[^>]*aria-selected=""false"")(?=[^>]*aria-current=""false"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(new Regex(@"(&middot;|Â·)", RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersStyledBudgetStatusInWeeklyPlanSummary()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.IncludeDessertAddOn"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var css = await GetCombinedAislePilotCssAsync(client);

        Assert.Matches(
            new Regex(
                @"<span[^>]*class=""[^""]*aislepilot-mobile-context-budget-status[^""]*(?:is-over-budget|is-on-budget)[^""]*""[^>]*>[^<]*(?:under|over|On budget)[^<]*</span>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Contains(".aislepilot-mobile-context-budget-status.is-over-budget", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-mobile-context-budget-status.is-on-budget", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersWindowTabsAndMealActionsWithAlignedAriaContracts()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Matches(
            new Regex(
                @"<button[^>]*id=""aislepilot-tab-meals""(?=[^>]*class=""[^""]*aislepilot-window-tab[^""]*is-active[^""]*"")(?=[^>]*aria-controls=""aislepilot-meals"")(?=[^>]*aria-selected=""true"")(?=[^>]*aria-current=""page"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<button[^>]*id=""aislepilot-tab-shop""(?=[^>]*class=""[^""]*aislepilot-window-tab[^""]*"")(?=[^>]*aria-controls=""aislepilot-shop"")(?=[^>]*aria-selected=""false"")(?=[^>]*aria-current=""false"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<button[^>]*id=""aislepilot-tab-export""(?=[^>]*class=""[^""]*aislepilot-window-tab[^""]*"")(?=[^>]*aria-controls=""aislepilot-export"")(?=[^>]*aria-selected=""false"")(?=[^>]*aria-current=""false"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Contains("<span class=\"aislepilot-tab-text\">Meals</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-tab-text\">Shop</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-tab-text\">Export</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"role=""group""[^>]*aria-label=""Meal actions""|aria-label=""Meal actions""[^>]*role=""group""",
                RegexOptions.IgnoreCase),
            html);
        Assert.DoesNotContain("aria-haspopup=\"menu\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role=\"menuitem\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-inline-details-panel hidden aria-hidden=\"true\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithLongExclusionsAndPantryValues_RendersFullSettingsSummaries()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var longExclusions = "peanuts mushrooms shellfish sesame celery mustard lupin gluten dairy soy eggs fish";
        var longPantry = "brown rice chickpeas chopped tomatoes spinach olive oil garlic ginger spring onions cumin paprika";

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = longExclusions,
            ["Request.PantryItems"] = longPantry,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        var exclusionSummaryMatch = Regex.Match(
            html,
            @"<span[^>]*data-exclusion-summary[^>]*title=""(?<title>[^""]*)""[^>]*>(?<text>[^<]*)</span>",
            RegexOptions.IgnoreCase);
        Assert.True(exclusionSummaryMatch.Success, "Expected exclusions summary span with title attribute.");
        Assert.Equal(longExclusions, WebUtility.HtmlDecode(exclusionSummaryMatch.Groups["title"].Value).Trim());
        Assert.Equal(longExclusions, WebUtility.HtmlDecode(exclusionSummaryMatch.Groups["text"].Value).Trim());
        Assert.DoesNotContain($"{longExclusions[..64]}...", html, StringComparison.OrdinalIgnoreCase);

        var pantrySummaryMatch = Regex.Match(
            html,
            @"<span[^>]*data-pantry-summary[^>]*title=""(?<title>[^""]*)""[^>]*>(?<text>[^<]*)</span>",
            RegexOptions.IgnoreCase);
        Assert.True(pantrySummaryMatch.Success, "Expected pantry summary span with title attribute.");
        Assert.Equal(longPantry, WebUtility.HtmlDecode(pantrySummaryMatch.Groups["title"].Value).Trim());
        Assert.Equal(longPantry, WebUtility.HtmlDecode(pantrySummaryMatch.Groups["text"].Value).Trim());
        Assert.DoesNotContain($"{longPantry[..64]}...", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_PrioritizesFirstMealImagesForFasterPerceivedLoad()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Matches(
            new Regex(
                @"<img[^>]*class=""[^""]*aislepilot-meal-image[^""]*""(?=[^>]*loading=""eager"")(?=[^>]*fetchpriority=""high"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<img[^>]*class=""[^""]*aislepilot-meal-image[^""]*""(?=[^>]*loading=""lazy"")(?=[^>]*fetchpriority=""low"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_SwapFormsIncludeCurrentPlanMealNames()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var swapMealFormMatch = Regex.Match(
            html,
            @"<form[^>]*action=""[^""]*/swap-meal[^""]*""[^>]*>(?<formBody>[\s\S]*?)</form>",
            RegexOptions.IgnoreCase);
        Assert.True(swapMealFormMatch.Success, "Expected at least one swap-meal form in generated plan.");
        Assert.Contains(
            "name=\"currentPlanMealNames\"",
            swapMealFormMatch.Groups["formBody"].Value,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithResult_RendersNotesCompatibleShareShoppingListLabel()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Share shopping list", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iPhone Notes", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithResult_ClarifiesShoppingEstimateIsNotCheckoutTotal()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Meal ingredient estimate", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("This estimate covers the ingredients used in these meals.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("It adjusts for your selected supermarket using stored public pricing data.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Actual checkout can be higher if shops only sell larger packs or bags.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" - Est. ", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithResult_RendersPhoneFirstExportHierarchy()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        var introIndex = html.IndexOf("Start with the shopping list on your phone.", StringComparison.OrdinalIgnoreCase);
        var shareIndex = html.IndexOf("Share shopping list", StringComparison.OrdinalIgnoreCase);
        var planPackIndex = html.IndexOf("Download plan pack (.pdf)", StringComparison.OrdinalIgnoreCase);
        var checklistIndex = html.IndexOf("Download checklist (.txt)", StringComparison.OrdinalIgnoreCase);
        var printIndex = html.IndexOf("Print meal and shopping view", StringComparison.OrdinalIgnoreCase);

        Assert.True(introIndex >= 0, "Expected export panel to explain the phone-first export option.");
        Assert.True(shareIndex >= 0, "Expected share-shopping-list export action to be rendered.");
        Assert.True(planPackIndex > shareIndex, "Expected full plan pack download to follow the share action.");
        Assert.True(checklistIndex > planPackIndex, "Expected text checklist download to follow the PDF action.");
        Assert.True(printIndex > planPackIndex, "Expected print action to be the final export option.");
        Assert.Contains("Take this plan with you", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Share to your phone", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Need a paper copy for the kitchen or a shared shop?", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-export-action is-primary\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-export-btn is-primary\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-export-action is-secondary\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-export-btn is-secondary\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-export-footer\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-export-btn is-tertiary\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithResult_RendersCheckableShoppingItems()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-shopping-item-label", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-shopping-item-key=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-shopping-item-input", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-shopping-item-text", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-shop-card-head\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-shop-card-count\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithResult_RendersCustomShoppingItemsSection()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-custom-shopping-shell", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-custom-shopping-form", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-custom-shopping-input", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-custom-shopping-list", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Your extra items", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithBreakfastOnlyMealSlot_UsesBreakfastTabsWithoutDuplicateCardKicker()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "65"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Breakfast"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(">Breakfast<", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-card-kicker\">Breakfast</p>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WhenCookDaysExceedPlanDays_DoesNotRenderCookDaysSliderAndKeepsHiddenCookDays()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "1",
            ["Request.CookDays"] = "5",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Request.CookDays\" value=\"1\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-cook-days-slider", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<p class=\"aislepilot-stat-label\">Leftovers</p>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Adjust cook-extra days", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithCustomLeftoverAssignment_ShowsDoubleLeftoverOnRequestedDay()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CookDays"] = "5",
            ["Request.LeftoverCookDayIndexesCsv"] = "4,4",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Adjust cook-extra days", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-leftover-planner", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-rebalance-form", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-max-extra=\"6\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<form[^>]*class=""[^""]*aislepilot-leftover-rebalance-form[^""]*""[^>]*(data-leftover-rebalance-form[^>]*data-ajax-swap-form|data-ajax-swap-form[^>]*data-leftover-rebalance-form)",
                RegexOptions.IgnoreCase),
            html);
        Assert.DoesNotContain("data-leftover-toggle-sign", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-day-card-leftover-controls", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-name=\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-leftover-day-count\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-day-count", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Makes extra for", html, StringComparison.OrdinalIgnoreCase);
    }

}

