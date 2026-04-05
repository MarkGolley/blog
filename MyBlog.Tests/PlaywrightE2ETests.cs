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

        var moreActionsTriggers = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary");
        var moreActionsTriggerCount = await moreActionsTriggers.CountAsync();
        Assert.True(moreActionsTriggerCount > 0, "Expected at least one meal actions menu trigger to be rendered.");

        var targetIndex = Math.Min(3, moreActionsTriggerCount - 1);
        var targetTrigger = moreActionsTriggers.Nth(targetIndex);
        var targetSwapButton = targetTrigger.Locator("xpath=ancestor::details[1]").Locator("button[aria-label='Swap meal']").First;
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
    public async Task NarrowMobile_AislePilotMealCardActionButtons_RemainVisibleWithinViewport()
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

        var submitted = await page.EvaluateAsync<bool>(
            """
            () => {
                const form = document.querySelector("form.aislepilot-form");
                if (!(form instanceof HTMLFormElement)) {
                    return false;
                }

                form.requestSubmit();
                return true;
            }
            """);
        Assert.True(submitted, "Expected to submit the AislePilot generator form.");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

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

        var viewDetailsSummary = activeMealPanel.Locator(".aislepilot-meal-details-image-toggle > summary").First;
        var detailsPanel = activeMealPanel.Locator("[data-inline-details-panel]").First;
        var moreActionsSummary = activeMealCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;

        await moreActionsSummary.ScrollIntoViewIfNeededAsync();
        await viewDetailsSummary.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await moreActionsSummary.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var actionOverflow = await page.EvaluateAsync<double>(
            """
            () => {
                const controls = Array.from(document.querySelectorAll(
                    "[data-day-card-header-actions].is-active [data-card-more-actions] > summary"));
                if (controls.length === 0) {
                    return Number.POSITIVE_INFINITY;
                }

                const padding = 6;
                return controls.reduce((maxOverflow, control) => {
                    if (!(control instanceof HTMLElement)) {
                        return Number.POSITIVE_INFINITY;
                    }

                    const rect = control.getBoundingClientRect();
                    if (rect.width <= 0 || rect.height <= 0) {
                        return Number.POSITIVE_INFINITY;
                    }

                    const overflowLeft = Math.max(0, padding - rect.left);
                    const overflowRight = Math.max(0, rect.right - (window.innerWidth - padding));
                    return Math.max(maxOverflow, overflowLeft, overflowRight);
                }, 0);
            }
            """);

        Assert.True(
            actionOverflow <= 1.5,
            $"Expected meal action controls to render fully inside viewport on narrow mobile. Overflow={actionOverflow}px.");

        var actionRowOffset = await page.EvaluateAsync<double>(
            """
            () => {
                const card = document.querySelector("[data-day-meal-card]:has(.aislepilot-day-meal-panel[aria-hidden='false'])");
                if (!(card instanceof HTMLElement)) {
                    return Number.POSITIVE_INFINITY;
                }

                const heading = card.querySelector(".aislepilot-day-card-head-main .aislepilot-card-kicker");
                const actionRow = card.querySelector("[data-day-card-header-actions].is-active");
                if (!(heading instanceof HTMLElement) || !(actionRow instanceof HTMLElement)) {
                    return Number.POSITIVE_INFINITY;
                }

                const headingRect = heading.getBoundingClientRect();
                const actionRect = actionRow.getBoundingClientRect();
                return Math.abs(headingRect.top - actionRect.top);
            }
            """);

        Assert.True(
            actionRowOffset <= 40,
            $"Expected top-right action buttons to align with the meal title on narrow mobile. Offset={actionRowOffset}px.");

        var actionRowInHeaderBand = await page.EvaluateAsync<bool>(
            """
            () => {
                const card = document.querySelector("[data-day-meal-card]:has(.aislepilot-day-meal-panel[aria-hidden='false'])");
                if (!(card instanceof HTMLElement)) {
                    return false;
                }

                const head = card.querySelector(".aislepilot-day-card-head");
                const actionRow = card.querySelector("[data-day-card-header-actions].is-active");
                if (!(head instanceof HTMLElement) || !(actionRow instanceof HTMLElement)) {
                    return false;
                }

                const headRect = head.getBoundingClientRect();
                const actionRect = actionRow.getBoundingClientRect();
                const tolerance = 2;
                return actionRect.top >= headRect.top - tolerance && actionRect.bottom <= headRect.bottom + tolerance;
            }
            """);
        Assert.True(actionRowInHeaderBand, "Expected swap and menu icons to stay inside the day header row on narrow mobile.");

        var imageTriggerWidthRatio = await page.EvaluateAsync<double>(
            """
            () => {
                const panel = document.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']");
                if (!(panel instanceof HTMLElement)) {
                    return Number.POSITIVE_INFINITY;
                }

                const imageSummary = panel.querySelector(".aislepilot-meal-details-image-toggle > summary");
                if (!(imageSummary instanceof HTMLElement)) {
                    return Number.POSITIVE_INFINITY;
                }

                const panelRect = panel.getBoundingClientRect();
                const summaryRect = imageSummary.getBoundingClientRect();
                if (panelRect.width <= 0) {
                    return Number.POSITIVE_INFINITY;
                }

                return summaryRect.width / panelRect.width;
            }
            """);

        Assert.True(
            imageTriggerWidthRatio >= 0.94 && imageTriggerWidthRatio <= 1.08,
            $"Expected image details trigger to span the card content width on narrow mobile. Ratio={imageTriggerWidthRatio:F2}.");

        var titleAndBottomSpacing = await page.EvaluateAsync<string>(
            """
            () => {
                const panel = document.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']");
                if (!(panel instanceof HTMLElement)) {
                    return "Infinity|Infinity";
                }

                const title = panel.querySelector(":scope > h3");
                const imageShell = panel.querySelector(".aislepilot-meal-details-image-toggle .aislepilot-meal-image-shell");
                if (!(title instanceof HTMLElement) || !(imageShell instanceof HTMLElement)) {
                    return "Infinity|Infinity";
                }

                const titleRect = title.getBoundingClientRect();
                const imageRect = imageShell.getBoundingClientRect();
                const titleOverlap = Math.max(0, titleRect.bottom - imageRect.top);

                const visibleChildren = Array.from(panel.children)
                    .filter(child => child instanceof HTMLElement)
                    .filter(child => !(child instanceof HTMLElement && child.matches("[hidden], .aislepilot-meal-removed-watermark")));
                const panelRect = panel.getBoundingClientRect();
                const furthestContentBottom = visibleChildren.reduce((maxBottom, child) => {
                    if (!(child instanceof HTMLElement)) {
                        return maxBottom;
                    }

                    return Math.max(maxBottom, child.getBoundingClientRect().bottom);
                }, panelRect.top);
                const trailingGap = Math.max(0, panelRect.bottom - furthestContentBottom);
                return `${titleOverlap}|${trailingGap}`;
            }
            """);
        var spacingParts = (titleAndBottomSpacing ?? "Infinity|Infinity").Split('|');
        var titleOverlap = spacingParts.Length > 0 && double.TryParse(spacingParts[0], out var parsedTitleOverlap)
            ? parsedTitleOverlap
            : double.PositiveInfinity;
        var trailingGap = spacingParts.Length > 1 && double.TryParse(spacingParts[1], out var parsedTrailingGap)
            ? parsedTrailingGap
            : double.PositiveInfinity;

        Assert.True(titleOverlap <= 1.5, $"Expected meal title to stay clear of image area. Overlap={titleOverlap}px.");
        Assert.True(trailingGap <= 22, $"Expected meal card to avoid large empty space at bottom. Gap={trailingGap}px.");

        var worstTrailingGapAcrossCards = await page.EvaluateAsync<double>(
            """
            () => {
                const panels = Array.from(document.querySelectorAll(".aislepilot-day-meal-panel[aria-hidden='false']"));
                if (panels.length === 0) {
                    return Number.POSITIVE_INFINITY;
                }

                let worstGap = 0;
                for (const panel of panels) {
                    if (!(panel instanceof HTMLElement)) {
                        return Number.POSITIVE_INFINITY;
                    }

                    const visibleChildren = Array.from(panel.children)
                        .filter(child => child instanceof HTMLElement)
                        .filter(child => !(child instanceof HTMLElement && child.matches("[hidden], .aislepilot-meal-removed-watermark")));
                    const panelRect = panel.getBoundingClientRect();
                    const furthestContentBottom = visibleChildren.reduce((maxBottom, child) => {
                        if (!(child instanceof HTMLElement)) {
                            return maxBottom;
                        }

                        return Math.max(maxBottom, child.getBoundingClientRect().bottom);
                    }, panelRect.top);
                    const trailingGapPx = Math.max(0, panelRect.bottom - furthestContentBottom);
                    worstGap = Math.max(worstGap, trailingGapPx);
                }

                return worstGap;
            }
            """);
        Assert.True(
            worstTrailingGapAcrossCards <= 24,
            $"Expected all visible meal panels to avoid inconsistent bottom whitespace. Max gap={worstTrailingGapAcrossCards}px.");

        await viewDetailsSummary.ClickAsync();
        await detailsPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        Assert.Equal("true", await viewDetailsSummary.GetAttributeAsync("aria-expanded"));
    }

    [Fact]
    public async Task NarrowMobile_AislePilotMoreActionsMenu_RendersWhenOpened()
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

        var submitted = await page.EvaluateAsync<bool>(
            """
            () => {
                const form = document.querySelector("form.aislepilot-form");
                if (!(form instanceof HTMLFormElement)) {
                    return false;
                }

                form.requestSubmit();
                return true;
            }
            """);
        Assert.True(submitted, "Expected to submit the AislePilot generator form.");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var activeMealPanel = page.Locator(".aislepilot-day-meal-panel[aria-hidden='false']").First;
        await activeMealPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var activeMealCard = page.Locator("[data-day-meal-card]:has(.aislepilot-day-meal-panel[aria-hidden='false'])").First;
        var moreActionsSummary = activeMealCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;
        var moreActionsMenu = activeMealCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] .aislepilot-card-more-actions-menu").First;
        var moreActionsFirstButton = activeMealCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] .aislepilot-card-more-actions-menu button[type='submit']").First;

        await moreActionsSummary.ScrollIntoViewIfNeededAsync();
        await moreActionsSummary.ClickAsync();
        await moreActionsMenu.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await moreActionsFirstButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var menuMetrics = await page.EvaluateAsync<string>(
            """
            () => {
                const menu = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions][open] .aislepilot-card-more-actions-menu");
                if (!(menu instanceof HTMLElement)) {
                    return "0|0|0";
                }

                const rect = menu.getBoundingClientRect();
                const viewportPadding = 6;
                const isInViewport =
                    rect.left >= viewportPadding &&
                    rect.right <= window.innerWidth - viewportPadding &&
                    rect.top >= viewportPadding &&
                    rect.bottom <= window.innerHeight - viewportPadding;
                return `${rect.width}|${rect.height}|${isInViewport ? "1" : "0"}`;
            }
            """);
        var metricParts = (menuMetrics ?? "0|0|0").Split('|');
        var menuWidth = metricParts.Length > 0 && double.TryParse(metricParts[0], out var parsedWidth) ? parsedWidth : 0;
        var menuHeight = metricParts.Length > 1 && double.TryParse(metricParts[1], out var parsedHeight) ? parsedHeight : 0;
        var menuInViewport = metricParts.Length > 2 && string.Equals(metricParts[2], "1", StringComparison.Ordinal);

        Assert.True(menuWidth >= 120, $"Expected More actions menu width to be rendered. Width={menuWidth}px.");
        Assert.True(menuHeight >= 70, $"Expected More actions menu height to be rendered. Height={menuHeight}px.");
        Assert.True(menuInViewport, "Expected More actions menu to render fully inside the viewport.");

        var layoutMetrics = await page.EvaluateAsync<string>(
            """
            () => {
                const doc = document.documentElement;
                const body = document.body;
                const scrollWidth = Math.max(doc?.scrollWidth ?? 0, body?.scrollWidth ?? 0);
                const overflowX = Math.max(0, scrollWidth - window.innerWidth);

                const panel = document.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']");
                if (!(panel instanceof HTMLElement)) {
                    return `${overflowX}|0|0|0|0`;
                }

                const shell = panel.querySelector(".aislepilot-meal-image-shell");
                const image = panel.querySelector("img.aislepilot-meal-image");
                if (!(shell instanceof HTMLElement) || !(image instanceof HTMLImageElement)) {
                    return `${overflowX}|0|0|0|0`;
                }

                const shellRect = shell.getBoundingClientRect();
                const imageRect = image.getBoundingClientRect();
                return `${overflowX}|${shellRect.width}|${shellRect.height}|${imageRect.width}|${imageRect.height}`;
            }
            """);
        var layoutParts = (layoutMetrics ?? "0|0|0|0|0").Split('|');
        var pageOverflowX = layoutParts.Length > 0 && double.TryParse(layoutParts[0], out var parsedOverflowX) ? parsedOverflowX : double.PositiveInfinity;
        var shellWidth = layoutParts.Length > 1 && double.TryParse(layoutParts[1], out var parsedShellWidth) ? parsedShellWidth : 0;
        var shellHeight = layoutParts.Length > 2 && double.TryParse(layoutParts[2], out var parsedShellHeight) ? parsedShellHeight : 0;
        var imageWidth = layoutParts.Length > 3 && double.TryParse(layoutParts[3], out var parsedImageWidth) ? parsedImageWidth : 0;
        var imageHeight = layoutParts.Length > 4 && double.TryParse(layoutParts[4], out var parsedImageHeight) ? parsedImageHeight : 0;

        Assert.True(pageOverflowX <= 1.5, $"Expected opening More actions not to introduce horizontal page overflow. Overflow={pageOverflowX}px.");
        Assert.True(shellWidth >= 180 && shellHeight >= 95, $"Expected meal image shell to remain at a healthy rendered size. Width={shellWidth}px Height={shellHeight}px.");
        Assert.True(imageWidth >= shellWidth - 3 && imageHeight >= shellHeight - 3,
            $"Expected meal image to remain fully rendered inside the shell after opening More actions. Image={imageWidth}x{imageHeight}, Shell={shellWidth}x{shellHeight}.");
    }

    [Fact]
    public async Task NarrowMobile_AislePilotMoreActionsMenu_KeepsButtonsReachableNearViewportBottom()
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

        var submitted = await page.EvaluateAsync<bool>(
            """
            () => {
                const form = document.querySelector("form.aislepilot-form");
                if (!(form instanceof HTMLFormElement)) {
                    return false;
                }

                form.requestSubmit();
                return true;
            }
            """);
        Assert.True(submitted, "Expected to submit the AislePilot generator form.");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var activeMealPanels = page.Locator(".aislepilot-day-meal-panel[aria-hidden='false']");
        await activeMealPanels.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
        Assert.True(await activeMealPanels.CountAsync() > 0, "Expected at least one visible meal panel.");

        var targetSummary = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").Last;
        await targetSummary.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await targetSummary.ScrollIntoViewIfNeededAsync();
        await targetSummary.EvaluateAsync(
            """
            element => {
                if (!(element instanceof HTMLElement)) {
                    return;
                }

                const rect = element.getBoundingClientRect();
                const desiredBottom = window.innerHeight - 10;
                const delta = rect.bottom - desiredBottom;
                if (delta > 0) {
                    window.scrollBy(0, delta);
                }
            }
            """);

        var pageScrollBeforeOpen = await page.EvaluateAsync<double>("() => Math.round(window.scrollY)");
        await targetSummary.ClickAsync();

        var targetMenu = targetSummary.Locator("xpath=ancestor::details[1]").Locator(".aislepilot-card-more-actions-menu");
        await targetMenu.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var firstButton = targetMenu.Locator("button[type='submit']").First;
        var lastButton = targetMenu.Locator("button[type='submit']").Last;
        await firstButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await lastButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var menuReadability = await page.EvaluateAsync<string>(
            """
            () => {
                const openMenu = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions][open] .aislepilot-card-more-actions-menu");
                const openSummary = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions][open] > summary");
                if (!(openMenu instanceof HTMLElement)) {
                    return "0|0|0";
                }

                const buttons = openMenu.querySelectorAll("button[type='submit']");
                const lastButton = buttons.length > 0 ? buttons[buttons.length - 1] : null;
                if (!(lastButton instanceof HTMLElement)) {
                    return "0|0|0";
                }

                const buttonRect = lastButton.getBoundingClientRect();
                const viewportPadding = 6;
                const buttonFullyVisible =
                    buttonRect.top >= viewportPadding &&
                    buttonRect.bottom <= window.innerHeight - viewportPadding;
                const internallyScrollable = openMenu.scrollHeight - openMenu.clientHeight > 1.5;
                let opensUpward = false;
                if (openSummary instanceof HTMLElement) {
                    const menuRect = openMenu.getBoundingClientRect();
                    const summaryRect = openSummary.getBoundingClientRect();
                    opensUpward = menuRect.bottom <= summaryRect.top + 2;
                }
                return `${buttonFullyVisible ? "1" : "0"}|${internallyScrollable ? "1" : "0"}|${opensUpward ? "1" : "0"}`;
            }
            """);
        var readabilityParts = (menuReadability ?? "0|0|0").Split('|');
        var lastButtonInViewport = readabilityParts.Length > 0 && string.Equals(readabilityParts[0], "1", StringComparison.Ordinal);
        var menuInternallyScrollable = readabilityParts.Length > 1 && string.Equals(readabilityParts[1], "1", StringComparison.Ordinal);

        var pageScrollAfterOpen = await page.EvaluateAsync<double>("() => Math.round(window.scrollY)");
        var pageScrollDelta = Math.Abs(pageScrollAfterOpen - pageScrollBeforeOpen);
        Assert.True(lastButtonInViewport, "Expected the final More actions button to be immediately visible after opening near viewport bottom.");
        Assert.False(menuInternallyScrollable, "Expected More actions menu to avoid internal scrolling for this narrow-mobile scenario.");
        Assert.True(
            pageScrollDelta <= 120,
            $"Expected opening More actions near viewport bottom to avoid excessive page movement. Delta={pageScrollDelta}px.");
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

        var initiallySaved = await saveButton.EvaluateAsync<bool>(
            "button => button instanceof HTMLButtonElement && button.classList.contains('is-saved-meal')");
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
    public async Task Mobile_AislePilotDayCardSummary_TracksSelectedMealTab()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var card = page.Locator("[data-day-meal-card]:has([data-day-card-summary]):has([data-day-meal-tab])").First;
        await card.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var summary = card.Locator("[data-day-card-summary]").First;
        var tabs = card.Locator("[data-day-meal-tab]");
        var tabCount = await tabs.CountAsync();
        Assert.True(tabCount >= 2, "Expected at least two meal tabs on the card.");

        var visibleHeaderActionRowsBefore = await card.EvaluateAsync<int>(
            """
            cardRoot => {
                if (!(cardRoot instanceof HTMLElement)) {
                    return -1;
                }

                const rows = Array.from(cardRoot.querySelectorAll("[data-day-card-header-actions]"));
                return rows.filter(row => row instanceof HTMLElement && window.getComputedStyle(row).display !== "none").length;
            }
            """);
        Assert.Equal(1, visibleHeaderActionRowsBefore);

        var initialSummary = (await summary.InnerTextAsync()).Trim();
        var targetTabIndex = -1;
        var expectedSummary = string.Empty;
        for (var index = 0; index < tabCount; index++)
        {
            var candidateSummary = (await tabs.Nth(index).GetAttributeAsync("data-day-card-summary-value") ?? string.Empty).Trim();
            if (candidateSummary.Length == 0)
            {
                continue;
            }

            if (!candidateSummary.Equals(initialSummary, StringComparison.OrdinalIgnoreCase))
            {
                targetTabIndex = index;
                expectedSummary = candidateSummary;
                break;
            }
        }

        Assert.True(targetTabIndex >= 0, "Expected at least one meal tab to expose a different summary value.");

        var targetTab = tabs.Nth(targetTabIndex);
        await targetTab.ScrollIntoViewIfNeededAsync();
        await targetTab.ClickAsync();
        await page.WaitForTimeoutAsync(150);

        var updatedSummary = (await summary.InnerTextAsync()).Trim();
        Assert.Equal(expectedSummary, updatedSummary);

        var visibleHeaderActionRowsAfter = await card.EvaluateAsync<int>(
            """
            cardRoot => {
                if (!(cardRoot instanceof HTMLElement)) {
                    return -1;
                }

                const rows = Array.from(cardRoot.querySelectorAll("[data-day-card-header-actions]"));
                return rows.filter(row => row instanceof HTMLElement && window.getComputedStyle(row).display !== "none").length;
            }
            """);
        Assert.Equal(1, visibleHeaderActionRowsAfter);

        var activeHeaderActionSlot = await card.EvaluateAsync<int>(
            """
            cardRoot => {
                if (!(cardRoot instanceof HTMLElement)) {
                    return -1;
                }

                const activeRows = Array.from(cardRoot.querySelectorAll("[data-day-card-header-actions].is-active"));
                if (activeRows.length !== 1 || !(activeRows[0] instanceof HTMLElement)) {
                    return -1;
                }

                return Number.parseInt(activeRows[0].dataset.slotIndex ?? "-1", 10);
            }
            """);
        Assert.Equal(targetTabIndex, activeHeaderActionSlot);
    }

    [Fact]
    public async Task Mobile_AislePilotDayCardHeader_HasSpacedSummaryAndCompactLeftoverToggle()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var card = page.Locator("[data-day-meal-card]:has([data-day-card-summary]):has([data-leftover-day-zone])").First;
        await card.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var toggleButton = card.Locator("[data-leftover-toggle-sign]").First;
        var dayZone = card.Locator("[data-leftover-day-zone]").First;
        var dayIndex = await dayZone.GetAttributeAsync("data-day-index");
        Assert.False(string.IsNullOrWhiteSpace(dayIndex));

        var beforeMetrics = await card.EvaluateAsync<double[]>(
            """
            cardRoot => {
                if (!(cardRoot instanceof HTMLElement)) {
                    return [-1, -1, -1];
                }

                const kicker = cardRoot.querySelector(".aislepilot-day-card-head-main .aislepilot-card-kicker");
                const summary = cardRoot.querySelector(".aislepilot-day-card-head-main .aislepilot-day-card-meta[data-day-card-summary]");
                const zone = cardRoot.querySelector("[data-leftover-day-zone]");
                const button = zone?.querySelector("[data-leftover-toggle-sign]");
                if (!(kicker instanceof HTMLElement) || !(summary instanceof HTMLElement) || !(zone instanceof HTMLElement) || !(button instanceof HTMLElement)) {
                    return [-1, -1, -1];
                }

                const kickerRect = kicker.getBoundingClientRect();
                const summaryRect = summary.getBoundingClientRect();
                const zoneRect = zone.getBoundingClientRect();
                const buttonRect = button.getBoundingClientRect();
                const spacingGap = Math.max(0, summaryRect.left - kickerRect.right);
                const controlSlack = Math.max(0, zoneRect.width - buttonRect.width);
                return [spacingGap, zoneRect.width, controlSlack];
            }
            """);

        Assert.Equal(3, beforeMetrics.Length);
        Assert.True(beforeMetrics[0] >= 10, $"Expected day-to-summary spacing to be wider. Actual={beforeMetrics[0]:F1}px.");
        Assert.True(beforeMetrics[1] <= 56, $"Expected compact leftover control width. Actual={beforeMetrics[1]:F1}px.");
        Assert.True(beforeMetrics[2] <= 20, $"Expected little slack around leftover icon. Actual={beforeMetrics[2]:F1}px.");

        await toggleButton.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(220);

        var afterToggleMetrics = await page.EvaluateAsync<double[]>(
            """
            dayIndex => {
                const safeDayIndex = typeof dayIndex === "string" ? dayIndex : "";
                const zone = document.querySelector(`[data-leftover-day-zone][data-day-index="${safeDayIndex}"]`);
                const button = zone?.querySelector("[data-leftover-toggle-sign]");
                if (!(zone instanceof HTMLElement) || !(button instanceof HTMLElement)) {
                    return [-1, -1];
                }

                const zoneRect = zone.getBoundingClientRect();
                const buttonRect = button.getBoundingClientRect();
                const controlSlack = Math.max(0, zoneRect.width - buttonRect.width);
                return [zoneRect.width, controlSlack];
            }
            """,
            dayIndex ?? string.Empty);

        Assert.Equal(2, afterToggleMetrics.Length);
        Assert.True(afterToggleMetrics[0] <= 56, $"Expected compact leftover control after toggle. Actual={afterToggleMetrics[0]:F1}px.");
        Assert.True(afterToggleMetrics[1] <= 20, $"Expected minimal control slack after toggle. Actual={afterToggleMetrics[1]:F1}px.");
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

    [Fact]
    public async Task Desktop_AislePilotWindowTabs_KeepActiveHighlightAfterSwitching()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var mealsTab = page.Locator("#aislepilot-tab-meals").First;
        var shoppingTab = page.Locator("#aislepilot-tab-shop").First;
        var exportTab = page.Locator("#aislepilot-tab-export").First;

        await shoppingTab.ClickAsync();
        await page.Locator("#aislepilot-shop[aria-hidden='false']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });

        Assert.Equal("true", await shoppingTab.GetAttributeAsync("aria-selected"));
        Assert.Equal("page", await shoppingTab.GetAttributeAsync("aria-current"));
        var shoppingClass = await shoppingTab.GetAttributeAsync("class") ?? string.Empty;
        Assert.Contains("is-active", shoppingClass, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("false", await mealsTab.GetAttributeAsync("aria-selected"));
        Assert.Equal("false", await mealsTab.GetAttributeAsync("aria-current"));

        var shopHasGradient = await page.EvaluateAsync<bool>(
            """
            () => {
                const tab = document.querySelector("#aislepilot-tab-shop");
                if (!(tab instanceof HTMLElement)) {
                    return false;
                }

                const backgroundImage = window.getComputedStyle(tab).backgroundImage || "";
                return backgroundImage.toLowerCase().includes("gradient");
            }
            """);
        var mealsHasGradient = await page.EvaluateAsync<bool>(
            """
            () => {
                const tab = document.querySelector("#aislepilot-tab-meals");
                if (!(tab instanceof HTMLElement)) {
                    return false;
                }

                const backgroundImage = window.getComputedStyle(tab).backgroundImage || "";
                return backgroundImage.toLowerCase().includes("gradient");
            }
            """);
        Assert.True(shopHasGradient);
        Assert.False(mealsHasGradient);

        await exportTab.ClickAsync();
        await page.Locator("#aislepilot-export[aria-hidden='false']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });

        Assert.Equal("true", await exportTab.GetAttributeAsync("aria-selected"));
        Assert.Equal("page", await exportTab.GetAttributeAsync("aria-current"));
        var exportClass = await exportTab.GetAttributeAsync("class") ?? string.Empty;
        Assert.Contains("is-active", exportClass, StringComparison.OrdinalIgnoreCase);
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
