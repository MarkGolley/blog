using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task NarrowMobile_AislePilotResults_AreAccessibleAndRemainInsideViewport()
    {
        if (!IsE2EEnabled()) return;
        if (_browser is null) throw new InvalidOperationException("Playwright browser is not initialized.");

        await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ColorScheme = ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = 320, Height = 700 }
        });
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);

        foreach (var tabName in new[] { "aislepilot-meals", "aislepilot-shop", "aislepilot-export" })
        {
            await page.Locator($"[data-window-tab='{tabName}']").First.ClickAsync();
            await page.Locator($"#{tabName}[aria-hidden='false']").First.WaitForAsync();

            var overflow = await page.EvaluateAsync<double>(
                "() => document.documentElement.scrollWidth - window.innerWidth");
            var offenders = await page.EvaluateAsync<string[]>(
                """
                () => Array.from(document.querySelectorAll('body *'))
                    .filter(element => {
                        const rect = element.getBoundingClientRect();
                        return rect.right > innerWidth + 1 || rect.left < -1;
                    })
                    .slice(0, 8)
                    .map(element => `${element.tagName.toLowerCase()}.${element.className || ''} (${Math.round(element.getBoundingClientRect().left)}..${Math.round(element.getBoundingClientRect().right)})`)
                """);
            var widthChain = await page.EvaluateAsync<string>(
                """
                () => {
                    let element = document.querySelector('.aislepilot-mobile-context');
                    const parts = [];
                    while (element && parts.length < 7) {
                        const rect = element.getBoundingClientRect();
                        const style = getComputedStyle(element);
                        parts.push(`${element.tagName.toLowerCase()}.${element.className || ''}=${Math.round(rect.width)}[w:${style.width},min:${style.minWidth},justify:${style.justifySelf}]`);
                        element = element.parentElement;
                    }
                    return parts.join(' > ');
                }
                """);
            Assert.True(overflow <= 1, $"{tabName} overflowed the 320px viewport by {overflow:F1}px. {string.Join(" | ", offenders)} Chain: {widthChain}");

            if (tabName == "aislepilot-meals")
            {
                var actionMetrics = await page.EvaluateAsync<double[]>(
                    """
                    () => Array.from(document.querySelectorAll('[data-day-card-slide][aria-hidden="false"] [data-day-meal-panel][aria-hidden="false"] [data-meal-primary-actions] button'))
                        .flatMap(button => {
                            const rect = button.getBoundingClientRect();
                            return [rect.left, rect.right, rect.height];
                        })
                    """);
                Assert.True(actionMetrics.Length >= 12, "Expected Recipe, Swap, Save and Ignore controls.");
                for (var index = 0; index < actionMetrics.Length; index += 3)
                {
                    Assert.True(actionMetrics[index] >= -1 && actionMetrics[index + 1] <= 321,
                        $"Primary action {index / 3} escaped the narrow viewport.");
                    Assert.True(actionMetrics[index + 2] >= 44,
                        $"Primary action {index / 3} was shorter than 44px.");
                }
            }

            var axeResult = await page.RunAxe();
            var blocking = axeResult.Violations
                .Where(violation =>
                    violation.Impact.Equals("serious", StringComparison.OrdinalIgnoreCase) ||
                    violation.Impact.Equals("critical", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(blocking.Count == 0,
                $"{tabName} has serious or critical accessibility violations: " +
                string.Join(" | ", blocking.Select(violation =>
                    $"{violation.Id}: {string.Join(", ", violation.Nodes.Select(node => node.Target.ToString()))}")));
        }
    }

    [Fact]
    public async Task Desktop_AislePilotOrganizerAndShoppingDisclosures_WorkFromKeyboard()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);

        var organizer = page.Locator("[data-day-reorder-toggle]").First;
        await organizer.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("true", await organizer.GetAttributeAsync("aria-pressed"));

        var firstMove = page.Locator("[data-meal-organizer-move]:visible").First;
        await firstMove.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("true", await firstMove.GetAttributeAsync("aria-pressed"));
        Assert.Contains("Moving", await page.Locator("[data-day-carousel-status]").TextContentAsync());
        await page.Keyboard.PressAsync("Enter");
        Assert.NotEqual("true", await firstMove.GetAttributeAsync("aria-pressed"));

        await page.Locator("[data-window-tab='aislepilot-shop']").First.ClickAsync();
        var department = page.Locator("[data-shopping-department]").First;
        var summary = department.Locator("summary");
        await summary.FocusAsync();
        var wasOpen = await department.GetAttributeAsync("open") is not null;
        await page.Keyboard.PressAsync("Enter");
        Assert.NotEqual(wasOpen, await department.GetAttributeAsync("open") is not null);
    }
}
