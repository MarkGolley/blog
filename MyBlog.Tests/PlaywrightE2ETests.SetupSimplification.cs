using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task NarrowMobile_AislePilotSetupControlsMeetTouchTargetAndSpacingContract()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateNarrowMobileContextAsync();
        var page = await context.NewPageAsync();
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        foreach (var mode in new[] { "planner", "generator" })
        {
            await page.Locator($"[data-setup-mode-toggle='{mode}']").ClickAsync();
            await page.EvaluateAsync(
                """
                () => document.querySelectorAll('#aislepilot-setup-form details').forEach(details => details.open = true)
                """);

            var failures = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const root = document.querySelector('#aislepilot-setup-form');
                    const selector = [
                        '[data-setup-mode-toggle]',
                        'details > summary',
                        'button[type="submit"]',
                        'button[type="button"]:not([hidden])',
                        'textarea',
                        'select',
                        'input:not([type])',
                        'input[type="text"]',
                        'input[type="range"]',
                        'input[type="number"]:not([hidden])',
                        'label.aislepilot-meals-per-day-option',
                        'label.aislepilot-supermarket-option',
                        'label.aislepilot-toggle-chip',
                        'label.aislepilot-mode-option'
                    ].join(',');
                    const targets = Array.from(root?.querySelectorAll(selector) ?? [])
                        .filter(element => {
                            const style = getComputedStyle(element);
                            const rect = element.getBoundingClientRect();
                            return !element.hidden && style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                        })
                        .map((element, index) => ({
                            element,
                            name: element.getAttribute('aria-label') ?? element.textContent?.trim().replace(/\s+/g, ' ').slice(0, 55) ?? `${element.tagName} ${index}`,
                            rect: element.getBoundingClientRect()
                        }));
                    const failures = targets
                        .filter(target => target.rect.width < 43.5 || target.rect.height < 43.5)
                        .map(target => `${target.name}: ${target.rect.width.toFixed(1)}x${target.rect.height.toFixed(1)}`);
                    for (let leftIndex = 0; leftIndex < targets.length; leftIndex++) {
                        for (let rightIndex = leftIndex + 1; rightIndex < targets.length; rightIndex++) {
                            const left = targets[leftIndex];
                            const right = targets[rightIndex];
                            if (left.element.contains(right.element) || right.element.contains(left.element)) continue;
                            const horizontalGap = Math.max(0, Math.max(left.rect.left, right.rect.left) - Math.min(left.rect.right, right.rect.right));
                            const verticalGap = Math.max(0, Math.max(left.rect.top, right.rect.top) - Math.min(left.rect.bottom, right.rect.bottom));
                            const overlapX = horizontalGap === 0;
                            const overlapY = verticalGap === 0;
                            const gap = overlapX ? verticalGap : overlapY ? horizontalGap : Math.hypot(horizontalGap, verticalGap);
                            if (gap > 0 && gap < 7.5) failures.push(`${left.name} <> ${right.name}: ${gap.toFixed(1)}px`);
                        }
                    }
                    return failures;
                }
                """);

            Assert.True(failures.Length == 0, $"{mode} touch-contract failures:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
        }
    }

    [Fact]
    public async Task NarrowMobile_AislePilotValidationRecoveryExposesKeyboardAndScreenReaderContract()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateNarrowMobileContextAsync();
        var page = await context.NewPageAsync();
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var generatorChoice = page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Use my ingredients" });
        await generatorChoice.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var pantry = page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Pantry items" });
        await pantry.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        var flexibility = page.GetByText("Ingredient flexibility", new PageGetByTextOptions { Exact = true }).Locator("..");
        await flexibility.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var coreIngredients = page.GetByRole(AriaRole.Checkbox, new PageGetByRoleOptions { Name = "Use only my listed ingredients" });
        await coreIngredients.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        Assert.True(await coreIngredients.IsCheckedAsync());
        var submit = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Generate 3 meal ideas" });
        await submit.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var alert = page.GetByRole(AriaRole.Alert);
        await alert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var errorLink = alert.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { NameRegex = new System.Text.RegularExpressions.Regex("Ingredients you have", System.Text.RegularExpressions.RegexOptions.IgnoreCase) });
        Assert.Single(await errorLink.AllAsync());
        Assert.Equal("Request_PantryItems", await page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''"));
        Assert.Contains("aislepilot-pantry-items-error", await pantry.GetAttributeAsync("aria-describedby"), StringComparison.Ordinal);
        Assert.Contains("Add a few pantry ingredients", await page.Locator("#aislepilot-pantry-items-error").TextContentAsync(), StringComparison.OrdinalIgnoreCase);
        await errorLink.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("Request_PantryItems", await page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''"));
    }

    [Fact]
    public async Task NarrowMobile_AislePilotValidationWaitsForBlurAndClearsOnNextBlur()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateNarrowMobileContextAsync();
        var page = await context.NewPageAsync();
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("[data-setup-mode-toggle='generator']").ClickAsync();
        var pantry = page.Locator("textarea[name='Request.PantryItems']");
        var error = page.Locator("#aislepilot-pantry-items-error");
        await pantry.FocusAsync();
        await pantry.EvaluateAsync("element => element.value = 'x'.repeat(401)");
        await pantry.DispatchEventAsync("input");
        Assert.Equal(string.Empty, (await error.TextContentAsync())?.Trim());

        await pantry.BlurAsync();
        var blurState = await page.EvaluateAsync<string>(
            """
            () => {
                const form = document.querySelector('#aislepilot-setup-form');
                const field = document.querySelector("textarea[name='Request.PantryItems']");
                const validator = window.jQuery?.(form).data('validator');
                return JSON.stringify({ length: field?.value.length, rules: validator?.settings?.rules?.['Request.PantryItems'], invalid: validator?.invalid?.['Request.PantryItems'] });
            }
            """);
        Assert.True((await error.TextContentAsync())?.Contains("400 characters", StringComparison.OrdinalIgnoreCase) is true, blurState);

        await pantry.FocusAsync();
        await pantry.EvaluateAsync("element => element.value = 'rice, beans'");
        await pantry.DispatchEventAsync("input");
        Assert.Contains("400 characters", await error.TextContentAsync(), StringComparison.OrdinalIgnoreCase);
        await pantry.BlurAsync();
        Assert.Equal(string.Empty, (await error.TextContentAsync())?.Trim());
    }

    [Fact]
    public async Task NarrowMobile_AislePilotValidationSummaryLinksErrorsAndFocusesFirstInvalidField()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateNarrowMobileContextAsync();
        var page = await context.NewPageAsync();
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("[data-setup-mode-toggle='generator']").ClickAsync();
        var submitted = await page.EvaluateAsync<bool>(
            """
            async () => {
                const form = document.querySelector("#aislepilot-setup-form");
                const pantry = form?.querySelector("textarea[name='Request.PantryItems']");
                const submit = form?.querySelector("[data-setup-mode-submit='generator']");
                if (!(form instanceof HTMLFormElement) || !(pantry instanceof HTMLTextAreaElement) || !(submit instanceof HTMLButtonElement)) {
                    return false;
                }
                pantry.value = "";
                const response = await fetch(submit.formAction, { method: "POST", body: new FormData(form) });
                const html = await response.text();
                document.open();
                document.write(html);
                document.close();
                return true;
            }
            """);
        Assert.True(submitted);

        var summary = page.Locator("[data-validation-summary]");
        await summary.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Single(await summary.Locator("a[href^='#Request_PantryItems']").AllAsync());
        var focusIds = await page.EvaluateAsync<string[]>(
            """
            () => [
                document.activeElement?.id ?? "",
                document.querySelector("[autofocus]")?.id ?? ""
            ]
            """);
        Assert.False(string.IsNullOrWhiteSpace(focusIds[0]));
        Assert.Equal(focusIds[1], focusIds[0]);
        Assert.Equal(string.Empty, await page.Locator("textarea[name='Request.PantryItems']").InputValueAsync());
    }

    [Fact]
    public async Task NarrowMobile_AislePilotModeChoiceStartsPredictablyAndReturnsToSavedMode()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateNarrowMobileContextAsync();
        var page = await context.NewPageAsync();
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var plannerToggle = page.Locator("[data-setup-mode-toggle='planner']");
        var generatorToggle = page.Locator("[data-setup-mode-toggle='generator']");
        Assert.Equal("true", await plannerToggle.GetAttributeAsync("aria-selected"));
        Assert.True(await page.Locator("[data-planner-only-preference]").First.IsVisibleAsync());
        Assert.False(await page.Locator("[data-generator-only-preference]").First.IsVisibleAsync());

        await generatorToggle.ClickAsync();
        Assert.Equal("true", await generatorToggle.GetAttributeAsync("aria-selected"));
        Assert.False(await page.Locator("[data-planner-only-preference]").First.IsVisibleAsync());
        Assert.True(await page.Locator("[data-generator-only-preference]").First.IsVisibleAsync());
        Assert.True(await page.Locator("#aislepilot-generator-mode .aislepilot-outcome-summary").IsVisibleAsync());
        Assert.True(await page.Locator("#aislepilot-generator-mode [data-setup-mode-submit='generator']").IsVisibleAsync());
        Assert.False(await page.Locator("input[type='checkbox'][name='Request.RequireCorePantryIngredients']").IsDisabledAsync());

        await page.ReloadAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Equal("true", await page.Locator("[data-setup-mode-toggle='generator']").GetAttributeAsync("aria-selected"));
        Assert.True(await page.Locator("#aislepilot-generator-mode").IsVisibleAsync());
        Assert.False(await page.Locator("#aislepilot-planner-mode").IsVisibleAsync());
    }

    [Fact]
    public async Task NarrowMobile_AislePilotPersonalisationPreservesValuesAndKeepsOutcomeAboveAction()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateNarrowMobileContextAsync();
        var page = await context.NewPageAsync();
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var plannerPersonalise = page.Locator("details[data-planner-personalise]");
        await plannerPersonalise.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.False(await plannerPersonalise.GetAttributeAsync("open") is not null);
        Assert.True(await page.Locator("details[data-plan-basic-item='meal-types']").IsVisibleAsync());
        Assert.True(await page.Locator("details[data-plan-basic-item='plan-days']").IsVisibleAsync());
        Assert.True(await page.Locator("details[data-plan-basic-item='budget']").IsVisibleAsync());

        await plannerPersonalise.Locator(":scope > summary").ClickAsync();
        await plannerPersonalise.Locator("details[data-plan-basic-item='supermarket'] > summary").ClickAsync();
        await plannerPersonalise
            .Locator("label.aislepilot-supermarket-option", new LocatorLocatorOptions { HasTextString = "Aldi" })
            .ClickAsync(new LocatorClickOptions { Force = true });
        await page.Locator("[data-setup-mode-toggle='generator']").ClickAsync();
        await page.Locator("[data-setup-mode-toggle='planner']").ClickAsync();
        Assert.True(await plannerPersonalise.Locator("input[name='Request.Supermarket'][value='Aldi']").IsCheckedAsync());

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const outcome = document.querySelector("#aislepilot-planner-mode .aislepilot-outcome-summary");
                const action = document.querySelector("#aislepilot-planner-mode [data-setup-mode-submit='planner']");
                const summary = document.querySelector("details[data-planner-personalise] > summary");
                if (!(outcome instanceof HTMLElement) || !(action instanceof HTMLElement) || !(summary instanceof HTMLElement)) {
                    return [-1, -1, -1, -1];
                }
                const outcomeRect = outcome.getBoundingClientRect();
                const actionRect = action.getBoundingClientRect();
                const summaryRect = summary.getBoundingClientRect();
                return [outcomeRect.bottom, actionRect.top, summaryRect.height, document.documentElement.scrollWidth - window.innerWidth];
            }
            """);

        Assert.True(layout[1] >= layout[0] + 7, $"Expected at least 8px between outcome and action. Gap={layout[1] - layout[0]:F1}px.");
        Assert.True(layout[2] >= 44, $"Expected Personalise summary to be at least 44px tall. Height={layout[2]:F1}px.");
        Assert.True(layout[3] <= 1, $"Expected no horizontal overflow. Overflow={layout[3]:F1}px.");
    }
}
