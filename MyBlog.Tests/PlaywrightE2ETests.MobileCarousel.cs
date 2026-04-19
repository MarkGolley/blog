using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests : IAsyncLifetime
{
    [Fact]
    public async Task Mobile_AislePilotDayCarousel_PaginationSwipeDoesNotSwitchToShoppingPanel()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var pagination = page.Locator("[data-day-carousel-pagination]").First;
        await pagination.WaitForAsync(new LocatorWaitForOptions
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

        var carouselStatusBeforeSwipe = (await page.Locator("[data-day-carousel-status]").First.InnerTextAsync()).Trim();

        var swipeState = await pagination.EvaluateAsync<object[]>(
            """
            paginationRoot => {
                if (!(paginationRoot instanceof HTMLElement)) {
                    return [0, ""];
                }

                const overflowWidth = paginationRoot.scrollWidth - paginationRoot.clientWidth;
                const rect = paginationRoot.getBoundingClientRect();
                if (overflowWidth <= 8 || rect.width < 100 || rect.height < 20) {
                    return [overflowWidth, window.getComputedStyle(paginationRoot).touchAction || ""];
                }

                const edgeInset = Math.min(36, rect.width * 0.18);
                const startX = rect.right - edgeInset;
                const endX = rect.left + edgeInset;
                const y = rect.top + (rect.height / 2);

                const createTouchEvent = (type, x, touchY) => {
                    const event = new Event(type, { bubbles: true, cancelable: true });
                    Object.defineProperty(event, "changedTouches", {
                        configurable: true,
                        value: [{ clientX: x, clientY: touchY }]
                    });
                    return event;
                };

                paginationRoot.dispatchEvent(createTouchEvent("touchstart", startX, y));
                paginationRoot.dispatchEvent(createTouchEvent("touchmove", ((startX + endX) / 2), y));
                paginationRoot.dispatchEvent(createTouchEvent("touchend", endX, y));

                return [overflowWidth, window.getComputedStyle(paginationRoot).touchAction || ""];
            }
            """);

        Assert.Equal(2, swipeState.Length);
        Assert.True(Convert.ToDouble(swipeState[0]) > 8d, $"Expected the day pill strip to overflow on mobile. Overflow={swipeState[0]}.");
        Assert.Contains("pan-x", Convert.ToString(swipeState[1]) ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        await page.WaitForTimeoutAsync(150);

        var activePanelAfterSwipe = await page.EvaluateAsync<string>(
            """
            () => {
                const activePanel = document.querySelector(".aislepilot-window-panel[aria-hidden='false']");
                return activePanel instanceof HTMLElement ? (activePanel.id || "") : "";
            }
            """);
        Assert.Equal("aislepilot-meals", activePanelAfterSwipe);

        var carouselStatusAfterSwipe = (await page.Locator("[data-day-carousel-status]").First.InnerTextAsync()).Trim();
        Assert.Equal(carouselStatusBeforeSwipe, carouselStatusAfterSwipe);
    }

    [Fact]
    public async Task Mobile_AislePilotDayCarousel_DayPillJumpsToRequestedDayAndKeepsSlideCentered()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var fridayPill = page.Locator("[data-day-carousel-dot][data-day-carousel-target='4']").First;
        await fridayPill.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const viewport = document.querySelector("[data-day-carousel-viewport]");
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeSlide = slides[activeIndex];
                const status = document.querySelector("[data-day-carousel-status]");
                if (!(viewport instanceof HTMLElement) || !(activeSlide instanceof HTMLElement) || !(status instanceof HTMLElement)) {
                    return false;
                }

                const viewportRect = viewport.getBoundingClientRect();
                const activeRect = activeSlide.getBoundingClientRect();
                const centerDelta = Math.abs((activeRect.left + (activeRect.width / 2)) - (viewportRect.left + (viewportRect.width / 2)));
                const activeDotLabel = document.querySelector("[data-day-carousel-dot][aria-selected='true'] .aislepilot-day-carousel-dot-label");
                const labelText = activeDotLabel instanceof HTMLElement ? (activeDotLabel.textContent || "").trim() : "";
                return activeIndex === 4 &&
                    /Friday/i.test(status.textContent || "") &&
                    centerDelta <= 10 &&
                    /^Fri$/i.test(labelText);
            }
            """);

        var mondayPill = page.Locator("[data-day-carousel-dot][data-day-carousel-target='0']").First;
        await mondayPill.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const viewport = document.querySelector("[data-day-carousel-viewport]");
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeSlide = slides[activeIndex];
                const status = document.querySelector("[data-day-carousel-status]");
                if (!(viewport instanceof HTMLElement) || !(activeSlide instanceof HTMLElement) || !(status instanceof HTMLElement)) {
                    return false;
                }

                const viewportRect = viewport.getBoundingClientRect();
                const activeRect = activeSlide.getBoundingClientRect();
                const centerDelta = Math.abs((activeRect.left + (activeRect.width / 2)) - (viewportRect.left + (viewportRect.width / 2)));
                const leadingGhost = document.querySelector("[data-day-carousel-ghost-side='leading']");
                const ghostRect = leadingGhost instanceof HTMLElement ? leadingGhost.getBoundingClientRect() : null;
                const ghostVisibleWidth = ghostRect
                    ? Math.max(0, Math.min(ghostRect.right, viewportRect.right) - Math.max(ghostRect.left, viewportRect.left))
                    : 0;
                return activeIndex === 0 &&
                    /Monday/i.test(status.textContent || "") &&
                    centerDelta <= 10 &&
                    ghostVisibleWidth <= 2;
            }
            """);
    }

    [Fact]
    public async Task Mobile_AislePilotDayCarousel_ArrowsWrapAcrossWeekBoundary()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var sundayPill = page.Locator("[data-day-carousel-dot][data-day-carousel-target='6']").First;
        await sundayPill.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const status = document.querySelector("[data-day-carousel-status]");
                const nextButton = document.querySelector("[data-day-carousel-next]");
                return status instanceof HTMLElement &&
                    /Sunday/i.test(status.textContent || "") &&
                    nextButton instanceof HTMLButtonElement &&
                    !nextButton.disabled;
            }
            """);

        var nextButton = page.Locator("[data-day-carousel-next]").First;
        await nextButton.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const viewport = document.querySelector("[data-day-carousel-viewport]");
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeSlide = slides[activeIndex];
                const status = document.querySelector("[data-day-carousel-status]");
                const previousButton = document.querySelector("[data-day-carousel-prev]");
                if (!(viewport instanceof HTMLElement) || !(activeSlide instanceof HTMLElement) || !(status instanceof HTMLElement) || !(previousButton instanceof HTMLButtonElement)) {
                    return false;
                }

                const viewportRect = viewport.getBoundingClientRect();
                const activeRect = activeSlide.getBoundingClientRect();
                const centerDelta = Math.abs((activeRect.left + (activeRect.width / 2)) - (viewportRect.left + (viewportRect.width / 2)));
                const leadingGhost = document.querySelector("[data-day-carousel-ghost-side='leading']");
                const ghostRect = leadingGhost instanceof HTMLElement ? leadingGhost.getBoundingClientRect() : null;
                const ghostVisibleWidth = ghostRect
                    ? Math.max(0, Math.min(ghostRect.right, viewportRect.right) - Math.max(ghostRect.left, viewportRect.left))
                    : 0;
                return activeIndex === 0 &&
                    /Monday/i.test(status.textContent || "") &&
                    centerDelta <= 10 &&
                    !previousButton.disabled &&
                    ghostVisibleWidth <= 2;
            }
            """);
    }

    [Fact]
    public async Task Mobile_AislePilotDayCarousel_UsesCompactHintAndReadableDayPills()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        await page.WaitForFunctionAsync(
            """
            () => {
                const parseAlpha = value => {
                    if (typeof value !== "string") {
                        return 0;
                    }

                    const match = value.match(/rgba?\(([^)]+)\)/i);
                    if (!match) {
                        return 1;
                    }

                    const parts = match[1].split(",").map(part => Number.parseFloat(part.trim()));
                    return parts.length >= 4 && Number.isFinite(parts[3]) ? parts[3] : 1;
                };

                const activeSlide = document.querySelector("[data-day-card-slide][data-day-carousel-position='active']");
                const hint = activeSlide instanceof HTMLElement ? activeSlide.querySelector(".aislepilot-meal-image-hint") : null;
                const primaryHint = hint instanceof HTMLElement ? hint.querySelector(".aislepilot-meal-image-hint-primary") : null;
                const chips = hint instanceof HTMLElement ? Array.from(hint.querySelectorAll(".aislepilot-meal-image-hint-chip")) : [];
                const chipsShell = hint instanceof HTMLElement ? hint.querySelector(".aislepilot-meal-image-hint-chips") : null;
                const activeDot = document.querySelector("[data-day-carousel-dot][aria-selected='true']");
                const inactiveDot = document.querySelector("[data-day-carousel-dot][aria-selected='false']");
                const dayTabs = activeSlide instanceof HTMLElement ? activeSlide.querySelector(".aislepilot-day-meal-tabs") : null;
                if (!(activeSlide instanceof HTMLElement)
                    || !(hint instanceof HTMLElement)
                    || !(primaryHint instanceof HTMLElement)
                    || !(activeDot instanceof HTMLElement)
                    || !(inactiveDot instanceof HTMLElement)
                    || !(dayTabs instanceof HTMLElement)) {
                    return false;
                }

                const hintRect = hint.getBoundingClientRect();
                const activeDotOpacity = Number.parseFloat(getComputedStyle(activeDot).opacity || "0");
                const inactiveDotOpacity = Number.parseFloat(getComputedStyle(inactiveDot).opacity || "0");
                const hintAlpha = parseAlpha(getComputedStyle(hint).backgroundColor || "");
                const tabsAlpha = parseAlpha(getComputedStyle(dayTabs).backgroundColor || "");
                const primaryText = (primaryHint.textContent || "").trim();
                const primaryGap = Number.parseFloat(getComputedStyle(primaryHint).columnGap || getComputedStyle(primaryHint).gap || "0");
                const labelText = activeDot.textContent || "";
                const chipsShellDisplay = chipsShell instanceof HTMLElement ? getComputedStyle(chipsShell).display : "missing";

                return hintRect.width <= 124
                    && hintRect.height <= 34
                    && chips.length === 3
                    && chipsShellDisplay === "none"
                    && activeDotOpacity >= 0.98
                    && inactiveDotOpacity >= 0.9
                    && hintAlpha >= 0.3 && hintAlpha <= 0.54
                    && tabsAlpha <= 0.12
                    && primaryGap <= 4
                    && /[A-Za-z]{3,}/.test(labelText)
                    && /Recipe/i.test(primaryText);
            }
            """);
    }
}
