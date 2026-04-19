using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;

namespace MyBlog.Tests;

[Trait("Category", "E2E")]
public sealed partial class PlaywrightE2ETests : IAsyncLifetime
{
    private const string E2EEnvVar = "RUN_PLAYWRIGHT_E2E";
    private const string AdminUsername = "admin";
    private const string AdminPassword = "password";
    private const string ModerationBannerText =
        "Your comment was not published because it did not meet our moderation standards.";

    private LocalAppHost? _appHost;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task InitializeAsync()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        _appHost = await LocalAppHost.StartAsync();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();

        if (_appHost is not null)
        {
            await _appHost.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "E2ESmoke")]
    public async Task Mobile_ModeratedComment_ShowsModerationBannerAtAddCommentSection()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToFirstPostAsync(page);

        await page.FillAsync("#author-root", "Mobile E2E");
        await page.FillAsync("#content-root", "kill yourself you deserve to die");
        await page.ClickAsync("section[aria-labelledby='add-comment-title'] button[type='submit']");
        await page.WaitForFunctionAsync(
            "() => window.location.href.toLowerCase().includes('commentstatus=moderated')",
            null,
            new() { Timeout = 15000 });

        Assert.Contains("commentStatus=moderated", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#add-comment-title", page.Url, StringComparison.OrdinalIgnoreCase);

        var banner = page.Locator("section[aria-labelledby='add-comment-title'] .moderation-notice");
        Assert.True(await banner.IsVisibleAsync());
        Assert.Equal(ModerationBannerText, (await banner.InnerTextAsync()).Trim());
    }

    [Fact]
    public async Task Mobile_AdminLogin_SucceedsWithUppercaseUsernameInput()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await LoginAsync(page, "ADMIN");

        Assert.Contains("/Admin", page.Url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pending Comments", await page.ContentAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mobile_AislePilotSwap_PreservesScrollPosition()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var moreActionsTriggers = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary:visible");
        var moreActionsTriggerCount = await moreActionsTriggers.CountAsync();
        Assert.True(moreActionsTriggerCount > 0, "Expected at least one meal actions menu trigger to be rendered.");

        var targetIndex = Math.Min(3, moreActionsTriggerCount - 1);
        var targetTrigger = moreActionsTriggers.Nth(targetIndex);
        var targetSwapButton = page.Locator("[data-card-more-actions-panel].is-mobile-sheet button[aria-label='Swap meal']").First;
        await targetTrigger.ScrollIntoViewIfNeededAsync();
        await targetTrigger.ClickAsync();
        await targetSwapButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var beforeScrollY = await page.EvaluateAsync<int>("() => Math.round(window.scrollY)");
        Assert.True(beforeScrollY > 100, $"Expected to scroll below hero before swap. Actual scrollY={beforeScrollY}.");

        var swapResponseTask = page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
            response.Url.Contains("/projects/aisle-pilot/swap-meal", StringComparison.OrdinalIgnoreCase));

        await targetSwapButton.ClickAsync();
        _ = await swapResponseTask;
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(1100);

        // Allow delayed scroll-restore hooks to complete.
        await page.WaitForTimeoutAsync(250);

        var afterScrollY = await page.EvaluateAsync<int>("() => Math.round(window.scrollY)");
        var scrollDelta = Math.Abs(afterScrollY - beforeScrollY);
        var upwardDelta = beforeScrollY - afterScrollY;

        Assert.True(
            scrollDelta <= 8,
            $"Expected swap postback to keep viewport stable. Before={beforeScrollY}, After={afterScrollY}, Delta={scrollDelta}.");
        Assert.True(
            upwardDelta <= 4,
            $"Expected swap postback not to pull the viewport upward. Before={beforeScrollY}, After={afterScrollY}, UpwardDelta={upwardDelta}.");
    }

    [Fact]
    public async Task Mobile_AislePilotSwap_ShowsPendingStateWhileKeepingViewportStable()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var moreActionsTriggers = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary:visible");
        var moreActionsTriggerCount = await moreActionsTriggers.CountAsync();
        Assert.True(moreActionsTriggerCount > 0, "Expected at least one meal actions menu trigger to be rendered.");

        var targetIndex = Math.Min(2, moreActionsTriggerCount - 1);
        var targetTrigger = moreActionsTriggers.Nth(targetIndex);
        var targetCard = targetTrigger.Locator("xpath=ancestor::*[@data-day-meal-card][1]");
        var targetSwapButton = page.Locator("[data-card-more-actions-panel].is-mobile-sheet button[aria-label='Swap meal']").First;
        await targetTrigger.ScrollIntoViewIfNeededAsync();
        await targetTrigger.ClickAsync();
        await targetSwapButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var beforeScrollY = await page.EvaluateAsync<int>("() => Math.round(window.scrollY)");
        Assert.True(beforeScrollY > 100, $"Expected to scroll below hero before swap. Actual scrollY={beforeScrollY}.");

        await page.RouteAsync("**/projects/aisle-pilot/swap-meal", async route =>
        {
            await Task.Delay(900);
            await route.ContinueAsync();
        });

        var swapResponseTask = page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
            response.Url.Contains("/projects/aisle-pilot/swap-meal", StringComparison.OrdinalIgnoreCase));

        await targetSwapButton.ClickAsync();
        await page.WaitForTimeoutAsync(150);

        var pendingState = await targetCard.EvaluateAsync<string>(
            """
            card => {
                if (!(card instanceof HTMLElement)) {
                    return "missing";
                }

                const swapStatus = window.getComputedStyle(card, "::before").content || "";
                const isBusy = card.getAttribute("aria-busy") || "";
                const isPending = card.classList.contains("is-swap-fading-out") ? "1" : "0";
                return `${isBusy}|${isPending}|${swapStatus}`;
            }
            """);

        Assert.Contains("true|1|", pendingState, StringComparison.Ordinal);
        Assert.Contains("Loading new meal", pendingState, StringComparison.OrdinalIgnoreCase);

        _ = await swapResponseTask;
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(1100);

        var afterScrollY = await page.EvaluateAsync<int>("() => Math.round(window.scrollY)");
        var scrollDelta = Math.Abs(afterScrollY - beforeScrollY);
        var upwardDelta = beforeScrollY - afterScrollY;

        Assert.True(
            scrollDelta <= 8,
            $"Expected swap viewport to stay anchored after showing pending state. Before={beforeScrollY}, After={afterScrollY}, Delta={scrollDelta}.");
        Assert.True(
            upwardDelta <= 4,
            $"Expected pending-state swap not to pull the viewport upward. Before={beforeScrollY}, After={afterScrollY}, UpwardDelta={upwardDelta}.");
    }

    [Fact]
    public async Task Mobile_AislePilotSwap_ClosesActionsSheetAndPreservesActiveDayAndSlot()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var targetCardIndex = await page.EvaluateAsync<int>(
            """
            () => {
                const cards = Array.from(document.querySelectorAll("[data-day-meal-card]"));
                return cards.findIndex((card, index) =>
                    index > 0 &&
                    card instanceof HTMLElement &&
                    card.querySelectorAll("[data-day-meal-tab]").length > 1 &&
                    card.querySelector("[data-day-card-expander]") instanceof HTMLDetailsElement);
            }
            """);

        Assert.True(targetCardIndex > 0, $"Expected a non-default day card with multiple meal slots. Actual index={targetCardIndex}.");

        var targetCard = page.Locator("[data-day-meal-card]").Nth(targetCardIndex);
        var targetExpanderSummary = targetCard.Locator("[data-day-card-expander] > summary").First;
        var targetExpander = targetCard.Locator("[data-day-card-expander]").First;
        var wasTargetExpanderOpen = await targetExpander.EvaluateAsync<bool>(
            "element => element instanceof HTMLDetailsElement && element.open");
        if (!wasTargetExpanderOpen)
        {
            await targetExpanderSummary.ScrollIntoViewIfNeededAsync();
            await targetExpanderSummary.ClickAsync();
        }

        var mealTabs = targetCard.Locator("[data-day-meal-tab]");
        var mealTabCount = await mealTabs.CountAsync();
        Assert.True(mealTabCount > 1, $"Expected target day card to expose multiple meal slots. Actual count={mealTabCount}.");

        var targetSlotIndex = Math.Min(2, mealTabCount - 1);
        var targetMealTab = mealTabs.Nth(targetSlotIndex);
        await targetMealTab.ClickAsync();

        var expectedDayLabel = (await targetCard.Locator(".aislepilot-day-card-expander-day").First.InnerTextAsync()).Trim();
        var expectedSlotLabel = (await targetMealTab.InnerTextAsync()).Trim();
        var activeMealPanel = targetCard.Locator(".aislepilot-day-meal-panel[aria-hidden='false']").First;
        var previousMealName = (await activeMealPanel.Locator("h3").First.InnerTextAsync()).Trim();

        var targetMoreActionsSummary = targetCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;
        var targetSwapButton = page.Locator("[data-card-more-actions-panel].is-mobile-sheet button[aria-label='Swap meal']").First;
        await targetMoreActionsSummary.ScrollIntoViewIfNeededAsync();
        await targetMoreActionsSummary.ClickAsync();
        await targetSwapButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var swapResponseTask = page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
            response.Url.Contains("/projects/aisle-pilot/swap-meal", StringComparison.OrdinalIgnoreCase));

        await targetSwapButton.ClickAsync();
        _ = await swapResponseTask;
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await page.WaitForFunctionAsync(
            """
            ([cardIndex, dayLabel, slotLabel, priorMealName]) => {
                const cards = Array.from(document.querySelectorAll("[data-day-meal-card]"));
                const card = cards[cardIndex];
                if (!(card instanceof HTMLElement)) {
                    return false;
                }

                const expander = card.querySelector("[data-day-card-expander]");
                const isOpen = !(expander instanceof HTMLDetailsElement) || expander.open;
                const currentDayLabel = (card.querySelector(".aislepilot-day-card-expander-day")?.textContent ?? "").trim();
                const activeTab = card.querySelector("[data-day-meal-tab].is-active");
                const currentSlotLabel = (activeTab?.textContent ?? "").trim();
                const currentMealName = (card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false'] h3")?.textContent ?? "").trim();
                const openMenuCount = document.querySelectorAll("[data-card-more-actions][open]").length;
                const visibleLoadingButtons = Array.from(document.querySelectorAll(".aislepilot-swap-btn.is-loading"))
                    .filter(button => button instanceof HTMLElement && button.offsetParent !== null)
                    .length;

                return isOpen &&
                    currentDayLabel === dayLabel &&
                    currentSlotLabel === slotLabel &&
                    currentMealName.length > 0 &&
                    currentMealName !== priorMealName &&
                    openMenuCount === 0 &&
                    visibleLoadingButtons === 0;
            }
            """,
            new object[] { targetCardIndex, expectedDayLabel, expectedSlotLabel, previousMealName },
            new PageWaitForFunctionOptions
            {
                Timeout = 10000
            });

        var postSwapState = await page.EvaluateAsync<string>(
            """
            ([cardIndex, priorMealName]) => {
                const cards = Array.from(document.querySelectorAll("[data-day-meal-card]"));
                const card = cards[cardIndex];
                if (!(card instanceof HTMLElement)) {
                    return "missing";
                }

                const expander = card.querySelector("[data-day-card-expander]");
                const isOpen = expander instanceof HTMLDetailsElement && expander.open ? "1" : "0";
                const dayLabel = (card.querySelector(".aislepilot-day-card-expander-day")?.textContent ?? "").trim();
                const slotLabel = (card.querySelector("[data-day-meal-tab].is-active")?.textContent ?? "").trim();
                const mealName = (card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false'] h3")?.textContent ?? "").trim();
                const sheetOpen = document.querySelector("[data-card-more-actions][open]") ? "1" : "0";
                const loadingButtons = Array.from(document.querySelectorAll(".aislepilot-swap-btn.is-loading"))
                    .filter(button => button instanceof HTMLElement && button.offsetParent !== null)
                    .length;
                const mealChanged = mealName.length > 0 && mealName !== priorMealName ? "1" : "0";
                return `${isOpen}|${dayLabel}|${slotLabel}|${mealChanged}|${sheetOpen}|${loadingButtons}`;
            }
            """,
            new object[] { targetCardIndex, previousMealName });

        var stateParts = postSwapState.Split('|', StringSplitOptions.None);
        Assert.True(stateParts.Length >= 6, $"Expected swap state payload. Actual='{postSwapState}'.");
        Assert.Equal("1", stateParts[0]);
        Assert.Equal(expectedDayLabel, stateParts[1]);
        Assert.Equal(expectedSlotLabel, stateParts[2]);
        Assert.Equal("1", stateParts[3]);
        Assert.Equal("0", stateParts[4]);
        Assert.Equal("0", stateParts[5]);
    }

    [Fact]
    public async Task Mobile_AislePilotMealImagePolling_ShowsLoadingStateForFallbackImage()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var configured = await page.EvaluateAsync<bool>(
            """
            () => {
                const pollRoot = document.querySelector("[data-meal-image-poll-root]");
                if (!(pollRoot instanceof HTMLElement)) {
                    return false;
                }

                const targetImage = pollRoot.querySelector("img[data-meal-image][data-meal-name]");
                if (!(targetImage instanceof HTMLImageElement)) {
                    return false;
                }

                pollRoot.dataset.mealImagePollEnabled = "true";
                window.sessionStorage.removeItem("aislepilot:meal-image-cache");
                const fallbackImageUrl = pollRoot.dataset.fallbackMealImageUrl?.trim() || "/projects/aisle-pilot/images/aislepilot-icon.svg";
                targetImage.src = fallbackImageUrl;

                const originalFetch = window.fetch.bind(window);
                window.fetch = () => new Promise(() => {});

                const controller = window.AislePilotMealImagePolling?.createController({
                    documentRef: document,
                    intervalMs: 60000,
                    maxAttempts: 2
                });
                if (!controller || typeof controller.start !== "function") {
                    window.fetch = originalFetch;
                    return false;
                }

                controller.start();
                window.setTimeout(() => {
                    window.fetch = originalFetch;
                }, 500);
                return true;
            }
            """);

        Assert.True(configured, "Expected to configure meal image polling test state.");

        var loadingShell = page.Locator(".aislepilot-meal-image-shell[data-meal-image-loading='true']").First;
        await loadingShell.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        Assert.True(await loadingShell.IsVisibleAsync());
    }

    [Fact]
    public async Task Mobile_AislePilotMealImagePolling_UsesSessionCacheBeforePolling()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var configured = await page.EvaluateAsync<bool>(
            """
            () => {
                const pollRoot = document.querySelector("[data-meal-image-poll-root]");
                if (!(pollRoot instanceof HTMLElement)) {
                    return false;
                }

                const targetImage = pollRoot.querySelector("img[data-meal-image][data-meal-name]");
                if (!(targetImage instanceof HTMLImageElement)) {
                    return false;
                }

                pollRoot.dataset.mealImagePollEnabled = "true";
                const fallbackImageUrl = pollRoot.dataset.fallbackMealImageUrl?.trim() || "/projects/aisle-pilot/images/aislepilot-icon.svg";
                targetImage.src = fallbackImageUrl;

                const mealName = (targetImage.dataset.mealName || "").trim();
                if (!mealName) {
                    return false;
                }

                const mealKey = mealName.toLowerCase();
                const cachedImageUrl = "/projects/aisle-pilot/images/aislepilot-meals/egg-fried-rice.png";
                window.sessionStorage.setItem(
                    "aislepilot:meal-image-cache",
                    JSON.stringify({
                        [mealKey]: {
                            url: cachedImageUrl,
                            at: Date.now()
                        }
                    }));

                const originalFetch = window.fetch.bind(window);
                let fetchCalled = false;
                window.fetch = (...args) => {
                    fetchCalled = true;
                    return originalFetch(...args);
                };
                window.__aislePilotFetchCalled = () => fetchCalled;

                const controller = window.AislePilotMealImagePolling?.createController({
                    documentRef: document,
                    intervalMs: 60000,
                    maxAttempts: 2
                });
                if (!controller || typeof controller.start !== "function") {
                    window.fetch = originalFetch;
                    return false;
                }

                controller.start();
                return true;
            }
            """);

        Assert.True(configured, "Expected to configure meal image cache test state.");

        await page.WaitForFunctionAsync(
            """
            () => {
                const image = document.querySelector("[data-meal-image-poll-root] img[data-meal-image][data-meal-name]");
                if (!(image instanceof HTMLImageElement)) {
                    return false;
                }

                const currentSrc = image.getAttribute("src") || image.currentSrc || "";
                return currentSrc.includes("aislepilot-meals/egg-fried-rice.png");
            }
            """,
            null,
            new() { Timeout = 10000 });

        var fetchCalled = await page.EvaluateAsync<bool>("() => window.__aislePilotFetchCalled?.() === true");
        Assert.False(fetchCalled);
    }

    [Fact]
    public async Task Mobile_AislePilotViewDetails_KeepsActionRowStable()
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
        await activeMealCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var inlineActionRow = activeMealCard.Locator("[data-day-card-header-actions].is-active").First;
        var viewSummaryButton = activeMealPanel.Locator(".aislepilot-meal-details-image-toggle > summary").First;
        var detailsPanel = activeMealPanel.Locator("[data-inline-details-panel]").First;

        await viewSummaryButton.ScrollIntoViewIfNeededAsync();

        var inlineActionRowBefore = await inlineActionRow.BoundingBoxAsync();
        var activeMealCardBefore = await activeMealCard.BoundingBoxAsync();
        Assert.NotNull(inlineActionRowBefore);
        Assert.NotNull(activeMealCardBefore);

        await viewSummaryButton.ClickAsync();
        await detailsPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var inlineActionRowAfter = await inlineActionRow.BoundingBoxAsync();
        var activeMealCardAfter = await activeMealCard.BoundingBoxAsync();
        Assert.NotNull(inlineActionRowAfter);
        Assert.NotNull(activeMealCardAfter);

        var actionRowShift = Math.Abs(
            (inlineActionRowAfter!.Y - activeMealCardAfter!.Y) -
            (inlineActionRowBefore!.Y - activeMealCardBefore!.Y));

        Assert.True(
            actionRowShift <= 20,
            $"Expected action icons to remain fixed when details open. Relative delta={actionRowShift}.");
        Assert.Equal("true", await viewSummaryButton.GetAttributeAsync("aria-expanded"));
    }

    [Fact]
    public async Task Mobile_AislePilotMealDetailSections_StartCollapsedAndExpandIndependently()
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

        var viewSummaryButton = activeMealPanel.Locator(".aislepilot-meal-details-image-toggle > summary").First;
        var detailsPanel = activeMealPanel.Locator("[data-inline-details-panel]").First;
        await viewSummaryButton.ScrollIntoViewIfNeededAsync();
        await viewSummaryButton.ClickAsync();
        await detailsPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var nutritionSection = activeMealPanel.Locator("[data-meal-section='nutrition']").First;
        var ingredientsSection = activeMealPanel.Locator("[data-meal-section='ingredients']").First;
        var methodSection = activeMealPanel.Locator("[data-meal-section='method']").First;
        var nutritionSummary = nutritionSection.Locator("[data-meal-section-summary]").First;
        var ingredientsSummary = ingredientsSection.Locator("[data-meal-section-summary]").First;
        var nutritionContent = nutritionSection.Locator("[data-meal-section-content]").First;
        var ingredientsContent = ingredientsSection.Locator("[data-meal-section-content]").First;
        var methodContent = methodSection.Locator("[data-meal-section-content]").First;

        Assert.False(await nutritionSection.EvaluateAsync<bool>("details => details.open"));
        Assert.False(await ingredientsSection.EvaluateAsync<bool>("details => details.open"));
        Assert.False(await methodSection.EvaluateAsync<bool>("details => details.open"));
        Assert.False(await nutritionContent.IsVisibleAsync());
        Assert.False(await ingredientsContent.IsVisibleAsync());
        Assert.False(await methodContent.IsVisibleAsync());

        await nutritionSummary.ScrollIntoViewIfNeededAsync();
        await nutritionSummary.ClickAsync();
        await nutritionContent.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        Assert.True(await nutritionSection.EvaluateAsync<bool>("details => details.open"));
        Assert.False(await ingredientsSection.EvaluateAsync<bool>("details => details.open"));
        Assert.False(await methodSection.EvaluateAsync<bool>("details => details.open"));

        await ingredientsSummary.ScrollIntoViewIfNeededAsync();
        await ingredientsSummary.ClickAsync();
        await ingredientsContent.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        Assert.True(await nutritionSection.EvaluateAsync<bool>("details => details.open"));
        Assert.True(await ingredientsSection.EvaluateAsync<bool>("details => details.open"));
        Assert.False(await methodSection.EvaluateAsync<bool>("details => details.open"));
    }

    [Fact]
    public async Task Mobile_AislePilotMoreActions_OpensWithoutViewingDetails()
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
        await activeMealCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var inlineDetails = activeMealPanel.Locator("[data-inline-details-toggle]").First;
        var detailsPanel = activeMealPanel.Locator("[data-inline-details-panel]").First;
        var moreActionsHost = activeMealCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions]").First;
        var moreActionsSummary = activeMealCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;
        var moreActionsButton = activeMealCard.Locator(
            "[data-card-more-actions] .aislepilot-card-more-actions-menu button[type='submit']").First;

        await moreActionsSummary.ScrollIntoViewIfNeededAsync();

        Assert.False(
            await inlineDetails.EvaluateAsync<bool>("details => details.open"),
            "Expected View details to start collapsed.");
        Assert.False(await detailsPanel.IsVisibleAsync());
        var activeMealCardBefore = await activeMealCard.BoundingBoxAsync();
        Assert.NotNull(activeMealCardBefore);

        await moreActionsSummary.ClickAsync();
        Assert.True(
            await moreActionsHost.EvaluateAsync<bool>("details => details.open"),
            "Expected More actions dropdown to open.");

        await moreActionsButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        Assert.True(await moreActionsButton.IsVisibleAsync());
        var activeMealCardAfter = await activeMealCard.BoundingBoxAsync();
        Assert.NotNull(activeMealCardAfter);
        var panelHeightDelta = Math.Abs(activeMealCardAfter!.Height - activeMealCardBefore!.Height);
        Assert.True(
            panelHeightDelta <= 12,
            $"Expected More actions overlay not to change panel height. Delta={panelHeightDelta}.");
        Assert.False(
            await detailsPanel.IsVisibleAsync(),
            "Opening More actions should not require opening View details.");
    }

    [Fact]
    public async Task Mobile_AislePilotMoreActionsMenu_RemainsInsideViewport()
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
        var moreActionsMenu = activeMealCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] .aislepilot-card-more-actions-menu").First;

        await moreActionsSummary.ScrollIntoViewIfNeededAsync();
        await moreActionsSummary.EvaluateAsync(
            """
            element => {
                if (!(element instanceof HTMLElement)) {
                    return;
                }

                const rect = element.getBoundingClientRect();
                const targetTop = 104;
                window.scrollBy(0, rect.top - targetTop);
            }
            """);
        await moreActionsSummary.ClickAsync();
        await moreActionsMenu.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var viewportOverflow = await page.EvaluateAsync<double>(
            """
            () => {
                const menu = document.querySelector("[data-card-more-actions][open] .aislepilot-card-more-actions-menu");
                if (!(menu instanceof HTMLElement)) {
                    return Number.POSITIVE_INFINITY;
                }

                const rect = menu.getBoundingClientRect();
                const padding = 6;
                const overflowLeft = Math.max(0, padding - rect.left);
                const overflowRight = Math.max(0, rect.right - (window.innerWidth - padding));
                const overflowTop = Math.max(0, padding - rect.top);
                const overflowBottom = Math.max(0, rect.bottom - (window.innerHeight - padding));
                return Math.max(overflowLeft, overflowRight, overflowTop, overflowBottom);
            }
            """);

        Assert.True(
            viewportOverflow <= 1.5,
            $"Expected More actions menu to stay inside viewport bounds. Overflow={viewportOverflow}px.");

        var opensDownwardNearTop = await page.EvaluateAsync<bool>(
            """
            () => {
                const summary = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions] > summary");
                const menu = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions][open] .aislepilot-card-more-actions-menu");
                if (!(summary instanceof HTMLElement) || !(menu instanceof HTMLElement)) {
                    return false;
                }

                const summaryRect = summary.getBoundingClientRect();
                const menuRect = menu.getBoundingClientRect();
                const triggerIsNearTop = summaryRect.top <= window.innerHeight * 0.55;
                if (!triggerIsNearTop) {
                    return true;
                }

                return menuRect.top >= summaryRect.bottom - 2;
            }
            """);
        Assert.True(opensDownwardNearTop, "Expected More actions menu to open downward when trigger is in the upper viewport area.");
    }

    [Fact]
    public async Task Mobile_AislePilotFirstVisibleDayCardMoreActions_PrefersDropDown()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var firstMoreActionsSummary = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;
        await firstMoreActionsSummary.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        await firstMoreActionsSummary.ScrollIntoViewIfNeededAsync();
        await firstMoreActionsSummary.ClickAsync();

        var directionMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const openMenuHost = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions][open]");
                const summary = openMenuHost?.querySelector("summary");
                const menu = openMenuHost?.querySelector(".aislepilot-card-more-actions-menu");
                if (!(openMenuHost instanceof HTMLElement) || !(summary instanceof HTMLElement) || !(menu instanceof HTMLElement)) {
                    return [1, Number.POSITIVE_INFINITY];
                }

                const summaryRect = summary.getBoundingClientRect();
                const menuRect = menu.getBoundingClientRect();
                const opensUpward = openMenuHost.classList.contains("is-drop-up") || menuRect.bottom <= summaryRect.top + 2;
                return [opensUpward ? 1 : 0, summaryRect.top];
            }
            """);

        Assert.Equal(2, directionMetrics.Length);
        Assert.Equal(0, Convert.ToInt32(directionMetrics[0]));
        Assert.True(
            Convert.ToDouble(directionMetrics[1]) < 620,
            $"Expected first visible More actions trigger to remain in the upper viewport region. Top={directionMetrics[1]}.");
    }

    private static bool IsE2EEnabled()
    {
        var value = Environment.GetEnvironmentVariable(E2EEnvVar);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IBrowserContext> CreateMobileContextAsync()
    {
        if (_playwright is null || _browser is null)
        {
            throw new InvalidOperationException("Playwright browser is not initialized.");
        }

        if (_playwright.Devices.TryGetValue("iPhone 13", out var iphone13))
        {
            return await _browser.NewContextAsync(iphone13);
        }

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = 390,
                Height = 844
            }
        });
    }

    private async Task<IBrowserContext> CreateNarrowMobileContextAsync()
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("Playwright browser is not initialized.");
        }

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IsMobile = true,
            HasTouch = true,
            DeviceScaleFactor = 2,
            ViewportSize = new ViewportSize
            {
                Width = 320,
                Height = 740
            }
        });
    }

    private async Task<IBrowserContext> CreateDesktopContextAsync()
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("Playwright browser is not initialized.");
        }

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = 1440,
                Height = 900
            }
        });
    }

    private async Task GoToFirstPostAsync(IPage page)
    {
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync(
            $"{_appHost.BaseUrl}/blog",
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

        var postTitleLinks = page.Locator("main .post-title-link");
        await postTitleLinks.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var firstPostLink = postTitleLinks.First;
        var firstPostPath = await firstPostLink.GetAttributeAsync("href");
        Assert.False(string.IsNullOrWhiteSpace(firstPostPath), "Expected first blog post link to have an href.");
        await firstPostLink.ClickAsync();
        await page.WaitForURLAsync($"**{firstPostPath}", new PageWaitForURLOptions
        {
            Timeout = 15000
        });
        await page.Locator("#post-title").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
        await page.Locator("#add-comment-title").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
    }

    private async Task GoToAislePilotAndGeneratePlanAsync(IPage page)
    {
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var generateButton = page.Locator("form.aislepilot-form button[type='submit']:has-text('Generate weekly plan')");
        await generateButton.ScrollIntoViewIfNeededAsync();
        // Some mobile emulation runs report transient hit-target interception while layout settles.
        // Force-click keeps downstream scenario tests deterministic; direct hit-testing has its own regression test.
        await generateButton.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
    }

    private async Task LoginAsync(IPage page, string username)
    {
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/Admin/Login");
        await page.FillAsync("#username", username);
        await page.FillAsync("#password", AdminPassword);
        await page.ClickAsync("form button[type='submit']");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        Assert.Contains("/Admin", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LogoutAsync(IPage page)
    {
        await page.ClickAsync("button:has-text('Logout')");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Contains("/Blog", page.Url, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LocalAppHost : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output;

        private LocalAppHost(Process process, string baseUrl, StringBuilder output)
        {
            _process = process;
            BaseUrl = baseUrl;
            _output = output;
        }

        public string BaseUrl { get; }

        public static async Task<LocalAppHost> StartAsync()
        {
            var port = GetFreePort();
            var baseUrl = $"http://127.0.0.1:{port}";
            var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var projectPath = Path.Combine(repoRoot, "MyBlog", "MyBlog.csproj");
            var output = new StringBuilder();

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectPath}\"",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.Environment["PORT"] = port.ToString();
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["ADMIN_USERNAME"] = AdminUsername;
            startInfo.Environment["ADMIN_PASSWORD"] = AdminPassword;
            startInfo.Environment["SUBSCRIBER_NOTIFY_KEY"] = "integration-notify-key";
            startInfo.Environment["OPENAI_API_KEY"] = string.Empty;

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    output.AppendLine(args.Data);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    output.AppendLine(args.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await WaitUntilReadyAsync(baseUrl, process, output);
            return new LocalAppHost(process, baseUrl, output);
        }

        public async ValueTask DisposeAsync()
        {
            if (_process.HasExited)
            {
                _process.Dispose();
                return;
            }

            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            finally
            {
                _process.Dispose();
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static async Task WaitUntilReadyAsync(string baseUrl, Process process, StringBuilder output)
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };

            var timeoutAt = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < timeoutAt)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Local app process exited before startup completed. Output:{Environment.NewLine}{output}");
                }

                try
                {
                    using var response = await client.GetAsync($"{baseUrl}/health");
                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        return;
                    }
                }
                catch
                {
                    // App may still be starting.
                }

                await Task.Delay(500);
            }

            throw new TimeoutException(
                $"Timed out waiting for local app startup at {baseUrl}. Output:{Environment.NewLine}{output}");
        }
    }
}
