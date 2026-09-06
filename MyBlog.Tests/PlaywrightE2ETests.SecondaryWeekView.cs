using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task AislePilot_UsesSwapDaysAsItsOnlySecondaryWeekView()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);

        Assert.Equal(0, await page.Locator("[data-day-view-toggle]").CountAsync());
        var swapDays = page.Locator("[data-day-reorder-toggle]").First;
        Assert.Equal("Swap days", (await swapDays.InnerTextAsync()).Trim());
        await swapDays.ClickAsync();

        await page.WaitForFunctionAsync("""
            () => document.querySelector("[data-day-card-carousel]")?.getAttribute("data-day-reorder-mode") === "true"
        """);
        Assert.True(await page.Locator("[data-day-carousel-pagination]").IsHiddenAsync());
        Assert.Equal(0, await page.Locator("[data-day-card-slide]:not([data-day-carousel-ghost='true'])[aria-hidden='true']").CountAsync());
        Assert.Equal("Done", (await swapDays.InnerTextAsync()).Trim());
        var guide = page.Locator("[data-day-reorder-guide]");
        Assert.True(await guide.IsVisibleAsync());
        Assert.Equal("false", await guide.GetAttributeAsync("aria-hidden"));
        Assert.True(await page.Locator("[data-day-reorder-handle]").First.IsVisibleAsync());
        Assert.True(await page.Locator("[data-day-reorder-move='later']").First.IsVisibleAsync());

        var dayCards = page.Locator("[data-day-meal-card][data-day-card-meal-names]");
        var secondDayMeals = await dayCards.Nth(1).GetAttributeAsync("data-day-card-meal-names");
        Assert.False(string.IsNullOrWhiteSpace(secondDayMeals));
        await dayCards.Nth(0).Locator("[data-day-reorder-move='later']").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            expectedMeals => {
                const cards = document.querySelectorAll("[data-day-meal-card][data-day-card-meal-names]");
                const form = document.querySelector("[data-day-reorder-form]");
                return cards.length > 1
                    && cards[0].getAttribute("data-day-card-meal-names") === expectedMeals
                    && form?.getAttribute("data-ajax-swap-submitting") !== "true";
            }
            """,
            secondDayMeals);

        Assert.Equal(1, await page.Locator("[data-day-reorder-toggle]").CountAsync());
        Assert.Equal("Done", (await swapDays.InnerTextAsync()).Trim());

        await swapDays.ClickAsync();
        await page.WaitForFunctionAsync("""
            () => document.querySelector("[data-day-card-carousel]")?.getAttribute("data-day-reorder-mode") === "false"
        """);
        Assert.True(await page.Locator("[data-day-carousel-pagination]").IsVisibleAsync());
        Assert.Equal("true", await guide.GetAttributeAsync("aria-hidden"));
    }
}
