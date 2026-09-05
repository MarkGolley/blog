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
        await WriteAislePilotStateScreenshotAsync(page, $"colour-setup-{width}-{theme}");
        await GetAislePilotGenerateButton(page).ClickAsync();
        await page.Locator("#aislepilot-tab-meals").WaitForAsync();
        await AssertThemeColourAsync(page, ".aislepilot-head-primary-action", "--ap-refresh-primary");
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
}
