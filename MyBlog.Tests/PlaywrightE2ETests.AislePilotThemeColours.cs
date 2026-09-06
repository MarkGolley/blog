using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Theory]
    [InlineData(390, "light")]
    [InlineData(390, "dark")]
    [InlineData(1440, "light")]
    [InlineData(1440, "dark")]
    public async Task AislePilot_ControlsUseSharedThemeColours(int width, string theme)
    {
        if (!IsE2EEnabled()) return;
        Assert.NotNull(_browser);
        await using var context = await _browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await GoToAislePilotSetupAsync(page);
        await AssertThemeColourAsync(page, ".aislepilot-slider-field", "--ap-refresh-surface-strong");
        await AssertThemeColourAsync(page, ".aislepilot-head-menu-trigger", "--ap-refresh-primary");
        await AssertThemeColourAsync(page, "button[type='submit']:has-text('Generate weekly plan')", "--ap-refresh-primary");
        await page.Locator("[data-head-menu] > summary").ClickAsync();
        await AssertThemeColourAsync(page, ".aislepilot-head-menu-panel", "--ap-refresh-surface-strong");
        await page.Locator("[data-head-menu] > summary").ClickAsync();
        var people = page.Locator("#aislepilot-household-size");
        await people.PressAsync("Home");
        await people.PressAsync("ArrowRight");
        Assert.Equal("2", await people.InputValueAsync());
        Assert.Equal("static", await page.Locator(".aislepilot-slider-value").First.EvaluateAsync<string>("e => getComputedStyle(e).position"));
        Assert.Equal("rgba(0, 0, 0, 0)", await people.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        Assert.Equal("1", await people.EvaluateAsync<string>("e => getComputedStyle(e, '::-webkit-slider-thumb').opacity"));
        var budget = page.Locator("[data-budget-slider]");
        var sliderWidths = await page.EvaluateAsync<double[]>(
            """
            () => [
                document.querySelector("#aislepilot-household-size")?.getBoundingClientRect().width ?? 0,
                document.querySelector("[data-budget-slider]")?.getBoundingClientRect().width ?? 0
            ]
            """);
        Assert.InRange(Math.Abs(sliderWidths[0] - sliderWidths[1]), 0, 1);
        await budget.PressAsync("End");
        Assert.Equal("250", await budget.InputValueAsync());
        Assert.Contains("250", await page.Locator(".aislepilot-budget-slider-field [data-number-slider-value]").InnerTextAsync());
        await page.Locator(".aislepilot-personalise > summary").First.ClickAsync();
        await page.Locator("summary").Filter(new() { HasText = "Dietary requirements" }).ClickAsync();
        await AssertThemeColourAsync(page, ".aislepilot-mode-option input:checked + span", "--ap-refresh-primary");
        await AssertThemeColourAsync(page, ".aislepilot-mode-option input:not(:checked) + span", "--ap-refresh-surface");
        await page.Locator(".aislepilot-mode-option").Filter(new() { HasText = "Vegetarian" }).ClickAsync();
        Assert.True(await page.Locator("input[name='Request.DietaryModes'][value='Vegetarian']").IsCheckedAsync());
        await AssertThemeColourAsync(page, "input[value='Vegetarian'] + span", "--ap-refresh-primary");
        Assert.NotEqual(
            "sticky",
            await page.Locator(".app-shell-header").EvaluateAsync<string>("element => getComputedStyle(element).position"));
        await WriteAislePilotStateScreenshotAsync(page, $"colour-setup-{width}-{theme}");
        await page.EvaluateAsync("() => window.AislePilotCore.showPlanLoadingShell()");
        await AssertThemeColourAsync(page, ".aislepilot-plan-loading-surface", "--ap-refresh-surface");
        await AssertThemeColourAsync(page, ".aislepilot-plan-loading-card", "--ap-refresh-surface-strong");
        await AssertThemeTextColourAsync(page.Locator(".aislepilot-plan-loading-title"), "--ap-refresh-text");
        await AssertThemeTextColourAsync(page.Locator(".aislepilot-plan-loading-meta"), "--ap-refresh-text-muted");
        await page.EvaluateAsync("() => window.AislePilotCore.hidePlanLoadingShell()");
        await GetAislePilotGenerateButton(page).ClickAsync();
        await page.Locator("#aislepilot-tab-meals").WaitForAsync();
        await AssertThemeColourAsync(page, ".aislepilot-head-primary-action", "--ap-refresh-primary");
        var activeMeal = page.Locator("[data-day-meal-panel][aria-hidden='false']").First;
        await AssertThemeTextColourAsync(activeMeal.Locator(".aislepilot-more-actions-trigger .aislepilot-symbol-glyph"), "--ap-refresh-text");
        await activeMeal.Locator(".aislepilot-meal-primary-action.is-recipe").ClickAsync();
        await AssertThemeColourAsync(page, "[data-day-meal-panel][aria-hidden='false'] [data-inline-details-panel]", "--ap-refresh-surface-strong");
        await AssertThemeColourAsync(page, "[data-day-meal-panel][aria-hidden='false'] [data-inline-details-panel] .aislepilot-meal-section", "--ap-refresh-surface");
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > innerWidth"));
        await WriteAislePilotStateScreenshotAsync(page, $"colour-results-{width}-{theme}");
    }

    private static async Task AssertThemeColourAsync(IPage page, string selector, string token)
    {
        var colours = await page.Locator(selector).First.EvaluateAsync<string[]>(
            """
            (element, token) => {
                const probe = document.createElement('span');
                probe.style.backgroundColor = `var(${token})`;
                element.appendChild(probe);
                const expected = getComputedStyle(probe).backgroundColor;
                probe.remove();
                const style = getComputedStyle(element);
                return [expected, style.backgroundColor, style.backgroundImage];
            }
            """, token);
        Assert.Equal(colours[0], colours[1]);
        Assert.Equal("none", colours[2]);
    }

    private static async Task AssertThemeTextColourAsync(ILocator locator, string token)
    {
        var colours = await locator.EvaluateAsync<string[]>(
            """
            (element, token) => {
                const probe = document.createElement('span');
                probe.style.color = `var(${token})`;
                element.appendChild(probe);
                const expected = getComputedStyle(probe).color;
                probe.remove();
                return [expected, getComputedStyle(element).color];
            }
            """, token);
        Assert.Equal(colours[0], colours[1]);
    }
}
