using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotMealCard_ExposesRecipeSwapSaveAndIgnoreAsPrimaryActions()
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
        var recipe = actions.GetByRole(AriaRole.Button, new() { Name = "Recipe" });
        var swap = actions.GetByRole(AriaRole.Button, new() { Name = "Swap meal" });
        var save = actions.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^(Save|Unsave) meal$") });
        var ignore = actions.GetByRole(AriaRole.Button, new() { Name = "Ignore meal" });
        Assert.True(await recipe.IsVisibleAsync());
        Assert.True(await swap.IsVisibleAsync());
        Assert.True(await save.IsVisibleAsync());
        Assert.True(await ignore.IsVisibleAsync());
        Assert.StartsWith("meal-swap-", await swap.GetAttributeAsync("form"), StringComparison.Ordinal);
        Assert.StartsWith("meal-save-", await save.GetAttributeAsync("form"), StringComparison.Ordinal);
        Assert.StartsWith("meal-ignore-", await ignore.GetAttributeAsync("form"), StringComparison.Ordinal);
        Assert.True((await recipe.BoundingBoxAsync())?.Height >= 44);
        Assert.True((await swap.BoundingBoxAsync())?.Height >= 44);
        Assert.True((await save.BoundingBoxAsync())?.Height >= 44);
        Assert.True((await ignore.BoundingBoxAsync())?.Height >= 44);

        await recipe.ClickAsync();
        Assert.True(await activeMeal.Locator("[data-inline-details-panel]").IsVisibleAsync());
        Assert.Equal("true", await recipe.GetAttributeAsync("aria-expanded"));
        Assert.Equal("Hide recipe", await recipe.InnerTextAsync());
        await recipe.ClickAsync();
        Assert.False(await activeMeal.Locator("[data-inline-details-panel]").IsVisibleAsync());

        Assert.False(await activeMeal.Locator(".aislepilot-more-actions-trigger").IsVisibleAsync());
    }
}
