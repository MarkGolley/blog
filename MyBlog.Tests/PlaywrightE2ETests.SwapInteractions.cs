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
        var swapDebugLines = new List<string>();
        page.Console += (_, message) =>
        {
            if (!string.Equals(message.Type, "info", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!message.Text.Contains("[AislePilot swap debug]", StringComparison.Ordinal))
            {
                return;
            }

            swapDebugLines.Add(message.Text);
        };

        await GoToAislePilotAndGeneratePlanAsync(page);
        await page.EvaluateAsync(
            """
            () => {
                window.__swapDebugPayloads = [];
                if (window.__swapDebugCaptureInstalled) {
                    return;
                }

                const originalInfo = console.info.bind(console);
                console.info = (...args) => {
                    if (args[0] === "[AislePilot swap debug]") {
                        window.__swapDebugPayloads.push({
                            stage: args[1] ?? "",
                            details: args[2] ?? null
                        });
                    }

                    return originalInfo(...args);
                };
                window.__swapDebugCaptureInstalled = true;
            }
            """);

        var moreActionsTriggers = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary:visible");
        var moreActionsTriggerCount = await moreActionsTriggers.CountAsync();
        Assert.True(moreActionsTriggerCount > 1, $"Expected multiple meal cards to be rendered. Actual trigger count={moreActionsTriggerCount}.");

        var targetIndex = Math.Min(2, moreActionsTriggerCount - 1);
        var targetTrigger = moreActionsTriggers.Nth(targetIndex);
        var targetCard = page.Locator("[data-day-meal-card]").Nth(targetIndex);
        var previousMealName = (await targetCard.Locator(".aislepilot-day-meal-panel[aria-hidden='false'] h3").First.InnerTextAsync()).Trim();

        var targetSwapButton = page.Locator("[data-card-more-actions-panel].is-mobile-sheet button[aria-label='Swap meal']").First;
        await targetTrigger.ScrollIntoViewIfNeededAsync();
        await targetTrigger.ClickAsync();
        await targetSwapButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

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
            var structuredDebugPayloads = await page.EvaluateAsync<string>(
                """
                () => JSON.stringify(window.__swapDebugPayloads ?? [])
                """);
            throw new XunitException(
                $"Expected swap POST after tapping mobile sheet swap button. Captured debug lines: {string.Join(" || ", swapDebugLines)}. Structured debug: {structuredDebugPayloads}",
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
        var nextButton = page.Locator("[data-day-carousel-next]").First;
        await nextButton.ClickAsync();

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
        Assert.Equal(afterCarouselStatus, stateParts[3]);
    }
}
