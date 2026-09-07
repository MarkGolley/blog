using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotShoppingChecklist_ResetRequiresConfirmationAndKeepsCustomItems()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);
        await page.Locator("[data-window-tab='aislepilot-shop']").ClickAsync();
        await page.EvaluateAsync("() => localStorage.setItem('aislepilot:unrelated-test', 'keep-me')");

        await page.Locator("[data-custom-shopping-input]").FillAsync("Kitchen roll");
        await page.Locator("[data-custom-shopping-add]").ClickAsync();
        var generated = page.Locator("[data-shopping-department] [data-shopping-item-input]").First;
        var custom = page.Locator("[data-custom-shopping-list] [data-shopping-item-input]").First;
        await generated.CheckAsync();
        await custom.CheckAsync();

        var reset = page.Locator("[data-shopping-reset]");
        var confirmation = page.Locator("[data-shopping-reset-confirm]");
        await reset.ClickAsync();
        Assert.True(await confirmation.IsVisibleAsync());
        Assert.Equal("true", await reset.GetAttributeAsync("aria-expanded"));
        Assert.Equal("Reset", await page.EvaluateAsync<string>("() => document.activeElement?.textContent?.trim() || ''"));

        await confirmation.Locator("[data-shopping-reset-cancel]").ClickAsync();
        Assert.True(await confirmation.IsHiddenAsync());
        Assert.True(await generated.IsCheckedAsync());
        Assert.True(await custom.IsCheckedAsync());
        Assert.Equal("Reset checks", await page.EvaluateAsync<string>("() => document.activeElement?.textContent?.trim() || ''"));

        await reset.ClickAsync();
        await confirmation.Locator("[data-shopping-reset-accept]").ClickAsync();
        Assert.Equal(0, await page.Locator("#aislepilot-shop [data-shopping-item-input]:checked").CountAsync());
        Assert.Equal("0", (await page.Locator("[data-shopping-progress]").TextContentAsync())?.Split(' ')[0]);
        Assert.Equal("Kitchen roll", (await page.Locator("[data-custom-shopping-list] [data-shopping-item-text]").TextContentAsync())?.Trim());
        Assert.Equal("keep-me", await page.EvaluateAsync<string>("() => localStorage.getItem('aislepilot:unrelated-test') || ''"));
        Assert.Equal("Reset checks", await page.EvaluateAsync<string>("() => document.activeElement?.textContent?.trim() || ''"));
    }

    [Fact]
    public async Task Mobile_AislePilotShoppingChecklist_TracksFiltersCompletesAndRestoresChecks()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);
        await page.Locator("[data-window-tab='aislepilot-shop']").ClickAsync();

        var labels = page.Locator("#aislepilot-shop [data-shopping-department] [data-shopping-item-label]");
        var total = await labels.CountAsync();
        Assert.True(total > 0);
        Assert.Contains($"0 of {total} items checked", await page.Locator("[data-shopping-progress]").TextContentAsync());

        var first = labels.First;
        var firstKey = await first.GetAttributeAsync("data-shopping-item-key");
        await first.ClickAsync();
        Assert.Contains($"1 of {total} items checked", await page.Locator("[data-shopping-progress]").TextContentAsync());

        var filter = page.Locator("[data-shopping-hide-checked]");
        await filter.CheckAsync();
        Assert.True(await first.Locator("xpath=ancestor::li[1]").IsHiddenAsync());
        Assert.Contains("Show all items", await page.Locator("[data-shopping-filter-label]").TextContentAsync());

        var nextCheckbox = labels.Nth(1).Locator("[data-shopping-item-input]");
        await nextCheckbox.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        Assert.Equal("true", await page.EvaluateAsync<string>("() => document.activeElement?.hasAttribute('data-shopping-hide-checked') ? 'true' : 'false'"));

        await filter.UncheckAsync();
        Assert.True(await page.EvaluateAsync<bool>("key => JSON.parse(localStorage.getItem('aislepilot:shopping-item-state') || '{}')[key] === true", firstKey));
        await GoToAislePilotAndGeneratePlanAsync(page);
        await page.Locator("[data-window-tab='aislepilot-shop']").ClickAsync();
        Assert.True(await page.Locator("#aislepilot-shop [data-shopping-item-input]:checked").CountAsync() > 0);

        await page.EvaluateAsync(
            """
            () => document.querySelectorAll('#aislepilot-shop [data-shopping-item-input]').forEach(input => {
                input.checked = true;
                input.dispatchEvent(new Event('change', { bubbles: true }));
            })
            """);
        Assert.Equal("All items checked", (await page.Locator("[data-shopping-progress]").TextContentAsync())?.Trim());
        await page.Locator("[data-shopping-hide-checked]").CheckAsync();
        Assert.Equal(0, await page.Locator("[data-shopping-department]:visible").CountAsync());
        await page.Locator("[data-shopping-hide-checked]").UncheckAsync();
        Assert.True(await page.Locator("[data-shopping-department]:visible").CountAsync() > 0);
    }

    [Fact]
    public async Task NarrowMobile_AislePilotShoppingChecklist_WrapsLongCustomItemsAndSurvivesStorageFailure()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateNarrowMobileContextAsync();
        var page = await context.NewPageAsync();
        await context.AddInitScriptAsync("Storage.prototype.setItem = () => { throw new Error('storage unavailable'); };");
        await GoToAislePilotAndGeneratePlanAsync(page);
        await page.Locator("[data-window-tab='aislepilot-shop']").ClickAsync();
        await page.EvaluateAsync("() => document.documentElement.dataset.theme = 'dark'");

        var longItem = "A very long reusable shopping item name with several words that must wrap safely";
        await page.Locator("[data-custom-shopping-input]").FillAsync(longItem);
        await page.Locator("[data-custom-shopping-add]").ClickAsync();
        var customLabel = page.Locator("[data-custom-shopping-list] [data-shopping-item-label]").Last;
        await customLabel.ClickAsync();
        Assert.True(await customLabel.Locator("[data-shopping-item-input]").IsCheckedAsync());

        var metrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const label = document.querySelector('[data-custom-shopping-list] [data-shopping-item-label]');
                const rect = label?.getBoundingClientRect();
                return [document.documentElement.scrollWidth - innerWidth, rect ? rect.right - innerWidth : -1, rect?.height ?? -1];
            }
            """);
        Assert.True(metrics[0] <= 1, $"Expected no page overflow. Overflow={metrics[0]:F1}px.");
        Assert.True(metrics[1] <= 1, $"Expected long item inside viewport. Overflow={metrics[1]:F1}px.");
        Assert.True(metrics[2] >= 44, $"Expected at least a 44px row target. Height={metrics[2]:F1}px.");
    }

    [Fact]
    public async Task Mobile_AislePilotShoppingChecklist_SearchesAndCollapsesDepartmentsWithStickyControls()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);
        await page.Locator("[data-window-tab='aislepilot-shop']").ClickAsync();

        var departments = page.Locator("[data-shopping-department]");
        Assert.True(await departments.CountAsync() > 1);
        Assert.True(await departments.First.GetAttributeAsync("open") is not null);
        Assert.True(await departments.Nth(1).GetAttributeAsync("open") is null);

        await departments.Nth(1).Locator("summary").ClickAsync();
        Assert.True(await departments.Nth(1).GetAttributeAsync("open") is not null);

        var firstItem = page.Locator("[data-shopping-department] [data-shopping-item-text]").First;
        var itemName = (await firstItem.TextContentAsync())?.Trim();
        Assert.False(string.IsNullOrWhiteSpace(itemName));
        await page.Locator("[data-shopping-search]").FillAsync(itemName!);

        Assert.True(await page.Locator("[data-shopping-department]:visible").CountAsync() >= 1);
        Assert.Equal(
            0,
            await page.Locator("[data-shopping-department] li:visible [data-shopping-item-text]")
                .EvaluateAllAsync<int>("(items, query) => items.filter(item => !(item.textContent || '').toLocaleLowerCase().includes(query)).length", itemName!.ToLowerInvariant()));

        await page.Locator("[data-shopping-search]").FillAsync("an item that is not on this list");
        Assert.True(await page.Locator("[data-shopping-filter-empty]").IsVisibleAsync());

        await page.Locator("[data-shopping-search]").FillAsync(string.Empty);
        await departments.Last.Locator("summary").ScrollIntoViewIfNeededAsync();
        var toolbarTop = await page.Locator(".aislepilot-shopping-toolbar").EvaluateAsync<double>("toolbar => toolbar.getBoundingClientRect().top");
        Assert.InRange(toolbarTop, 0, 20);
    }
}
