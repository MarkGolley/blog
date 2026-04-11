using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;
namespace MyBlog.Tests;
public sealed partial class PlaywrightE2ETests : IAsyncLifetime
{
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

        Assert.True(menuWidth >= 48, $"Expected More actions menu width to be rendered. Width={menuWidth}px.");
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
        await targetSummary.EvaluateAsync(
            """
            element => {
                if (!(element instanceof HTMLElement)) {
                    return;
                }

                const detailsToggle = document.querySelector(".aislepilot-day-meal-panel[aria-hidden='false'] [data-inline-details-toggle]");
                if (detailsToggle instanceof HTMLDetailsElement) {
                    detailsToggle.open = false;
                }
                element.scrollIntoView({ block: "end", inline: "nearest" });
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

        var lastButton = targetMenu.Locator("button[type='submit']").Last;
        await lastButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var menuReadability = await page.EvaluateAsync<string>(
            """
            () => {
                const openMenu = document.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions][open] .aislepilot-card-more-actions-menu");
                if (!(openMenu instanceof HTMLElement)) {
                    return "0|0|0|0|0";
                }

                const buttons = Array.from(openMenu.querySelectorAll("button"));
                if (buttons.length === 0) {
                    return "0|0|0|0|0";
                }

                const internallyScrollable = openMenu.scrollHeight - openMenu.clientHeight > 1.5;
                const viewportPadding = 6;
                const hiddenButtonCount = buttons.reduce((count, button) => {
                    if (!(button instanceof HTMLElement)) {
                        return count + 1;
                    }

                    const rect = button.getBoundingClientRect();
                    const fullyVisible =
                        rect.top >= viewportPadding &&
                        rect.bottom <= window.innerHeight - viewportPadding &&
                        rect.left >= viewportPadding &&
                        rect.right <= window.innerWidth - viewportPadding;
                    const hit = document.elementFromPoint(rect.left + (rect.width / 2), rect.top + (rect.height / 2));
                    const unclipped = hit instanceof Element && (hit === button || button.contains(hit));
                    return fullyVisible && unclipped ? count : count + 1;
                }, 0);
                const viewportHost = openMenu.closest("[data-window-viewport], .aislepilot-window-viewport");
                const viewportRect = viewportHost instanceof HTMLElement ? viewportHost.getBoundingClientRect() : null;
                const hiddenPanelBleed = viewportRect
                    ? Array.from(document.querySelectorAll(".aislepilot-window-panel[aria-hidden='true']")).some(panel => {
                        if (!(panel instanceof HTMLElement)) return false;
                        const rect = panel.getBoundingClientRect();
                        return rect.right > viewportRect.left + 2 && rect.left < viewportRect.right - 2 && rect.bottom > viewportRect.top + 2 && rect.top < viewportRect.bottom - 2;
                    })
                    : false;
                return `${internallyScrollable ? "1" : "0"}|${hiddenPanelBleed ? "1" : "0"}|${hiddenButtonCount}|${buttons.length}`;
            }
            """);
        var readabilityParts = (menuReadability ?? "0|0|0|0").Split('|');
        var menuInternallyScrollable = readabilityParts.Length > 0 && string.Equals(readabilityParts[0], "1", StringComparison.Ordinal);
        var hiddenPanelBleed = readabilityParts.Length > 1 && string.Equals(readabilityParts[1], "1", StringComparison.Ordinal);
        var hiddenButtonCount = readabilityParts.Length > 2 && int.TryParse(readabilityParts[2], out var parsedHiddenButtonCount) ? parsedHiddenButtonCount : int.MaxValue;
        var buttonCount = readabilityParts.Length > 3 && int.TryParse(readabilityParts[3], out var parsedButtonCount) ? parsedButtonCount : 0;

        var pageScrollAfterOpen = await page.EvaluateAsync<double>("() => Math.round(window.scrollY)");
        var pageScrollDelta = Math.Abs(pageScrollAfterOpen - pageScrollBeforeOpen);
        Assert.False(menuInternallyScrollable, "Expected More actions menu to avoid internal scrolling for this narrow-mobile scenario.");
        Assert.True(buttonCount >= 3, $"Expected meal actions menu to expose at least three action buttons. Count={buttonCount}.");
        Assert.Equal(0, hiddenButtonCount);
        Assert.False(hiddenPanelBleed, "Expected hidden tabs (like shopping list) not to bleed into the meal viewport when More actions opens.");
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
    public async Task Mobile_AislePilotSavedMealsMenu_ShowsReadableMealRowsWithoutTruncation()
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

        var headMenuTrigger = page.Locator("[data-head-menu] > summary").First;
        await headMenuTrigger.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await headMenuTrigger.ClickAsync();

        var menuMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const panel = document.querySelector(".aislepilot-head-menu-panel");
                if (!(panel instanceof HTMLElement)) {
                    return [0, "missing", "missing", -1, Number.POSITIVE_INFINITY];
                }

                let list = panel.querySelector(".aislepilot-head-saved-meal-list");
                if (!(list instanceof HTMLUListElement)) {
                    list = document.createElement("ul");
                    list.className = "aislepilot-head-saved-meal-list";
                    panel.appendChild(list);
                }

                let row = list.querySelector(".aislepilot-head-saved-meal-row");
                if (!(row instanceof HTMLLIElement)) {
                    row = document.createElement("li");
                    row.className = "aislepilot-head-saved-meal-row";
                    row.innerHTML =
                        "<span class='aislepilot-head-saved-meal-name'>Warm roasted vegetable tray bake with lemon herb chicken</span>" +
                        "<button type='button' class='aislepilot-head-week-action-btn is-danger' aria-label='Remove saved meal'>x</button>";
                    list.appendChild(row);
                }

                const name = row.querySelector(".aislepilot-head-saved-meal-name");
                if (!(panel instanceof HTMLElement) || !(row instanceof HTMLElement) || !(name instanceof HTMLElement)) {
                    return [0, "missing", "missing", -1, Number.POSITIVE_INFINITY];
                }

                const styles = window.getComputedStyle(name);
                const rowRect = row.getBoundingClientRect();
                const panelRect = panel.getBoundingClientRect();
                const viewportPadding = 4;
                const rowVisibleWithinPanel =
                    rowRect.top >= panelRect.top - 1 &&
                    rowRect.bottom <= Math.min(panelRect.bottom, window.innerHeight - viewportPadding) + 1;

                const panelOverflowBottom = Math.max(0, panelRect.bottom - (window.innerHeight - viewportPadding));
                return [
                    rowVisibleWithinPanel ? 1 : 0,
                    styles.whiteSpace || "",
                    styles.textOverflow || "",
                    rowRect.height,
                    panelOverflowBottom
                ];
            }
            """);

        Assert.Equal(5, menuMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(menuMetrics[0]));
        Assert.NotEqual("nowrap", Convert.ToString(menuMetrics[1]));
        Assert.NotEqual("ellipsis", Convert.ToString(menuMetrics[2]));
        Assert.True(Convert.ToDouble(menuMetrics[3]) >= 30, $"Expected saved meal rows to remain readable. Height={menuMetrics[3]}px.");
        Assert.True(
            Convert.ToDouble(menuMetrics[4]) <= 1.5,
            $"Expected saved meals menu to fit the viewport without forcing page scroll. Overflow={menuMetrics[4]}px.");
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
    public async Task Mobile_AislePilotStickyContext_StaysBelowShellHeader()
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

        await page.EvaluateAsync(
            """
            () => {
                window.scrollTo(0, 240);
            }
            """);
        await page.WaitForTimeoutAsync(150);

        var headerGap = await page.EvaluateAsync<double>(
            """
            () => {
                const header = document.querySelector(".app-shell-header");
                const context = document.querySelector(".aislepilot-mobile-context");
                if (!(header instanceof HTMLElement) || !(context instanceof HTMLElement)) {
                    return Number.NEGATIVE_INFINITY;
                }

                const headerRect = header.getBoundingClientRect();
                const contextRect = context.getBoundingClientRect();
                return contextRect.top - headerRect.bottom;
            }
            """);
        var shellHeaderOffset = await page.EvaluateAsync<double>(
            """
            () => {
                const app = document.querySelector(".aislepilot-app");
                if (!(app instanceof HTMLElement)) {
                    return Number.NEGATIVE_INFINITY;
                }

                const raw = window.getComputedStyle(app).getPropertyValue("--ap-shell-header-offset");
                const parsed = Number.parseFloat(raw || "0");
                return Number.isFinite(parsed) ? parsed : Number.NEGATIVE_INFINITY;
            }
            """);

        Assert.True(
            headerGap >= -1,
            $"Expected sticky context to remain below shell header. Gap={headerGap:F1}px.");
        Assert.True(
            shellHeaderOffset >= 28,
            $"Expected shell header offset CSS variable to be set from header height. Value={shellHeaderOffset:F1}px.");
    }

    [Fact]
    public async Task Mobile_AislePilotOverviewActions_UseHamburgerMenuForRefreshAndSettings()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var actionMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const actionBar = document.querySelector(".aislepilot-overview-actions");
                const titleRow = document.querySelector(".aislepilot-overview-title-row");
                const title = titleRow?.querySelector(".aislepilot-section-title");
                const titleToggleButton = titleRow?.querySelector("[data-overview-toggle]");
                const inlineActions = actionBar?.querySelector(".aislepilot-overview-actions-inline");
                const overviewMenu = actionBar?.querySelector("[data-overview-actions-menu]");
                const menuTrigger = overviewMenu?.querySelector("summary");
                if (!(actionBar instanceof HTMLElement) ||
                    !(title instanceof HTMLElement) ||
                    !(titleToggleButton instanceof HTMLElement) ||
                    !(inlineActions instanceof HTMLElement) ||
                    !(overviewMenu instanceof HTMLElement) ||
                    !(menuTrigger instanceof HTMLElement)) {
                    return ["missing", "missing", -1, -1, -1];
                }

                const titleRect = title.getBoundingClientRect();
                const titleToggleRect = titleToggleButton.getBoundingClientRect();
                const menuTriggerRect = menuTrigger.getBoundingClientRect();
                const inlineDisplay = window.getComputedStyle(inlineActions).display;
                const dropdownDisplay = window.getComputedStyle(overviewMenu).display;

                return [
                    inlineDisplay,
                    dropdownDisplay,
                    menuTriggerRect.width,
                    Math.abs(titleToggleRect.top - menuTriggerRect.top),
                    Math.max(0, titleToggleRect.left - titleRect.right)
                ];
            }
            """);

        Assert.Equal(5, actionMetrics.Length);
        Assert.Equal("none", Convert.ToString(actionMetrics[0]));
        Assert.NotEqual("none", Convert.ToString(actionMetrics[1]));
        Assert.True(Convert.ToDouble(actionMetrics[2]) <= 48, $"Expected compact overview hamburger width. Width={actionMetrics[2]}.");
        Assert.True(Convert.ToDouble(actionMetrics[3]) <= 12, $"Expected title and menu trigger to remain visually aligned. Y delta={actionMetrics[3]}.");
        Assert.True(Convert.ToDouble(actionMetrics[4]) <= 8, $"Expected minimal gap between overview title and chevron toggle. Gap={actionMetrics[4]}.");

        var menuTrigger = page.Locator("[data-overview-actions-menu] > summary").First;
        await menuTrigger.ClickAsync();

        var openMenu = page.Locator("[data-overview-actions-menu][open] .aislepilot-overview-actions-menu").First;
        await openMenu.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var menuMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const menu = document.querySelector("[data-overview-actions-menu][open] .aislepilot-overview-actions-menu");
                if (!(menu instanceof HTMLElement)) {
                    return [-1, -1, -1, "missing", "missing", 0, 0, -1, -1, 0];
                }

                const rect = menu.getBoundingClientRect();
                const refresh = menu.querySelector(".aislepilot-overview-regenerate-btn");
                const settings = menu.querySelector(".aislepilot-edit-setup-btn");
                const refreshText = refresh instanceof HTMLElement ? (refresh.textContent || "").trim() : "";
                const settingsText = settings instanceof HTMLElement ? (settings.textContent || "").trim() : "";
                const mobileContext = document.querySelector(".aislepilot-mobile-context");

                const isButtonCenterVisible = button => {
                    if (!(button instanceof HTMLElement)) {
                        return false;
                    }

                    const buttonRect = button.getBoundingClientRect();
                    if (buttonRect.width < 2 || buttonRect.height < 2) {
                        return false;
                    }

                    const x = Math.min(window.innerWidth - 1, Math.max(0, buttonRect.left + (buttonRect.width / 2)));
                    const y = Math.min(window.innerHeight - 1, Math.max(0, buttonRect.top + (buttonRect.height / 2)));
                    const topElement = document.elementFromPoint(x, y);
                    return topElement instanceof Element &&
                        (topElement === button || button.contains(topElement) || menu.contains(topElement));
                };

                const menuZIndex = Number.parseInt(window.getComputedStyle(menu).zIndex || "0", 10);
                const mobileContextZIndex = mobileContext instanceof HTMLElement
                    ? Number.parseInt(window.getComputedStyle(mobileContext).zIndex || "0", 10)
                    : -1;
                const overviewSection = document.querySelector("#aislepilot-overview");
                return [
                    Math.max(0, 8 - rect.left),
                    Math.max(0, rect.right - (window.innerWidth - 8)),
                    menu.querySelectorAll(".aislepilot-overview-regenerate-btn, .aislepilot-edit-setup-btn").length,
                    refreshText,
                    settingsText,
                    isButtonCenterVisible(refresh) ? 1 : 0,
                    isButtonCenterVisible(settings) ? 1 : 0,
                    Number.isFinite(menuZIndex) ? menuZIndex : -1,
                    Number.isFinite(mobileContextZIndex) ? mobileContextZIndex : -1,
                    overviewSection instanceof HTMLElement && overviewSection.classList.contains("is-actions-menu-open") ? 1 : 0
                ];
            }
            """);

        Assert.Equal(10, menuMetrics.Length);
        Assert.True(Convert.ToDouble(menuMetrics[0]) <= 1.5, $"Expected overview menu to stay inside viewport on left edge. Overflow={menuMetrics[0]}.");
        Assert.True(Convert.ToDouble(menuMetrics[1]) <= 1.5, $"Expected overview menu to stay inside viewport on right edge. Overflow={menuMetrics[1]}.");
        Assert.Equal(3, Convert.ToInt32(menuMetrics[2]));
        Assert.Contains("Refresh plan", Convert.ToString(menuMetrics[3]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("settings", Convert.ToString(menuMetrics[4]), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Convert.ToInt32(menuMetrics[5]));
        Assert.Equal(1, Convert.ToInt32(menuMetrics[6]));
        Assert.True(
            Convert.ToDouble(menuMetrics[7]) > Convert.ToDouble(menuMetrics[8]),
            $"Expected overview menu z-index to exceed sticky context z-index. Menu={menuMetrics[7]}, context={menuMetrics[8]}.");
        Assert.Equal(1, Convert.ToInt32(menuMetrics[9]));
    }

    [Fact]
    public async Task NarrowMobile_AislePilotOverviewCollapsed_DoesNotCreateHorizontalOverflow()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateNarrowMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var overflowMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const doc = document.documentElement;
                const body = document.body;
                const scrollWidth = Math.max(doc?.scrollWidth ?? 0, body?.scrollWidth ?? 0);
                const overflowX = Math.max(0, scrollWidth - window.innerWidth);

                const overviewHead = document.querySelector(".aislepilot-overview-head");
                const overviewActions = document.querySelector(".aislepilot-overview-actions");
                const headRect = overviewHead instanceof HTMLElement ? overviewHead.getBoundingClientRect() : null;
                const actionsRect = overviewActions instanceof HTMLElement ? overviewActions.getBoundingClientRect() : null;
                const headOverflow = headRect ? Math.max(0, headRect.right - window.innerWidth) : -1;
                const actionsOverflow = actionsRect ? Math.max(0, actionsRect.right - window.innerWidth) : -1;
                return [overflowX, headOverflow, actionsOverflow];
            }
            """);

        Assert.Equal(3, overflowMetrics.Length);
        Assert.True(overflowMetrics[0] <= 1.5, $"Expected collapsed overview not to cause page horizontal overflow. Overflow={overflowMetrics[0]:F1}px.");
        Assert.True(overflowMetrics[1] <= 1.5, $"Expected overview header to stay inside viewport. Overflow={overflowMetrics[1]:F1}px.");
        Assert.True(overflowMetrics[2] <= 1.5, $"Expected overview action row to stay inside viewport. Overflow={overflowMetrics[2]:F1}px.");
    }

    [Fact]
    public async Task Mobile_AislePilotOverviewCollapsed_DoesNotRenderDuplicateGlanceMetrics()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var overviewMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const glance = document.querySelector(".aislepilot-overview-glance");
                const overviewContent = document.querySelector("[data-overview-content]");
                if (!(overviewContent instanceof HTMLElement)) {
                    return [0, "missing"];
                }

                const collapsed = overviewContent.hasAttribute("hidden") ? 1 : 0;
                const glanceExists = glance instanceof HTMLElement ? 1 : 0;
                return [collapsed, glanceExists];
            }
            """);

        Assert.Equal(2, overviewMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(overviewMetrics[0]));
        Assert.Equal(0, Convert.ToInt32(overviewMetrics[1]));
    }

    [Fact]
    public async Task Mobile_AislePilotDayCard_ShowsSingleVisibleSummarySurface()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var summaryMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const card = document.querySelector("[data-day-meal-card]:has([data-day-card-summary])");
                if (!(card instanceof HTMLElement)) {
                    return [0, 0, "missing"];
                }

                const headerSummary = card.querySelector("[data-day-card-summary]");
                const inlineSummary = card.querySelector("[data-day-meal-summary]");
                if (!(headerSummary instanceof HTMLElement) || !(inlineSummary instanceof HTMLElement)) {
                    return [0, 0, "missing"];
                }

                const headerStyle = window.getComputedStyle(headerSummary);
                const inlineStyle = window.getComputedStyle(inlineSummary);
                const headerVisible = headerStyle.display !== "none" && headerSummary.getBoundingClientRect().height > 0 ? 1 : 0;
                const inlineVisible = inlineStyle.display !== "none" && inlineSummary.getBoundingClientRect().height > 0 ? 1 : 0;
                const headerText = (headerSummary.textContent || "").replace(/\s+/g, " ").trim();
                return [headerVisible, inlineVisible, headerText];
            }
            """);

        Assert.Equal(3, summaryMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(summaryMetrics[0]));
        Assert.Equal(0, Convert.ToInt32(summaryMetrics[1]));
        var summaryText = Convert.ToString(summaryMetrics[2]) ?? string.Empty;
        Assert.True(
            summaryText.Contains("mins", StringComparison.OrdinalIgnoreCase) ||
            summaryText.Contains("removed", StringComparison.OrdinalIgnoreCase) ||
            summaryText.Contains("£", StringComparison.OrdinalIgnoreCase),
            $"Expected visible header summary to include meal meta text. Text='{summaryText}'.");
    }

    [Fact]
    public async Task Mobile_AislePilotDayCardExpander_CollapsesAndExpandsMealBody()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var expanderMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const expanders = Array.from(document.querySelectorAll("[data-day-card-expander]"));
                if (expanders.length < 2) {
                    return [0, 0];
                }

                const openCount = expanders.filter(expander => expander instanceof HTMLDetailsElement && expander.open).length;
                const closedCount = expanders.filter(expander => expander instanceof HTMLDetailsElement && !expander.open).length;
                return [openCount, closedCount];
            }
            """);
        Assert.Equal(2, expanderMetrics.Length);
        Assert.True(Convert.ToInt32(expanderMetrics[0]) >= 1, $"Expected at least one day card expander open by default. Open={expanderMetrics[0]}.");
        Assert.True(Convert.ToInt32(expanderMetrics[1]) >= 1, $"Expected at least one day card expander collapsed by default. Closed={expanderMetrics[1]}.");

        var secondExpanderSummary = page.Locator("[data-day-card-expander] > summary").Nth(1);
        await secondExpanderSummary.ScrollIntoViewIfNeededAsync();
        await secondExpanderSummary.ClickAsync();

        var toggleMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const targetExpander = document.querySelectorAll("[data-day-card-expander]")[1];
                if (!(targetExpander instanceof HTMLDetailsElement)) {
                    return [0, 0, "missing", 0, Number.POSITIVE_INFINITY, 1, 1, 0, 1, Number.POSITIVE_INFINITY];
                }

                const summary = targetExpander.querySelector(":scope > summary");
                const body = targetExpander.querySelector(":scope > .aislepilot-day-card-expander-body");
                if (!(summary instanceof HTMLElement) || !(body instanceof HTMLElement)) {
                    return [0, 0, "missing", 0, Number.POSITIVE_INFINITY, 1, 1, 0, 1, Number.POSITIVE_INFINITY];
                }

                const thumb = summary.querySelector("[data-day-card-expander-image]");
                const day = summary.querySelector(".aislepilot-day-card-expander-day");
                const meta = summary.querySelector(".aislepilot-day-card-expander-meta");
                const thumbVisible =
                    thumb instanceof HTMLImageElement &&
                    thumb.getBoundingClientRect().width >= 24 &&
                    thumb.getBoundingClientRect().height >= 24
                        ? 1
                        : 0;
                const dayMetaTopDelta =
                    day instanceof HTMLElement && meta instanceof HTMLElement
                        ? Math.abs(day.getBoundingClientRect().top - meta.getBoundingClientRect().top)
                        : Number.POSITIVE_INFINITY;

                const bodyVisible = window.getComputedStyle(body).display !== "none" && body.getBoundingClientRect().height > 12 ? 1 : 0;
                const headMain = body.querySelector(".aislepilot-day-card-head-main");
                const activePanelTitle = body.querySelector(".aislepilot-day-meal-panel[aria-hidden='false'] > h3");
                const duplicateHeadVisible =
                    headMain instanceof HTMLElement &&
                    window.getComputedStyle(headMain).display !== "none" &&
                    headMain.getBoundingClientRect().height > 1
                        ? 1
                        : 0;
                const duplicateTitleVisible =
                    activePanelTitle instanceof HTMLElement &&
                    window.getComputedStyle(activePanelTitle).display !== "none" &&
                    activePanelTitle.getBoundingClientRect().height > 1
                        ? 1
                        : 0;
                const tabs = body.querySelector(".aislepilot-day-meal-tabs");
                const firstPanel = body.querySelector(".aislepilot-day-meal-slider");
                const bodyTop = body.getBoundingClientRect().top;
                const firstContentTop =
                    tabs instanceof HTMLElement
                        ? tabs.getBoundingClientRect().top
                        : (firstPanel instanceof HTMLElement ? firstPanel.getBoundingClientRect().top : Number.POSITIVE_INFINITY);
                const topGap = Math.max(0, firstContentTop - bodyTop);
                const activePanel = body.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']");
                const imageShell = activePanel instanceof HTMLElement
                    ? activePanel.querySelector(".aislepilot-meal-image-shell")
                    : null;
                const menuSummary = activePanel instanceof HTMLElement
                    ? activePanel.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions] > summary")
                    : null;
                const menuAttachedToMealPanel = menuSummary instanceof HTMLElement ? 1 : 0;
                const strayMenuInDayHead = body.querySelector(".aislepilot-day-card-head [data-day-card-header-actions]") instanceof HTMLElement ? 1 : 0;
                const menuInImageBand =
                    imageShell instanceof HTMLElement && menuSummary instanceof HTMLElement
                        ? (
                            menuSummary.getBoundingClientRect().top >= imageShell.getBoundingClientRect().top - 8 &&
                            menuSummary.getBoundingClientRect().bottom <= imageShell.getBoundingClientRect().bottom + 8
                                ? 1
                                : 0
                        )
                        : 0;
                const imageRightInset =
                    imageShell instanceof HTMLElement && menuSummary instanceof HTMLElement
                        ? Math.abs(imageShell.getBoundingClientRect().right - menuSummary.getBoundingClientRect().right)
                        : Number.POSITIVE_INFINITY;
                return [targetExpander.open ? 1 : 0, bodyVisible, summary.getAttribute("aria-expanded") || "", thumbVisible, dayMetaTopDelta, duplicateHeadVisible, duplicateTitleVisible, menuAttachedToMealPanel, menuInImageBand, imageRightInset, strayMenuInDayHead, topGap];
            }
            """);

        Assert.Equal(12, toggleMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(toggleMetrics[0]));
        Assert.Equal(1, Convert.ToInt32(toggleMetrics[1]));
        Assert.Equal("true", Convert.ToString(toggleMetrics[2]));
        Assert.Equal(1, Convert.ToInt32(toggleMetrics[3]));
        Assert.True(Convert.ToDouble(toggleMetrics[4]) <= 8, $"Expected day and summary meta to align on top row. Delta={toggleMetrics[4]}.");
        Assert.Equal(0, Convert.ToInt32(toggleMetrics[5]));
        Assert.Equal(0, Convert.ToInt32(toggleMetrics[6]));
        Assert.Equal(1, Convert.ToInt32(toggleMetrics[7]));
        Assert.Equal(1, Convert.ToInt32(toggleMetrics[8]));
        Assert.True(Convert.ToDouble(toggleMetrics[9]) <= 24, $"Expected meal actions trigger to align near the image right edge. Inset={toggleMetrics[9]}.");
        Assert.Equal(0, Convert.ToInt32(toggleMetrics[10]));
        Assert.True(Convert.ToDouble(toggleMetrics[11]) <= 12, $"Expected expanded card body content to start near the top without a dedicated action row gap. Gap={toggleMetrics[11]}.");
    }

    [Fact]
    public async Task Mobile_AislePilotShopAndExportPanels_UseConsistentCardSurfaces()
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

        var shopJump = stickyContext.Locator("button[data-window-tab='aislepilot-shop']").First;
        await shopJump.ClickAsync();
        await page.Locator("#aislepilot-shop[aria-hidden='false']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var shopMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const shopCard = document.querySelector("#aislepilot-shop .aislepilot-shop-card");
                if (!(shopCard instanceof HTMLElement)) {
                    return [0, -1];
                }

                const style = window.getComputedStyle(shopCard);
                return [1, Number.parseFloat(style.borderRadius || "0")];
            }
            """);
        Assert.Equal(2, shopMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(shopMetrics[0]));
        Assert.True(Convert.ToDouble(shopMetrics[1]) >= 10, $"Expected shop cards to use rounded panel surface. Radius={shopMetrics[1]}.");

        var exportJump = stickyContext.Locator("button[data-window-tab='aislepilot-export']").First;
        await exportJump.ClickAsync();
        await page.Locator("#aislepilot-export[aria-hidden='false']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var exportMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const exportAction = document.querySelector("#aislepilot-export .aislepilot-export-action");
                const exportButton = document.querySelector("#aislepilot-export .aislepilot-export-action .aislepilot-export-btn");
                if (!(exportAction instanceof HTMLElement) || !(exportButton instanceof HTMLElement)) {
                    return [0, -1, Number.POSITIVE_INFINITY];
                }

                const actionStyle = window.getComputedStyle(exportAction);
                const actionRect = exportAction.getBoundingClientRect();
                const buttonRect = exportButton.getBoundingClientRect();
                const widthRatio = actionRect.width > 1 ? buttonRect.width / actionRect.width : Number.POSITIVE_INFINITY;
                return [1, Number.parseFloat(actionStyle.borderRadius || "0"), widthRatio];
            }
            """);

        Assert.Equal(3, exportMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(exportMetrics[0]));
        Assert.True(Convert.ToDouble(exportMetrics[1]) >= 10, $"Expected export actions to use rounded panel surface. Radius={exportMetrics[1]}.");
        Assert.True(Convert.ToDouble(exportMetrics[2]) >= 0.94, $"Expected export button to fill action surface width. Ratio={exportMetrics[2]:F2}.");
    }
}
