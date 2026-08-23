using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotMealCard_ExposesRecipeSwapAndSaveAsPrimaryActions()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);

        var activeMeal = page.Locator("[data-day-meal-panel][aria-hidden='false']").First;
        await activeMeal.WaitForAsync();
        Assert.Contains("View recipe", await activeMeal.Locator(".aislepilot-meal-image-hint-primary").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);

        var actions = activeMeal.Locator("[data-meal-primary-actions]");
        var swap = actions.GetByRole(AriaRole.Button, new() { Name = "Swap meal" });
        var save = actions.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Save|Unsave) meal$") });
        Assert.True(await swap.IsVisibleAsync());
        Assert.True(await save.IsVisibleAsync());
        Assert.StartsWith("meal-swap-", await swap.GetAttributeAsync("form"), StringComparison.Ordinal);
        Assert.StartsWith("meal-save-", await save.GetAttributeAsync("form"), StringComparison.Ordinal);
        Assert.True((await swap.BoundingBoxAsync())?.Height >= 44);
        Assert.True((await save.BoundingBoxAsync())?.Height >= 44);

        var more = activeMeal.GetByRole(AriaRole.Button, new() { Name = "More meal actions" });
        await more.ClickAsync();
        var overflow = page.Locator("[data-card-more-actions-panel].is-mobile-sheet");
        await overflow.WaitForAsync();
        Assert.Equal(0, await overflow.GetByRole(AriaRole.Button, new() { Name = "Swap meal" }).CountAsync());
        Assert.Equal(0, await overflow.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Save|Unsave) meal$") }).CountAsync());
        Assert.True(await overflow.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Ignore|Include) meal$") }).IsVisibleAsync());
    }
}
