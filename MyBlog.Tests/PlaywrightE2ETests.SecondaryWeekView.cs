using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task AislePilot_MealOrganizer_MovesMealBetweenDaySlots()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);

        Assert.Equal(0, await page.Locator("[data-day-view-toggle]").CountAsync());
        var organizerToggle = page.Locator("[data-day-reorder-toggle]").First;
        Assert.Equal("Organise meals", (await organizerToggle.InnerTextAsync()).Trim());
        await organizerToggle.ClickAsync();

        await page.WaitForFunctionAsync("""
            () => document.querySelector("[data-day-card-carousel]")?.getAttribute("data-day-reorder-mode") === "true"
        """);
        Assert.True(await page.Locator("[data-day-carousel-pagination]").IsHiddenAsync());
        Assert.Equal(0, await page.Locator("[data-day-card-slide]:not([data-day-carousel-ghost='true'])[aria-hidden='true']").CountAsync());
        Assert.Equal("Done", (await organizerToggle.InnerTextAsync()).Trim());
        var guide = page.Locator("[data-day-reorder-guide]");
        Assert.True(await guide.IsVisibleAsync());
        Assert.Equal("false", await guide.GetAttributeAsync("aria-hidden"));
        Assert.Equal(0, await page.Locator("[data-day-reorder-handle], [data-day-reorder-move]").CountAsync());

        var organizerRows = page.Locator("[data-meal-organizer-slot][data-meal-organizer-name]");
        Assert.True(await organizerRows.CountAsync() > 3);
        var destinationIndex = 3;
        var destinationMeal = await organizerRows.Nth(destinationIndex).GetAttributeAsync("data-meal-organizer-name");
        Assert.False(string.IsNullOrWhiteSpace(destinationMeal));
        await organizerRows.Nth(0).Locator("[data-meal-organizer-move]").ClickAsync();
        Assert.Equal("Cancel", (await organizerRows.Nth(0).Locator("[data-meal-organizer-move]").InnerTextAsync()).Trim());
        Assert.Equal("Move here", (await organizerRows.Nth(destinationIndex).Locator("[data-meal-organizer-move]").InnerTextAsync()).Trim());
        await organizerRows.Nth(destinationIndex).Locator("[data-meal-organizer-move]").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            expectedMeal => {
                const rows = document.querySelectorAll("[data-meal-organizer-slot][data-meal-organizer-name]");
                const form = document.querySelector("[data-day-reorder-form]");
                return rows.length > 3
                    && rows[0].getAttribute("data-meal-organizer-name") === expectedMeal
                    && form?.getAttribute("data-ajax-swap-submitting") !== "true";
            }
            """,
            destinationMeal);

        Assert.Equal(1, await page.Locator("[data-day-reorder-toggle]").CountAsync());
        Assert.Equal("Done", (await organizerToggle.InnerTextAsync()).Trim());

        await organizerToggle.ClickAsync();
        await page.WaitForFunctionAsync("""
            () => document.querySelector("[data-day-card-carousel]")?.getAttribute("data-day-reorder-mode") === "false"
        """);
        Assert.True(await page.Locator("[data-day-carousel-pagination]").IsVisibleAsync());
        Assert.Equal("true", await guide.GetAttributeAsync("aria-hidden"));
    }
}
