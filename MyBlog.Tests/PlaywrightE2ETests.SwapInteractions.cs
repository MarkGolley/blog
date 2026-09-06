using Microsoft.Playwright;
using Xunit.Sdk;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotSwap_PreservesMealTabsAndDayCarouselInteractions()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        var browserErrors = new List<string>();
        page.PageError += (_, error) => browserErrors.Add(error);
        page.Console += (_, message) =>
        {
            if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
            {
                browserErrors.Add(message.Text);
            }
        };

        try
        {
            await GoToAislePilotAndGeneratePlanAsync(page);
        }
        catch (Exception ex)
        {
            throw new XunitException(
                $"Could not reach a generated plan. URL={page.Url}. Browser errors: {string.Join(" || ", browserErrors)}",
                ex);
        }

        var moreActionsTriggers = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary:visible");
        var moreActionsTriggerCount = await moreActionsTriggers.CountAsync();
        Assert.True(moreActionsTriggerCount > 1, $"Expected multiple meal cards to be rendered. Actual trigger count={moreActionsTriggerCount}.");

        var targetIndex = Math.Min(2, moreActionsTriggerCount - 1);
        var targetTrigger = moreActionsTriggers.Nth(targetIndex);
        var targetCard = page.Locator("[data-day-meal-card]").Nth(targetIndex);
        var previousMealName = (await targetCard.Locator(".aislepilot-day-meal-panel[aria-hidden='false'] h3").First.InnerTextAsync()).Trim();

        var targetSwapButton = targetTrigger.Locator("xpath=ancestor::*[@data-day-meal-panel][1]").Locator(".aislepilot-meal-primary-action[aria-label='Swap meal']");
        await targetSwapButton.ScrollIntoViewIfNeededAsync();
        await targetSwapButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        var primarySwapState = await targetSwapButton.EvaluateAsync<string>(
            """
            button => {
                const formId = button.getAttribute("form") ?? "";
                const matchingForms = document.querySelectorAll(`form[id="${CSS.escape(formId)}"]`);
                return JSON.stringify({
                    formId,
                    ownerId: button.form?.id ?? "",
                    matchingFormCount: matchingForms.length,
                    ownerConnected: button.form?.isConnected ?? false,
                    ownerValid: button.form?.checkValidity() ?? false,
                    ajaxWired: button.form?.dataset.ajaxSwapWired ?? "",
                    loadingWired: button.form?.dataset.loadingWired ?? "",
                    ajaxSubmitting: button.form?.dataset.ajaxSwapSubmitting ?? ""
                });
            }
            """);

        var swapResponseTask = page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
            response.Url.Contains("/projects/aisle-pilot/swap-meal", StringComparison.OrdinalIgnoreCase));

        await targetSwapButton.ClickAsync();
        try
        {
            _ = await swapResponseTask;
        }
        catch (TimeoutException ex)
        {
            var postClickState = await targetSwapButton.EvaluateAsync<string>(
                "button => JSON.stringify({ disabled: button.disabled, busy: button.getAttribute('aria-busy'), submitting: button.form?.dataset.isSubmitting ?? '', ajaxSubmitting: button.form?.dataset.ajaxSwapSubmitting ?? '' })");
            throw new XunitException(
                $"Expected swap POST after tapping the primary Swap button. State={primarySwapState}. PostClick={postClickState}. Browser errors: {string.Join(" || ", browserErrors)}",
                ex);
        }
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.WaitForFunctionAsync(
            """
            ([cardIndex, priorMealName]) => {
                const cards = Array.from(document.querySelectorAll("[data-day-meal-card]"));
                const card = cards[cardIndex];
                if (!(card instanceof HTMLElement)) {
                    return false;
                }

                const currentMealName = (card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false'] h3")?.textContent ?? "").trim();
                const activePanel = document.querySelector(".aislepilot-window-panel[aria-hidden='false']");
                return currentMealName.length > 0 &&
                    currentMealName !== priorMealName &&
                    activePanel?.id === "aislepilot-meals" &&
                    document.querySelectorAll("[data-card-more-actions][open]").length === 0 &&
                    currentMealName !== priorMealName;
            }
            """,
            new object[] { targetIndex, previousMealName },
            new PageWaitForFunctionOptions
            {
                Timeout = 10000
            });

        var beforeCarouselStatus = (await page.Locator("[data-day-carousel-status]").First.InnerTextAsync()).Trim();
        var secondDayTab = page.Locator("[data-day-carousel-dot][data-day-carousel-target='1']").First;
        await secondDayTab.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            previousStatus => {
                const status = document.querySelector("[data-day-carousel-status]");
                return ((status?.textContent ?? "").trim()) !== previousStatus;
            }
            """,
            beforeCarouselStatus,
            new PageWaitForFunctionOptions
            {
                Timeout = 10000
            });

        var afterCarouselStatus = (await page.Locator("[data-day-carousel-status]").First.InnerTextAsync()).Trim();
        Assert.NotEqual(beforeCarouselStatus, afterCarouselStatus);

        var postSwapInteractionState = await page.EvaluateAsync<string>(
            """
            cardIndex => {
                const cards = Array.from(document.querySelectorAll("[data-day-meal-card]"));
                const card = cards[cardIndex];
                if (!(card instanceof HTMLElement)) {
                    return "missing";
                }

                const currentMealName = (card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false'] h3")?.textContent ?? "").trim();
                const activePanel = document.querySelector(".aislepilot-window-panel[aria-hidden='false']");
                const openMenuCount = document.querySelectorAll("[data-card-more-actions][open]").length;
                const activeCarouselStatus = (document.querySelector("[data-day-carousel-status]")?.textContent ?? "").trim();
                return `${currentMealName}|${activePanel?.id ?? ""}|${openMenuCount}|${activeCarouselStatus}`;
            }
            """,
            targetIndex);

        var stateParts = postSwapInteractionState.Split('|', StringSplitOptions.None);
        Assert.True(stateParts.Length >= 4, $"Expected post-swap interaction state payload. Actual='{postSwapInteractionState}'.");
        Assert.NotEqual(previousMealName, stateParts[0]);
        Assert.Equal("aislepilot-meals", stateParts[1]);
        Assert.Equal("0", stateParts[2]);
        Assert.Equal(afterCarouselStatus, stateParts[3], ignoreCase: true);
    }
}
