using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;

namespace MyBlog.Tests;

[Trait("Category", "E2E")]
public sealed class PlaywrightE2ETests : IAsyncLifetime
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

        var swapButtons = page.Locator(
            ".aislepilot-day-meal-panel[aria-hidden='false'] form[action*='/swap-meal'] button.aislepilot-swap-btn:has-text('Swap meal')");
        var swapButtonCount = await swapButtons.CountAsync();
        Assert.True(swapButtonCount > 0, "Expected at least one swap meal button to be rendered.");

        var targetIndex = Math.Min(3, swapButtonCount - 1);
        var targetSwapButton = swapButtons.Nth(targetIndex);
        await targetSwapButton.ScrollIntoViewIfNeededAsync();

        var beforeScrollY = await page.EvaluateAsync<int>("() => Math.round(window.scrollY)");
        Assert.True(beforeScrollY > 100, $"Expected to scroll below hero before swap. Actual scrollY={beforeScrollY}.");

        var swapResponseTask = page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
            response.Url.Contains("/projects/aisle-pilot/swap-meal", StringComparison.OrdinalIgnoreCase));

        await targetSwapButton.ClickAsync();
        _ = await swapResponseTask;
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(700);

        // Allow delayed scroll-restore hooks to complete.
        await page.WaitForTimeoutAsync(500);

        var afterScrollY = await page.EvaluateAsync<int>("() => Math.round(window.scrollY)");
        var scrollDelta = Math.Abs(afterScrollY - beforeScrollY);

        Assert.True(
            scrollDelta <= 60,
            $"Expected swap postback to keep viewport stable. Before={beforeScrollY}, After={afterScrollY}, Delta={scrollDelta}.");
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

        var mealTimePill = activeMealPanel.Locator(".aislepilot-card-primary-row .aislepilot-card-meta-pill").First;
        var inlineActionRow = activeMealPanel.Locator(".aislepilot-card-action-row.is-inline-actions").First;
        var viewSummaryButton = activeMealPanel.Locator("[data-inline-details-toggle] > summary").First;
        var detailsPanel = activeMealPanel.Locator("[data-inline-details-panel]").First;

        await viewSummaryButton.ScrollIntoViewIfNeededAsync();

        var mealTimePillBefore = await mealTimePill.BoundingBoxAsync();
        var inlineActionRowBefore = await inlineActionRow.BoundingBoxAsync();
        var activeMealPanelBefore = await activeMealPanel.BoundingBoxAsync();
        Assert.NotNull(mealTimePillBefore);
        Assert.NotNull(inlineActionRowBefore);
        Assert.NotNull(activeMealPanelBefore);

        await viewSummaryButton.ClickAsync();
        await detailsPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var mealTimePillAfter = await mealTimePill.BoundingBoxAsync();
        var inlineActionRowAfter = await inlineActionRow.BoundingBoxAsync();
        var activeMealPanelAfter = await activeMealPanel.BoundingBoxAsync();
        Assert.NotNull(mealTimePillAfter);
        Assert.NotNull(inlineActionRowAfter);
        Assert.NotNull(activeMealPanelAfter);

        var actionRowShift = Math.Abs(
            (inlineActionRowAfter!.Y - activeMealPanelAfter!.Y) -
            (inlineActionRowBefore!.Y - activeMealPanelBefore!.Y));
        var mealTimeShift = Math.Abs(
            (mealTimePillAfter!.Y - activeMealPanelAfter!.Y) -
            (mealTimePillBefore!.Y - activeMealPanelBefore!.Y));

        Assert.True(
            actionRowShift <= 35,
            $"Expected action icons to remain fixed when details open. Relative delta={actionRowShift}.");
        Assert.True(
            mealTimeShift <= 35,
            $"Expected meal time pill to remain fixed when details open. Relative delta={mealTimeShift}.");
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

        var inlineDetails = activeMealPanel.Locator("[data-inline-details-toggle]").First;
        var detailsPanel = activeMealPanel.Locator("[data-inline-details-panel]").First;
        var moreActionsHost = activeMealPanel.Locator("[data-card-more-actions]").First;
        var moreActionsSummary = activeMealPanel.Locator("[data-card-more-actions] > summary").First;
        var moreActionsButton = activeMealPanel.Locator(
            "[data-card-more-actions] .aislepilot-card-more-actions-menu button[type='submit']").First;

        await moreActionsSummary.ScrollIntoViewIfNeededAsync();

        Assert.False(
            await inlineDetails.EvaluateAsync<bool>("details => details.open"),
            "Expected View details to start collapsed.");
        Assert.False(await detailsPanel.IsVisibleAsync());
        var activeMealPanelBefore = await activeMealPanel.BoundingBoxAsync();
        Assert.NotNull(activeMealPanelBefore);

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
        var activeMealPanelAfter = await activeMealPanel.BoundingBoxAsync();
        Assert.NotNull(activeMealPanelAfter);
        var panelHeightDelta = Math.Abs(activeMealPanelAfter!.Height - activeMealPanelBefore!.Height);
        Assert.True(
            panelHeightDelta <= 10,
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

        var moreActionsSummary = activeMealPanel.Locator("[data-card-more-actions] > summary").First;
        var moreActionsMenu = activeMealPanel.Locator("[data-card-more-actions] .aislepilot-card-more-actions-menu").First;

        await moreActionsSummary.ScrollIntoViewIfNeededAsync();
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
    }

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

        var moreActionsSummary = activeMealPanel.Locator("[data-card-more-actions] > summary").First;
        var saveButton = activeMealPanel.Locator(
            "[data-card-more-actions] .aislepilot-favorite-form button[type='submit']").First;
        var toasts = page.Locator(".aislepilot-toast");

        await moreActionsSummary.ScrollIntoViewIfNeededAsync();
        await moreActionsSummary.ClickAsync();
        await saveButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var initiallySaved = await saveButton.EvaluateAsync<bool>(
            "button => button instanceof HTMLButtonElement && button.classList.contains('is-saved-meal')");
        var expectedFirstToast = initiallySaved
            ? "Meal removed from saved meals."
            : "Meal saved.";
        var expectedSecondToast = expectedFirstToast.Equals("Meal saved.", StringComparison.Ordinal)
            ? "Meal removed from saved meals."
            : "Meal saved.";

        var beforeFirstToastCount = await toasts.CountAsync();
        var firstToggleResponseTask = page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
            response.Url.Contains("/projects/aisle-pilot/toggle-enjoyed-meal", StringComparison.OrdinalIgnoreCase));

        await saveButton.ClickAsync();
        _ = await firstToggleResponseTask;
        await page.WaitForFunctionAsync(
            "previousCount => document.querySelectorAll('.aislepilot-toast').length > previousCount",
            beforeFirstToastCount,
            new() { Timeout = 10000 });
        var firstToastText = (await toasts.Last.InnerTextAsync()).Trim();
        Assert.Equal(expectedFirstToast, firstToastText);

        await moreActionsSummary.ClickAsync();
        await saveButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var beforeSecondToastCount = await toasts.CountAsync();
        var secondToggleResponseTask = page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
            response.Url.Contains("/projects/aisle-pilot/toggle-enjoyed-meal", StringComparison.OrdinalIgnoreCase));

        await saveButton.ClickAsync();
        _ = await secondToggleResponseTask;
        await page.WaitForFunctionAsync(
            "previousCount => document.querySelectorAll('.aislepilot-toast').length > previousCount",
            beforeSecondToastCount,
            new() { Timeout = 10000 });
        var secondToastText = (await toasts.Last.InnerTextAsync()).Trim();
        Assert.Equal(expectedSecondToast, secondToastText);
    }

    [Fact]
    public async Task Mobile_AislePilotStickyContext_CanJumpBetweenPanels()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var stickyContext = page.Locator(".aislepilot-mobile-context").First;
        await stickyContext.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var shoppingJump = stickyContext.Locator("button[data-window-tab='aislepilot-shop']").First;
        await shoppingJump.ClickAsync();

        var shoppingPanel = page.Locator("#aislepilot-shop[aria-hidden='false']").First;
        await shoppingPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 15000
        });
        Assert.Equal("true", await shoppingJump.GetAttributeAsync("aria-selected"));

        var exportsJump = stickyContext.Locator("button[data-window-tab='aislepilot-export']").First;
        await exportsJump.ClickAsync();

        var exportPanel = page.Locator("#aislepilot-export[aria-hidden='false']").First;
        await exportPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 15000
        });
        Assert.Equal("true", await exportsJump.GetAttributeAsync("aria-selected"));
    }

    [Fact]
    public async Task Mobile_AislePilotQuickJumpTabs_SupportKeyboardNavigation()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var shoppingJump = page.Locator(".aislepilot-mobile-context-jump[data-window-tab='aislepilot-shop']").First;
        var exportsJump = page.Locator(".aislepilot-mobile-context-jump[data-window-tab='aislepilot-export']").First;

        await shoppingJump.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("true", await exportsJump.GetAttributeAsync("aria-selected"));
        await page.Locator("#aislepilot-export[aria-hidden='false']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });

        await page.Keyboard.PressAsync("Home");
        var mealsJump = page.Locator(".aislepilot-mobile-context-jump[data-window-tab='aislepilot-meals']").First;
        Assert.Equal("true", await mealsJump.GetAttributeAsync("aria-selected"));
        await page.Locator("#aislepilot-meals[aria-hidden='false']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });
    }

    [Fact]
    public async Task Mobile_AislePilotGeneratePlan_SubmitHookShowsPlanSkeletonShell()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var skeletonShown = await page.EvaluateAsync<bool>(
            """
            () => {
                const appRoot = document.querySelector(".aislepilot-app");
                const shell = document.querySelector("[data-plan-loading-shell]");
                const form = document.querySelector("#aislepilot-setup-form");
                const plannerButton = form?.querySelector("button[data-setup-mode-submit='planner'][data-show-plan-skeleton]");
                if (!(appRoot instanceof HTMLElement) ||
                    !(shell instanceof HTMLElement) ||
                    !(form instanceof HTMLFormElement) ||
                    !(plannerButton instanceof HTMLButtonElement) ||
                    typeof SubmitEvent !== "function") {
                    return false;
                }

                const submitEvent = new SubmitEvent("submit", {
                    bubbles: true,
                    cancelable: true,
                    submitter: plannerButton
                });

                form.dispatchEvent(submitEvent);

                return appRoot.classList.contains("is-plan-loading") &&
                    !shell.hasAttribute("hidden") &&
                    shell.getAttribute("aria-hidden") === "false";
            }
            """);

        Assert.True(skeletonShown, "Expected planner submit handler to show the plan skeleton shell.");
    }

    [Fact]
    public async Task Mobile_AislePilotGeneratePlan_SubmitHookLocksButtonWidthToPreventJump()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var widthDelta = await page.EvaluateAsync<double>(
            """
            () => {
                const form = document.querySelector("#aislepilot-setup-form");
                const plannerButton = form?.querySelector("button[data-setup-mode-submit='planner']");
                if (!(form instanceof HTMLFormElement) || !(plannerButton instanceof HTMLButtonElement) || typeof SubmitEvent !== "function") {
                    return Number.POSITIVE_INFINITY;
                }

                const widthBefore = plannerButton.getBoundingClientRect().width;
                form.addEventListener("submit", event => {
                    event.preventDefault();
                }, { once: true });

                const submitEvent = new SubmitEvent("submit", {
                    bubbles: true,
                    cancelable: true,
                    submitter: plannerButton
                });
                form.dispatchEvent(submitEvent);

                const widthDuring = plannerButton.getBoundingClientRect().width;
                return Math.abs(widthDuring - widthBefore);
            }
            """);

        Assert.True(
            widthDelta <= 1.5,
            $"Expected submit loading state to preserve button width. Delta={widthDelta}px.");
    }

    [Fact]
    public async Task Mobile_AislePilotBreakfastTabs_DoNotOverflowDayCardWidth()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var breakfastTab = page.Locator(".aislepilot-day-meal-tab", new() { HasText = "Breakfast" }).First;
        await breakfastTab.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var overflowIssueCount = await page.EvaluateAsync<int>(
            """
            () => {
                const tolerancePx = 1.5;
                const cards = Array.from(document.querySelectorAll("[data-day-meal-card]"));
                const breakfastCards = cards.filter(card => card.querySelectorAll(".aislepilot-day-meal-tab").length >= 3);
                if (breakfastCards.length === 0) {
                    return -1;
                }

                let issues = 0;
                for (const card of breakfastCards) {
                    const grid = card.closest(".aislepilot-meal-grid");
                    if (!(grid instanceof HTMLElement)) {
                        issues++;
                        continue;
                    }

                    if (card.scrollWidth - card.clientWidth > tolerancePx) {
                        issues++;
                    }

                    const gridRect = grid.getBoundingClientRect();
                    const cardRect = card.getBoundingClientRect();
                    if (cardRect.left < gridRect.left - tolerancePx || cardRect.right > gridRect.right + tolerancePx) {
                        issues++;
                    }

                    const tabList = card.querySelector(".aislepilot-day-meal-tabs");
                    if (!(tabList instanceof HTMLElement)) {
                        issues++;
                        continue;
                    }

                    if (tabList.scrollWidth - tabList.clientWidth > tolerancePx) {
                        issues++;
                    }

                    const listRect = tabList.getBoundingClientRect();
                    if (listRect.left < cardRect.left - tolerancePx || listRect.right > cardRect.right + tolerancePx) {
                        issues++;
                    }

                    const tabs = Array.from(tabList.querySelectorAll(".aislepilot-day-meal-tab"));
                    if (tabs.length !== 3) {
                        issues++;
                        continue;
                    }

                    for (const tab of tabs) {
                        if (!(tab instanceof HTMLElement)) {
                            issues++;
                            continue;
                        }

                        const tabRect = tab.getBoundingClientRect();
                        if (tabRect.left < listRect.left - tolerancePx || tabRect.right > listRect.right + tolerancePx) {
                            issues++;
                            break;
                        }
                    }
                }

                return issues;
            }
            """);

        Assert.NotEqual(-1, overflowIssueCount);
        Assert.Equal(
            0,
            overflowIssueCount);
    }

    [Fact]
    public async Task Desktop_LoginLogoutLogin_DoesNotReturnBadRequest()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();
        var sawBadRequest = false;

        page.Response += (_, response) =>
        {
            if (response.Status == (int)HttpStatusCode.BadRequest)
            {
                sawBadRequest = true;
            }
        };

        await LoginAsync(page, AdminUsername);
        await LogoutAsync(page);
        await LoginAsync(page, AdminUsername);

        Assert.False(sawBadRequest, "Encountered one or more HTTP 400 responses during login/logout flow.");
        Assert.Contains("/Admin", page.Url, StringComparison.OrdinalIgnoreCase);
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

        await page.GotoAsync($"{_appHost.BaseUrl}/blog");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var firstPostLink = page.Locator("main a[href^='/blog/']").First;
        await firstPostLink.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
        await generateButton.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator(".aislepilot-swap-btn").First.WaitForAsync(new LocatorWaitForOptions
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
