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

        await swapDays.ClickAsync();
        await page.WaitForFunctionAsync("""
            () => document.querySelector("[data-day-card-carousel]")?.getAttribute("data-day-reorder-mode") === "false"
        """);
        Assert.True(await page.Locator("[data-day-carousel-pagination]").IsVisibleAsync());
    }
}
