using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests : IAsyncLifetime
{

    [Fact]
    public async Task Mobile_AislePilotDayCardHeader_UsesCompactMealActionsWithoutDayLevelControls()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var headerMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const trigger = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions] > summary");
                if (!(trigger instanceof HTMLElement)) {
                    return [-1, -1, "missing", -1];
                }

                const triggerRect = trigger.getBoundingClientRect();
                const triggerLabel = trigger.querySelector(".aislepilot-swap-action-label");
                const triggerLabelDisplay = triggerLabel instanceof HTMLElement
                    ? window.getComputedStyle(triggerLabel).display
                    : "missing";
                const inlineLeftoverButton = document.querySelector("[data-day-card-header-actions].is-active")
                    ?.closest(".aislepilot-day-card-head-actions")
                    ?.querySelector(".aislepilot-day-card-leftover-controls [data-leftover-toggle-sign]");
                return [
                    triggerRect.width,
                    triggerRect.height,
                    triggerLabelDisplay,
                    inlineLeftoverButton instanceof HTMLElement ? 1 : 0
                ];
            }
            """);

        Assert.Equal(4, headerMetrics.Length);
        Assert.True(Convert.ToDouble(headerMetrics[0]) <= 44, $"Expected compact more-actions trigger width. Width={headerMetrics[0]}.");
        Assert.True(Convert.ToDouble(headerMetrics[1]) <= 44, $"Expected compact more-actions trigger height. Height={headerMetrics[1]}.");
        Assert.Equal("none", Convert.ToString(headerMetrics[2]));
        Assert.Equal(0, Convert.ToInt32(headerMetrics[3]));

        var moreActionsSummary = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;
        await moreActionsSummary.ClickAsync();

        var mobileSheetHeader = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions][open] .aislepilot-mobile-meal-sheet-head").First;
        await mobileSheetHeader.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        var sheetModeEnabled = await page.EvaluateAsync<int[]>(
            """
            () => {
                const details = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions][open]");
                if (!(details instanceof HTMLDetailsElement)) {
                    return [0, 0, 0, 0, 0, 0];
                }

                const menu = document.querySelector("[data-card-more-actions-panel].is-mobile-sheet");
                if (!(menu instanceof HTMLElement)) {
                    return [0, 0, 0, 0, 0, 0];
                }

                const rect = menu.getBoundingClientRect();
                const computed = window.getComputedStyle(menu);
                const isFixed = computed.position === "fixed" ? 1 : 0;
                const inViewport = rect.top >= 0 && rect.bottom <= window.innerHeight ? 1 : 0;
                const renderedHeight = rect.height >= 120 ? 1 : 0;
                const parentIsBody = menu.parentElement === document.body ? 1 : 0;
                const trappedInsideDetails = details.contains(menu) ? 1 : 0;
                return [details.open ? 1 : 0, isFixed, inViewport, renderedHeight, parentIsBody, trappedInsideDetails];
            }
            """);
        Assert.Equal(6, sheetModeEnabled.Length);
        Assert.Equal(1, sheetModeEnabled[0]);
        Assert.Equal(1, sheetModeEnabled[1]);
        Assert.Equal(1, sheetModeEnabled[2]);
        Assert.Equal(1, sheetModeEnabled[3]);
        Assert.Equal(1, sheetModeEnabled[4]);
        Assert.Equal(0, sheetModeEnabled[5]);
        await page.WaitForTimeoutAsync(220);
        var sheetPersistence = await page.EvaluateAsync<int[]>(
            """
            () => {
                const details = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions]");
                const backdrop = document.querySelector(".aislepilot-mobile-meal-sheet-backdrop");
                const isOpen = details instanceof HTMLDetailsElement && details.open ? 1 : 0;
                const backdropVisible = backdrop instanceof HTMLElement && !backdrop.hasAttribute("hidden") ? 1 : 0;
                const menuStillPortaled = document.querySelector("[data-card-more-actions-panel].is-mobile-sheet") instanceof HTMLElement ? 1 : 0;
                return [isOpen, backdropVisible, menuStillPortaled];
            }
            """);
        Assert.Equal(3, sheetPersistence.Length);
        Assert.Equal(1, sheetPersistence[0]);
        Assert.Equal(1, sheetPersistence[1]);
        Assert.Equal(1, sheetPersistence[2]);
        Assert.Equal(0, await page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions][open] [data-leftover-toggle-sign]").CountAsync());
    }

    [Fact]
    public async Task Mobile_AislePilotMealActionsSheet_ClosesOnBackdropTapAndActionTap()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var moreActionsSummary = page.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;
        await moreActionsSummary.ClickAsync();

        var mobileSheetHeader = page.Locator("[data-card-more-actions-panel].is-mobile-sheet .aislepilot-mobile-meal-sheet-head").First;
        await mobileSheetHeader.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        await page.WaitForTimeoutAsync(260);
        await page.Locator(".aislepilot-mobile-meal-sheet-backdrop").First.ClickAsync();
        await page.WaitForTimeoutAsync(520);

        var closedByBackdrop = await page.EvaluateAsync<int[]>(
            """
            () => {
                const details = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions]");
                const backdrop = document.querySelector(".aislepilot-mobile-meal-sheet-backdrop");
                const isOpen = details instanceof HTMLDetailsElement && details.open ? 1 : 0;
                const isClosing = details instanceof HTMLElement && details.classList.contains("is-closing") ? 1 : 0;
                const backdropVisible = backdrop instanceof HTMLElement && !backdrop.hasAttribute("hidden") ? 1 : 0;
                const bodyLocked = document.body.classList.contains("aislepilot-mobile-meal-sheet-open") ? 1 : 0;
                const portaledSheet = document.querySelector("[data-card-more-actions-panel].is-mobile-sheet");
                return [isOpen, isClosing, backdropVisible, bodyLocked, portaledSheet instanceof HTMLElement ? 1 : 0];
            }
            """);

        Assert.Equal(5, closedByBackdrop.Length);
        Assert.Equal(0, closedByBackdrop[0]);
        Assert.Equal(0, closedByBackdrop[1]);
        Assert.Equal(0, closedByBackdrop[2]);
        Assert.Equal(0, closedByBackdrop[3]);
        Assert.Equal(0, closedByBackdrop[4]);

        await moreActionsSummary.ClickAsync();
        await mobileSheetHeader.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var saveAction = page.Locator("[data-card-more-actions-panel].is-mobile-sheet .aislepilot-favorite-form button[type='submit']").First;
        await saveAction.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var closedAfterAction = await page.EvaluateAsync<int[]>(
            """
            () => {
                const openSheet = document.querySelector("[data-card-more-actions][open]");
                const closingSheet = document.querySelector("[data-card-more-actions].is-closing");
                const backdrop = document.querySelector(".aislepilot-mobile-meal-sheet-backdrop");
                const backdropVisible = backdrop instanceof HTMLElement && !backdrop.hasAttribute("hidden") ? 1 : 0;
                const bodyLocked = document.body.classList.contains("aislepilot-mobile-meal-sheet-open") ? 1 : 0;
                const portaledSheet = document.querySelector("[data-card-more-actions-panel].is-mobile-sheet");
                return [
                    openSheet instanceof HTMLDetailsElement ? 1 : 0,
                    closingSheet instanceof HTMLElement ? 1 : 0,
                    backdropVisible,
                    bodyLocked,
                    portaledSheet instanceof HTMLElement ? 1 : 0
                ];
            }
            """);

        Assert.Equal(5, closedAfterAction.Length);
        Assert.Equal(0, closedAfterAction[0]);
        Assert.Equal(0, closedAfterAction[1]);
        Assert.Equal(0, closedAfterAction[2]);
        Assert.Equal(0, closedAfterAction[3]);
        Assert.Equal(0, closedAfterAction[4]);
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
    public async Task Mobile_AislePilotDayCardSwipe_OnCardSurfaceChangesMealSlot()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var card = page.Locator("[data-day-meal-card]:has([data-day-meal-tab])").First;
        await card.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var initialState = await card.EvaluateAsync<int[]>(
            """
            cardRoot => {
                if (!(cardRoot instanceof HTMLElement)) {
                    return [-1, -1];
                }

                const tabs = Array.from(cardRoot.querySelectorAll("[data-day-meal-tab]"));
                if (tabs.length < 2) {
                    return [-1, -1];
                }

                const panels = Array.from(cardRoot.querySelectorAll("[data-day-meal-panel]"));
                const activePanelIndex = panels.findIndex(panel => panel instanceof HTMLElement && panel.getAttribute("aria-hidden") !== "true");
                return [tabs.length, activePanelIndex];
            }
            """);
        Assert.True(initialState.Length >= 2, "Expected card state to include tab count and active panel index.");
        var tabCount = initialState[0];
        var activeIndexBeforeSwipe = initialState[1];
        Assert.True(tabCount >= 2, "Expected at least two meal tabs on the swipe target card.");
        Assert.True(activeIndexBeforeSwipe >= 0, "Expected one active meal panel before swiping.");

        var swipeDispatched = await card.EvaluateAsync<bool>(
            """
            cardRoot => {
                if (!(cardRoot instanceof HTMLElement)) {
                    return false;
                }

                const tabs = cardRoot.querySelectorAll("[data-day-meal-tab]");
                if (tabs.length < 2) {
                    return false;
                }

                const rect = cardRoot.getBoundingClientRect();
                if (rect.width < 90 || rect.height < 40) {
                    return false;
                }

                const edgeInset = Math.min(36, rect.width * 0.15);
                const startX = rect.right - edgeInset;
                const endX = rect.left + edgeInset;
                const y = rect.top + Math.max(30, Math.min(rect.height * 0.6, rect.height - 30));

                const createTouchEvent = (type, x, y) => {
                    const event = new Event(type, { bubbles: false, cancelable: true });
                    Object.defineProperty(event, "changedTouches", {
                        configurable: true,
                        value: [{ clientX: x, clientY: y }]
                    });
                    return event;
                };

                cardRoot.dispatchEvent(createTouchEvent("touchstart", startX, y));
                cardRoot.dispatchEvent(createTouchEvent("touchend", endX, y));
                return true;
            }
            """);
        Assert.True(swipeDispatched, "Expected to dispatch a swipe gesture on the day meal card.");
        await page.WaitForTimeoutAsync(100);

        var activeIndexAfterSwipe = await card.EvaluateAsync<int>(
            """
            cardRoot => {
                if (!(cardRoot instanceof HTMLElement)) {
                    return -1;
                }

                const panels = Array.from(cardRoot.querySelectorAll("[data-day-meal-panel]"));
                return panels.findIndex(panel => panel instanceof HTMLElement && panel.getAttribute("aria-hidden") !== "true");
            }
            """);

        var expectedIndexAfterSwipe = (activeIndexBeforeSwipe + 1) % tabCount;
        Assert.Equal(expectedIndexAfterSwipe, activeIndexAfterSwipe);
    }

    [Fact]
    public async Task Mobile_AislePilotDayCardSwipe_DoesNotSwitchMainWindowPanel()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var card = page.Locator("[data-day-meal-card]:has([data-day-meal-tab])").First;
        await card.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var activePanelBeforeSwipe = await page.EvaluateAsync<string>(
            """
            () => {
                const activePanel = document.querySelector(".aislepilot-window-panel[aria-hidden='false']");
                return activePanel instanceof HTMLElement ? (activePanel.id || "") : "";
            }
            """);
        Assert.Equal("aislepilot-meals", activePanelBeforeSwipe);

        var swipeState = await card.EvaluateAsync<int[]>(
            """
            cardRoot => {
                if (!(cardRoot instanceof HTMLElement)) {
                    return [-1, -1, -1];
                }

                const tabs = Array.from(cardRoot.querySelectorAll("[data-day-meal-tab]"));
                if (tabs.length < 2) {
                    return [-1, -1, -1];
                }

                const panels = Array.from(cardRoot.querySelectorAll("[data-day-meal-panel]"));
                const beforeIndex = panels.findIndex(panel => panel instanceof HTMLElement && panel.getAttribute("aria-hidden") !== "true");

                const rect = cardRoot.getBoundingClientRect();
                if (rect.width < 90 || rect.height < 40) {
                    return [tabs.length, beforeIndex, -1];
                }

                const edgeInset = Math.min(36, rect.width * 0.15);
                const startX = rect.right - edgeInset;
                const endX = rect.left + edgeInset;
                const y = rect.top + Math.max(30, Math.min(rect.height * 0.6, rect.height - 30));

                const createTouchEvent = (type, x, y) => {
                    const event = new Event(type, { bubbles: true, cancelable: true });
                    Object.defineProperty(event, "changedTouches", {
                        configurable: true,
                        value: [{ clientX: x, clientY: y }]
                    });
                    return event;
                };

                cardRoot.dispatchEvent(createTouchEvent("touchstart", startX, y));
                cardRoot.dispatchEvent(createTouchEvent("touchend", endX, y));

                const afterIndex = panels.findIndex(panel => panel instanceof HTMLElement && panel.getAttribute("aria-hidden") !== "true");
                return [tabs.length, beforeIndex, afterIndex];
            }
            """);
        Assert.True(swipeState.Length >= 3, "Expected swipe state to include tab count and panel indexes.");
        var tabCount = swipeState[0];
        var beforeIndex = swipeState[1];
        var afterIndex = swipeState[2];
        Assert.True(tabCount >= 2, "Expected at least two meal tabs on the swipe target card.");
        Assert.True(beforeIndex >= 0, "Expected one active meal panel before swiping.");
        Assert.True(afterIndex >= 0, "Expected one active meal panel after swiping.");
        Assert.Equal((beforeIndex + 1) % tabCount, afterIndex);

        await page.WaitForTimeoutAsync(100);

        var activePanelAfterSwipe = await page.EvaluateAsync<string>(
            """
            () => {
                const activePanel = document.querySelector(".aislepilot-window-panel[aria-hidden='false']");
                return activePanel instanceof HTMLElement ? (activePanel.id || "") : "";
            }
            """);
        Assert.Equal("aislepilot-meals", activePanelAfterSwipe);
    }

    [Fact]
    public async Task Mobile_AislePilotDayCardHeader_HasSpacedSummaryAndMealActionsStayMealScoped()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        await page.Locator("[data-day-meal-card]:has([data-day-card-summary]):has([data-leftover-day-zone])").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var summaryMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector("[data-day-meal-card]:has([data-day-card-summary]):has([data-leftover-day-zone])");
                if (!(card instanceof HTMLElement)) {
                    return [-1, -1];
                }

                const kicker = card.querySelector(".aislepilot-day-card-head-main .aislepilot-card-kicker");
                const summary = card.querySelector(".aislepilot-day-card-head-main .aislepilot-day-card-meta[data-day-card-summary]");
                const zone = card.querySelector("[data-leftover-day-zone]");
                if (!(kicker instanceof HTMLElement) || !(summary instanceof HTMLElement) || !(zone instanceof HTMLElement)) {
                    return [-1, -1];
                }

                const kickerRect = kicker.getBoundingClientRect();
                const summaryRect = summary.getBoundingClientRect();
                const spacingGap = Math.max(0, summaryRect.left - kickerRect.right);
                const zoneHidden = window.getComputedStyle(zone).display === "none" ? 1 : 0;
                return [spacingGap, zoneHidden];
            }
            """);
        Assert.Equal(2, summaryMetrics.Length);
        Assert.True(summaryMetrics[0] >= 10, $"Expected day-to-summary spacing to be wider. Actual={summaryMetrics[0]:F1}px.");
        Assert.Equal(1, Convert.ToInt32(summaryMetrics[1]));

        var targetCard = page.Locator("[data-day-meal-card]:has([data-leftover-day-zone])").First;
        await targetCard.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var moreActionsSummary = targetCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;
        await moreActionsSummary.ClickAsync();

        var mealMenuMetrics = await targetCard.EvaluateAsync<string>(
            """
            cardRoot => {
                if (!(cardRoot instanceof HTMLElement)) {
                    return "0|0";
                }

                const openMenu = cardRoot.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions][open] .aislepilot-card-more-actions-menu");
                if (!(openMenu instanceof HTMLElement)) {
                    return "0|0";
                }

                const hasLeftoverAction = openMenu.querySelector("[data-leftover-toggle-sign]") instanceof HTMLElement ? 1 : 0;
                const sheetTitle = openMenu.querySelector(".aislepilot-mobile-meal-sheet-title");
                const hasTitle = sheetTitle instanceof HTMLElement && (sheetTitle.textContent ?? "").trim().length > 0 ? 1 : 0;
                return `${hasLeftoverAction}|${hasTitle}`;
            }
            """);
        var mealMenuParts = (mealMenuMetrics ?? "0|0").Split('|');
        var hasLeftoverActionInMealMenu = mealMenuParts.Length > 0 && string.Equals(mealMenuParts[0], "1", StringComparison.Ordinal);
        var hasSheetTitle = mealMenuParts.Length > 1 && string.Equals(mealMenuParts[1], "1", StringComparison.Ordinal);
        Assert.False(hasLeftoverActionInMealMenu, "Expected meal menu to contain meal-scoped actions only.");
        Assert.True(hasSheetTitle, "Expected mobile meal actions sheet to show the active meal title.");
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
    [Trait("Category", "E2ESmoke")]
    public async Task Mobile_AislePilotGeneratePlan_PrimarySubmitIsNotObstructed()
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

        // Allow setup-mode reveal/layout hooks to settle before evaluating hit-testing.
        await page.WaitForTimeoutAsync(350);

        var hitTestReport = await page.EvaluateAsync<string>(
            """
            () => {
                const button = document.querySelector("#aislepilot-setup-form button[data-setup-mode-submit='planner']");
                if (!(button instanceof HTMLButtonElement)) {
                    return "button-missing";
                }

                button.scrollIntoView({ block: "center", inline: "nearest" });
                const rect = button.getBoundingClientRect();
                if (rect.width <= 0 || rect.height <= 0) {
                    return "button-zero-size";
                }
                const scrollSnapshot = `scrollY=${window.scrollY.toFixed(1)} innerH=${window.innerHeight.toFixed(1)} docH=${document.documentElement.scrollHeight.toFixed(1)}`;

                const points = [
                    [rect.left + (rect.width / 2), rect.top + (rect.height / 2)],
                    [rect.left + 14, rect.top + (rect.height / 2)],
                    [rect.right - 14, rect.top + (rect.height / 2)]
                ];

                for (const [x, y] of points) {
                    const hit = document.elementFromPoint(x, y);
                    if (!(hit instanceof Element)) {
                        return `hit-missing@${x.toFixed(1)},${y.toFixed(1)} ${scrollSnapshot} buttonRect=${rect.left.toFixed(1)},${rect.top.toFixed(1)},${rect.width.toFixed(1)},${rect.height.toFixed(1)}`;
                    }

                    if (hit !== button && !button.contains(hit)) {
                        const hitClass = typeof hit.className === "string" ? hit.className : "";
                        const hitRect = hit.getBoundingClientRect();
                        return `blocked:${hit.tagName.toLowerCase()}.${hitClass}@${x.toFixed(1)},${y.toFixed(1)} ${scrollSnapshot} buttonRect=${rect.left.toFixed(1)},${rect.top.toFixed(1)},${rect.width.toFixed(1)},${rect.height.toFixed(1)} hitRect=${hitRect.left.toFixed(1)},${hitRect.top.toFixed(1)},${hitRect.width.toFixed(1)},${hitRect.height.toFixed(1)}`;
                    }
                }

                return "ok";
            }
            """);

        if (!string.Equals(hitTestReport, "ok", StringComparison.Ordinal))
        {
            throw new Xunit.Sdk.XunitException(hitTestReport);
        }
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
}
