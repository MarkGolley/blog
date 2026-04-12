using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotSaveMeal_ShowsSaveAndUnsaveToasts()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var activeMealPanel = page.Locator(".aislepilot-day-meal-panel[aria-hidden='false']").First;
        await activeMealPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var activeMealCard = page.Locator("[data-day-meal-card]:has(.aislepilot-day-meal-panel[aria-hidden='false'])").First;
        var moreActionsSummary = activeMealCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;
        var saveButton = activeMealCard.Locator(
            "[data-day-card-header-actions].is-active [data-card-more-actions] .aislepilot-favorite-form button[type='submit']").First;
        var toasts = page.Locator(".aislepilot-toast");

        await moreActionsSummary.EvaluateAsync("element => element instanceof HTMLElement && element.scrollIntoView({ block: 'center' })");
        await moreActionsSummary.ClickAsync(new LocatorClickOptions { Force = true });
        await activeMealCard.EvaluateAsync(
            """
            card => {
                if (!(card instanceof HTMLElement)) {
                    return;
                }

                const details = card.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions]");
                if (details instanceof HTMLDetailsElement && !details.open) {
                    details.open = true;
                }
            }
            """);
        await saveButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await saveButton.EvaluateAsync("element => element instanceof HTMLElement && element.scrollIntoView({ block: 'center' })");
        var mealName = (await activeMealCard.Locator(".aislepilot-favorite-form input[name='mealName']").First.InputValueAsync()).Trim();

        var initiallySaved = await saveButton.EvaluateAsync<bool>(
            "button => button instanceof HTMLButtonElement && button.classList.contains('is-saved-meal')");
        var shouldBeSavedAfterFirstSubmit = !initiallySaved;
        var expectedFirstToast = initiallySaved
            ? "Meal removed from saved meals."
            : "Meal saved.";
        var expectedSecondToast = expectedFirstToast.Equals("Meal saved.", StringComparison.Ordinal)
            ? "Meal removed from saved meals."
            : "Meal saved.";

        var beforeFirstToastCount = await toasts.CountAsync();
        await saveButton.EvaluateAsync(
            """
            button => {
                if (!(button instanceof HTMLButtonElement)) {
                    return;
                }

                button.form?.requestSubmit(button);
            }
            """);
        await page.WaitForFunctionAsync(
            "previousCount => document.querySelectorAll('.aislepilot-toast').length > previousCount",
            beforeFirstToastCount,
            new() { Timeout = 10000 });
        var firstToastText = (await toasts.Last.InnerTextAsync()).Trim();
        Assert.Equal(expectedFirstToast, firstToastText);
        await page.WaitForFunctionAsync(
            """
            expectedSaved => {
                const button = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions] .aislepilot-favorite-form button[type='submit']");
                return button instanceof HTMLButtonElement &&
                    button.classList.contains("is-saved-meal") === expectedSaved &&
                    (button.getAttribute("title") ?? "") === (expectedSaved ? "Unsave meal" : "Save meal");
            }
            """,
            shouldBeSavedAfterFirstSubmit,
            new() { Timeout = 10000 });

        var headMenuTrigger = page.Locator("[data-head-menu] > summary").First;
        await headMenuTrigger.ClickAsync();
        var savedMealsList = page.Locator("[data-saved-meals-menu-section]");
        await savedMealsList.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        var savedMealVisibleAfterFirstSubmit = await savedMealsList.EvaluateAsync<bool>(
            """
            (section, targetMealName) => {
                if (!(section instanceof HTMLElement) || typeof targetMealName !== "string") {
                    return false;
                }

                return Array.from(section.querySelectorAll(".aislepilot-head-saved-meal-name"))
                    .some(node => node instanceof HTMLElement && (node.textContent ?? "").trim() === targetMealName);
            }
            """,
            mealName);
        Assert.Equal(shouldBeSavedAfterFirstSubmit, savedMealVisibleAfterFirstSubmit);
        await headMenuTrigger.ClickAsync();

        await moreActionsSummary.EvaluateAsync("element => element instanceof HTMLElement && element.scrollIntoView({ block: 'center' })");
        await moreActionsSummary.ClickAsync(new LocatorClickOptions { Force = true });
        await activeMealCard.EvaluateAsync(
            """
            card => {
                if (!(card instanceof HTMLElement)) {
                    return;
                }

                const details = card.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions]");
                if (details instanceof HTMLDetailsElement && !details.open) {
                    details.open = true;
                }
            }
            """);
        await saveButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var beforeSecondToastCount = await toasts.CountAsync();
        await saveButton.EvaluateAsync(
            """
            button => {
                if (!(button instanceof HTMLButtonElement)) {
                    return;
                }

                button.form?.requestSubmit(button);
            }
            """);
        await page.WaitForFunctionAsync(
            "previousCount => document.querySelectorAll('.aislepilot-toast').length > previousCount",
            beforeSecondToastCount,
            new() { Timeout = 10000 });
        var secondToastText = (await toasts.Last.InnerTextAsync()).Trim();
        Assert.Equal(expectedSecondToast, secondToastText);
        await page.WaitForFunctionAsync(
            """
            expectedSaved => {
                const button = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions] .aislepilot-favorite-form button[type='submit']");
                return button instanceof HTMLButtonElement &&
                    button.classList.contains("is-saved-meal") === expectedSaved &&
                    (button.getAttribute("title") ?? "") === (expectedSaved ? "Unsave meal" : "Save meal");
            }
            """,
            initiallySaved,
            new() { Timeout = 10000 });

        await headMenuTrigger.ClickAsync();
        var savedMealVisibleAfterSecondSubmit = await savedMealsList.EvaluateAsync<bool>(
            """
            (section, targetMealName) => {
                if (!(section instanceof HTMLElement) || typeof targetMealName !== "string") {
                    return false;
                }

                return Array.from(section.querySelectorAll(".aislepilot-head-saved-meal-name"))
                    .some(node => node instanceof HTMLElement && (node.textContent ?? "").trim() === targetMealName);
            }
            """,
            mealName);
        Assert.Equal(initiallySaved, savedMealVisibleAfterSecondSubmit);
    }
}
