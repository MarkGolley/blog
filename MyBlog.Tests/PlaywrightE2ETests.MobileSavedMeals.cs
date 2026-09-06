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
        var saveButton = activeMealPanel.Locator(".aislepilot-meal-primary-action[form^='meal-save-']").First;
        var toasts = page.Locator(".aislepilot-toast");

        await saveButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await saveButton.EvaluateAsync("element => element instanceof HTMLElement && element.scrollIntoView({ block: 'center' })");
        var mealName = (await activeMealCard.Locator(".aislepilot-favorite-form input[name='mealName']").First.InputValueAsync()).Trim();

        var initiallySaved = await saveButton.EvaluateAsync<bool>(
            "button => button instanceof HTMLButtonElement && button.classList.contains('is-saved')");
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
                const button = document.querySelector("[data-day-meal-panel][aria-hidden='false'] .aislepilot-meal-primary-action[form^='meal-save-']");
                return button instanceof HTMLButtonElement &&
                    button.classList.contains("is-saved") === expectedSaved &&
                    (button.getAttribute("aria-label") ?? "") === (expectedSaved ? "Unsave meal" : "Save meal");
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
                const button = document.querySelector("[data-day-meal-panel][aria-hidden='false'] .aislepilot-meal-primary-action[form^='meal-save-']");
                return button instanceof HTMLButtonElement &&
                    button.classList.contains("is-saved") === expectedSaved &&
                    (button.getAttribute("aria-label") ?? "") === (expectedSaved ? "Unsave meal" : "Save meal");
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
