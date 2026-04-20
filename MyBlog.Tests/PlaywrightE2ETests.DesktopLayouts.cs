using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests : IAsyncLifetime
{

    [Fact]
    [Trait("Category", "E2ESmoke")]
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

    [Fact]
    public async Task Desktop_AislePilotDayCarousel_DayPillAndArrowAdvanceToExpectedSlides()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var saturdayPill = page.Locator("[data-day-carousel-dot][data-day-carousel-target='5']").First;
        await saturdayPill.ClickAsync();

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
                const previousSlide = slides[activeIndex - 1];
                const nextSlide = slides[activeIndex + 1];
                const activeTitle = activeSlide.querySelector("h3");
                const previousTitle = previousSlide instanceof HTMLElement ? previousSlide.querySelector("h3") : null;
                const nextTitle = nextSlide instanceof HTMLElement ? nextSlide.querySelector("h3") : null;
                const activeTitleOpacity = activeTitle instanceof HTMLElement ? Number.parseFloat(getComputedStyle(activeTitle).opacity || "1") : 0;
                const previousTitleOpacity = previousTitle instanceof HTMLElement ? Number.parseFloat(getComputedStyle(previousTitle).opacity || "1") : 0;
                const nextTitleOpacity = nextTitle instanceof HTMLElement ? Number.parseFloat(getComputedStyle(nextTitle).opacity || "1") : 0;
                return activeIndex === 5 &&
                    /Saturday/i.test(status.textContent || "") &&
                    centerDelta <= 12 &&
                    activeTitleOpacity >= 0.95 &&
                    previousTitleOpacity <= 0.4 &&
                    nextTitleOpacity <= 0.4;
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
                const nextButton = document.querySelector("[data-day-carousel-next]");
                if (!(viewport instanceof HTMLElement) || !(activeSlide instanceof HTMLElement) || !(status instanceof HTMLElement) || !(nextButton instanceof HTMLButtonElement)) {
                    return false;
                }

                const viewportRect = viewport.getBoundingClientRect();
                const activeRect = activeSlide.getBoundingClientRect();
                const centerDelta = Math.abs((activeRect.left + (activeRect.width / 2)) - (viewportRect.left + (viewportRect.width / 2)));
                const trailingGhost = document.querySelector("[data-day-carousel-ghost-side='trailing']");
                const ghostRect = trailingGhost instanceof HTMLElement ? trailingGhost.getBoundingClientRect() : null;
                const ghostVisibleWidth = ghostRect
                    ? Math.max(0, Math.min(ghostRect.right, viewportRect.right) - Math.max(ghostRect.left, viewportRect.left))
                    : 0;
                const ghostPlaceholder = trailingGhost instanceof HTMLElement
                    ? trailingGhost.querySelector(".aislepilot-day-card-ghost-panel")
                    : null;
                const ghostTitle = trailingGhost instanceof HTMLElement ? trailingGhost.querySelector("h3") : null;
                return activeIndex === 6 &&
                    /Sunday/i.test(status.textContent || "") &&
                    centerDelta <= 12 &&
                    !nextButton.disabled &&
                    ghostVisibleWidth >= 28 &&
                    ghostPlaceholder instanceof HTMLElement &&
                    !(ghostTitle instanceof HTMLElement);
            }
            """);

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
                const ghostPlaceholder = leadingGhost instanceof HTMLElement
                    ? leadingGhost.querySelector(".aislepilot-day-card-ghost-panel")
                    : null;
                const ghostTitle = leadingGhost instanceof HTMLElement ? leadingGhost.querySelector("h3") : null;
                return activeIndex === 0 &&
                    /Monday/i.test(status.textContent || "") &&
                    centerDelta <= 12 &&
                    !previousButton.disabled &&
                    ghostVisibleWidth >= 28 &&
                    ghostPlaceholder instanceof HTMLElement &&
                    !(ghostTitle instanceof HTMLElement);
            }
            """);
    }

    [Fact]
    public async Task Desktop_AislePilotDayCarousel_DoesNotFlashIntermediateStatusDuringSmoothNavigation()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        await page.EvaluateAsync(
            """
            () => {
                const status = document.querySelector("[data-day-carousel-status]");
                if (!(status instanceof HTMLElement)) {
                    return;
                }

                window.__aislePilotStatusObserver?.disconnect?.();
                window.__aislePilotStatusHistory = [];
                const record = () => {
                    window.__aislePilotStatusHistory.push((status.textContent || "").trim());
                };

                const observer = new MutationObserver(record);
                observer.observe(status, { childList: true, characterData: true, subtree: true });
                window.__aislePilotStatusObserver = observer;
            }
            """);

        var fridayPill = page.Locator("[data-day-carousel-dot][data-day-carousel-target='4']").First;
        await page.EvaluateAsync("() => { window.__aislePilotStatusHistory = []; }");
        await fridayPill.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const status = document.querySelector("[data-day-carousel-status]");
                const activeDot = document.querySelector("[data-day-carousel-dot][aria-selected='true']");
                return status instanceof HTMLElement
                    && /Friday, 5 of 7/i.test(status.textContent || "")
                    && activeDot instanceof HTMLElement
                    && activeDot.getAttribute("data-day-carousel-target") === "4";
            }
            """);

        var fridayHistory = await page.EvaluateAsync<string[]>(
            """
            () => Array.isArray(window.__aislePilotStatusHistory)
                ? window.__aislePilotStatusHistory.filter(value => typeof value === "string" && value.trim().length > 0)
                : []
            """);

        Assert.NotEmpty(fridayHistory);
        Assert.Equal(
            ["Friday, 5 of 7"],
            fridayHistory
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());

        var nextButton = page.Locator("[data-day-carousel-next]").First;
        await page.EvaluateAsync("() => { window.__aislePilotStatusHistory = []; }");
        await nextButton.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const status = document.querySelector("[data-day-carousel-status]");
                const activeDot = document.querySelector("[data-day-carousel-dot][aria-selected='true']");
                return status instanceof HTMLElement
                    && /Saturday, 6 of 7/i.test(status.textContent || "")
                    && activeDot instanceof HTMLElement
                    && activeDot.getAttribute("data-day-carousel-target") === "5";
            }
            """);

        var saturdayHistory = await page.EvaluateAsync<string[]>(
            """
            () => Array.isArray(window.__aislePilotStatusHistory)
                ? window.__aislePilotStatusHistory.filter(value => typeof value === "string" && value.trim().length > 0)
                : []
            """);

        Assert.NotEmpty(saturdayHistory);
        Assert.Equal(
            ["Saturday, 6 of 7"],
            saturdayHistory
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    [Fact]
    public async Task Desktop_AislePilotDayCarousel_WiresInitialSlideStateOnLoad()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, message) =>
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                pageErrors.Add(message.Trim());
            }
        };

        await GoToAislePilotAndGeneratePlanAsync(page);

        var carouselState = await page.EvaluateAsync<object[]>(
            """
            () => {
                const carousel = document.querySelector("[data-day-card-carousel]");
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeSlides = slides.filter(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const positionedSlides = slides.filter(slide => slide instanceof HTMLElement && (slide.getAttribute("data-day-carousel-position") || "").length > 0);
                const activePositionSlides = slides.filter(slide => slide instanceof HTMLElement && slide.getAttribute("data-day-carousel-position") === "active");
                const status = document.querySelector("[data-day-carousel-status]");
                const coreKeys = window.AislePilotCore ? Object.keys(window.AislePilotCore).sort().join(",") : "";

                return [
                    carousel instanceof HTMLElement ? (carousel.getAttribute("data-day-card-carousel-wired") || "") : "missing",
                    slides.length,
                    activeSlides.length,
                    positionedSlides.length,
                    activePositionSlides.length,
                    status instanceof HTMLElement ? (status.textContent || "").trim() : "",
                    window.__aislePilotCoreWired === true ? "1" : "0",
                    window.__aislePilotScriptWired === true ? "1" : "0",
                    window.AislePilotCore ? "1" : "0",
                    coreKeys
                ];
            }
            """);

        Assert.Equal(10, carouselState.Length);
        var debugState =
            $"wired={carouselState[0]}; slides={carouselState[1]}; activeSlides={carouselState[2]}; " +
            $"positioned={carouselState[3]}; activePositioned={carouselState[4]}; status={carouselState[5]}; " +
            $"coreWired={carouselState[6]}; scriptWired={carouselState[7]}; corePresent={carouselState[8]}; coreKeys={carouselState[9]}";
        if (pageErrors.Count > 0)
        {
            debugState = $"{debugState}; pageErrors={string.Join(" || ", pageErrors)}";
        }

        Assert.True(string.Equals("true", Convert.ToString(carouselState[0]), StringComparison.Ordinal), debugState);
        Assert.True(Convert.ToInt32(carouselState[1]) >= 7, debugState);
        Assert.Equal(1, Convert.ToInt32(carouselState[2]));
        Assert.Equal(Convert.ToInt32(carouselState[1]), Convert.ToInt32(carouselState[3]));
        Assert.Equal(1, Convert.ToInt32(carouselState[4]));
        Assert.Contains("1 of", Convert.ToString(carouselState[5]) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1", Convert.ToString(carouselState[6]));
        Assert.Equal("1", Convert.ToString(carouselState[7]));
        Assert.Equal("1", Convert.ToString(carouselState[8]));
        Assert.Contains("wirePlanBasicsSliders", Convert.ToString(carouselState[9]) ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Desktop_AislePilotDayCarousel_UsesMutedPreviewCardsAndCompactOverlayChrome()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var thursdayPill = page.Locator("[data-day-carousel-dot][data-day-carousel-target='3']").First;
        await thursdayPill.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeSlide = slides[activeIndex];
                const activeDot = document.querySelector("[data-day-carousel-dot][aria-selected='true']");
                return activeIndex === 3
                    && activeSlide instanceof HTMLElement
                    && activeSlide.getAttribute("data-day-carousel-position") === "active"
                    && activeSlide.getAttribute("data-day-carousel-settling") !== "true"
                    && activeDot instanceof HTMLElement
                    && activeDot.getAttribute("data-day-carousel-target") === "3";
            }
            """);

        var metrics = await page.EvaluateAsync<object[]>(
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

                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeSlide = slides.find(slide => slide instanceof HTMLElement && slide.getAttribute("data-day-carousel-position") === "active");
                const activeResolvedIndex = slides.findIndex(slide => slide === activeSlide);
                const previousSlide = slides[activeResolvedIndex - 1];
                const nextSlide = slides[activeResolvedIndex + 1];
                const activeDot = document.querySelector("[data-day-carousel-dot][aria-selected='true']");
                const inactiveDot = document.querySelector("[data-day-carousel-dot][aria-selected='false']");
                const hint = activeSlide instanceof HTMLElement ? activeSlide.querySelector(".aislepilot-meal-image-hint") : null;
                const hintChips = hint instanceof HTMLElement ? hint.querySelector(".aislepilot-meal-image-hint-chips") : null;
                const primaryHint = hint instanceof HTMLElement ? hint.querySelector(".aislepilot-meal-image-hint-primary") : null;
                const actionsTrigger = activeSlide instanceof HTMLElement
                    ? activeSlide.querySelector("[data-day-card-header-actions].is-active [data-card-more-actions] > summary")
                    : null;
                const title = activeSlide instanceof HTMLElement ? activeSlide.querySelector(".aislepilot-day-meal-panel > h3") : null;
                const previousTitle = previousSlide instanceof HTMLElement ? previousSlide.querySelector(".aislepilot-day-meal-panel > h3") : null;
                const nextTitle = nextSlide instanceof HTMLElement ? nextSlide.querySelector(".aislepilot-day-meal-panel > h3") : null;
                const metaBadge = activeSlide instanceof HTMLElement
                    ? activeSlide.querySelector(".aislepilot-day-card-head-main .aislepilot-day-card-meta")
                    : null;

                if (!(activeSlide instanceof HTMLElement)
                    || !(previousSlide instanceof HTMLElement)
                    || !(nextSlide instanceof HTMLElement)
                    || !(activeDot instanceof HTMLElement)
                    || !(inactiveDot instanceof HTMLElement)
                    || !(hint instanceof HTMLElement)
                    || !(actionsTrigger instanceof HTMLElement)
                    || !(primaryHint instanceof HTMLElement)
                    || !(title instanceof HTMLElement)
                    || !(previousTitle instanceof HTMLElement)
                    || !(nextTitle instanceof HTMLElement)) {
                    return [];
                }

                const activeRect = activeSlide.getBoundingClientRect();
                const previousRect = previousSlide.getBoundingClientRect();
                const nextRect = nextSlide.getBoundingClientRect();
                const triggerRect = actionsTrigger.getBoundingClientRect();
                const hintRect = hint.getBoundingClientRect();

                return [
                    activeResolvedIndex,
                    activeRect.width,
                    previousRect.width,
                    nextRect.width,
                    Number.parseFloat(getComputedStyle(activeSlide).opacity || "0"),
                    Number.parseFloat(getComputedStyle(previousSlide).opacity || "0"),
                    Number.parseFloat(getComputedStyle(nextSlide).opacity || "0"),
                    Number.parseFloat(getComputedStyle(previousTitle).opacity || "0"),
                    Number.parseFloat(getComputedStyle(nextTitle).opacity || "0"),
                    Number.parseFloat(getComputedStyle(activeDot).opacity || "0"),
                    Number.parseFloat(getComputedStyle(inactiveDot).opacity || "0"),
                    parseAlpha(getComputedStyle(hint).backgroundColor || ""),
                    hintRect.width,
                    hintChips instanceof HTMLElement ? getComputedStyle(hintChips).display : "missing",
                    (primaryHint.textContent || "").trim(),
                    parseAlpha(getComputedStyle(actionsTrigger).backgroundColor || ""),
                    triggerRect.width,
                    triggerRect.height,
                    Number.parseFloat(getComputedStyle(title).fontSize || "0"),
                    metaBadge instanceof HTMLElement
                        ? Number.parseFloat(getComputedStyle(metaBadge).borderTopLeftRadius || "0")
                        : 0
                ];
            }
            """);

        Assert.Equal(20, metrics.Length);
        var debugState =
            $"activeIndex={metrics[0]}; widths={metrics[1]}/{metrics[2]}/{metrics[3]}; " +
            $"cardOpacity={metrics[4]}/{metrics[5]}/{metrics[6]}; titleOpacity={metrics[7]}/{metrics[8]}; " +
            $"dotOpacity={metrics[9]}/{metrics[10]}; hintAlpha={metrics[11]}; hintWidth={metrics[12]}; " +
            $"hintChips={metrics[13]}; hintText={metrics[14]}; triggerAlpha={metrics[15]}; " +
            $"triggerSize={metrics[16]}x{metrics[17]}; titleSize={metrics[18]}; metaRadius={metrics[19]}";

        Assert.Equal(3, Convert.ToInt32(metrics[0]));
        Assert.True(Convert.ToDouble(metrics[1]) > Convert.ToDouble(metrics[2]) + 14, debugState);
        Assert.True(Convert.ToDouble(metrics[1]) > Convert.ToDouble(metrics[3]) + 14, debugState);
        Assert.True(Convert.ToDouble(metrics[4]) >= 0.98, debugState);
        Assert.InRange(Convert.ToDouble(metrics[5]), 0.38, 0.52);
        Assert.InRange(Convert.ToDouble(metrics[6]), 0.38, 0.52);
        Assert.InRange(Convert.ToDouble(metrics[7]), 0.08, 0.28);
        Assert.InRange(Convert.ToDouble(metrics[8]), 0.08, 0.28);
        Assert.True(Convert.ToDouble(metrics[9]) >= 0.98, debugState);
        Assert.True(Convert.ToDouble(metrics[10]) >= 0.9, debugState);
        Assert.InRange(Convert.ToDouble(metrics[11]), 0.3, 0.58);
        Assert.True(Convert.ToDouble(metrics[12]) <= 136, debugState);
        Assert.Equal("none", Convert.ToString(metrics[13]));
        Assert.Contains("Recipe", Convert.ToString(metrics[14]) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(Convert.ToDouble(metrics[15]), 0.28, 0.56);
        Assert.True(Convert.ToDouble(metrics[16]) <= 34, debugState);
        Assert.True(Convert.ToDouble(metrics[17]) <= 34, debugState);
        Assert.InRange(Convert.ToDouble(metrics[18]), 17, 19.5);
        Assert.True(Convert.ToDouble(metrics[19]) >= 20, debugState);
    }

    [Fact]
    public async Task Desktop_AislePilotDayCarousel_ExpandedMealCardDoesNotStretchPreviewSlides()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var thursdayPill = page.Locator("[data-day-carousel-dot][data-day-carousel-target='3']").First;
        await thursdayPill.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                return activeIndex === 3;
            }
            """);

        var imageSummary = page.Locator("[data-day-card-slide][aria-hidden='false'] .aislepilot-meal-details-image-toggle > .aislepilot-meal-image-summary").First;
        await imageSummary.ScrollIntoViewIfNeededAsync();
        var previewTopsBeforeExpand = await page.EvaluateAsync<double[]>(
            """
            () => {
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeSlide = slides[activeIndex];
                const previousSlide = slides[activeIndex - 1];
                const nextSlide = slides[activeIndex + 1];
                const viewport = activeSlide instanceof HTMLElement
                    ? activeSlide.closest("[data-day-carousel-viewport]")
                    : null;
                if (!(previousSlide instanceof HTMLElement)
                    || !(nextSlide instanceof HTMLElement)
                    || !(viewport instanceof HTMLElement)) {
                    return [];
                }

                const viewportTop = viewport.getBoundingClientRect().top;
                return [
                    previousSlide.getBoundingClientRect().top - viewportTop,
                    nextSlide.getBoundingClientRect().top - viewportTop
                ];
            }
            """);
        Assert.Equal(2, previewTopsBeforeExpand.Length);

        var pagePositionBeforeExpand = await page.EvaluateAsync<double[]>(
            """
            () => {
                const viewport = document.querySelector("[data-day-carousel-viewport]");
                if (!(viewport instanceof HTMLElement)) {
                    return [];
                }

                return [
                    viewport.getBoundingClientRect().top,
                    window.scrollY
                ];
            }
            """);
        Assert.Equal(2, pagePositionBeforeExpand.Length);

        var imageSummaryBox = await imageSummary.BoundingBoxAsync();
        Assert.NotNull(imageSummaryBox);
        await page.Mouse.ClickAsync(
            imageSummaryBox!.X + (imageSummaryBox.Width / 2),
            imageSummaryBox.Y + (imageSummaryBox.Height / 2));

        var driftMetrics = await page.EvaluateAsync<double[]>(
            """
            async ({ baselinePreviousTop, baselineNextTop, baselineViewportTop, baselineScrollY }) => {
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeSlide = slides[activeIndex];
                const previousSlide = slides[activeIndex - 1];
                const nextSlide = slides[activeIndex + 1];
                const viewport = activeSlide instanceof HTMLElement
                    ? activeSlide.closest("[data-day-carousel-viewport]")
                    : null;
                if (!(previousSlide instanceof HTMLElement)
                    || !(nextSlide instanceof HTMLElement)
                    || !(viewport instanceof HTMLElement)) {
                    return [];
                }

                let maxPreviousDrift = 0;
                let maxNextDrift = 0;
                let maxViewportDrift = 0;
                let maxScrollDrift = 0;
                const sample = () => {
                    const viewportTop = viewport.getBoundingClientRect().top;
                    maxPreviousDrift = Math.max(
                        maxPreviousDrift,
                        Math.abs((previousSlide.getBoundingClientRect().top - viewportTop) - baselinePreviousTop));
                    maxNextDrift = Math.max(
                        maxNextDrift,
                        Math.abs((nextSlide.getBoundingClientRect().top - viewportTop) - baselineNextTop));
                    maxViewportDrift = Math.max(
                        maxViewportDrift,
                        Math.abs(viewportTop - baselineViewportTop));
                    maxScrollDrift = Math.max(
                        maxScrollDrift,
                        Math.abs(window.scrollY - baselineScrollY));
                };

                const start = performance.now();
                sample();
                while (performance.now() - start < 360) {
                    await new Promise(resolve => window.setTimeout(resolve, 32));
                    sample();
                }

                return [maxPreviousDrift, maxNextDrift, maxViewportDrift, maxScrollDrift];
            }
            """,
            new
            {
                baselinePreviousTop = previewTopsBeforeExpand[0],
                baselineNextTop = previewTopsBeforeExpand[1],
                baselineViewportTop = pagePositionBeforeExpand[0],
                baselineScrollY = pagePositionBeforeExpand[1]
            });

        var expandedState = await page.WaitForFunctionAsync(
            """
            () => {
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeSlide = slides[activeIndex];
                const previousSlide = slides[activeIndex - 1];
                const nextSlide = slides[activeIndex + 1];
                const viewport = activeSlide instanceof HTMLElement
                    ? activeSlide.closest("[data-day-carousel-viewport]")
                    : null;
                if (!(activeSlide instanceof HTMLElement)
                    || !(previousSlide instanceof HTMLElement)
                    || !(nextSlide instanceof HTMLElement)) {
                    return false;
                }
                if (!(viewport instanceof HTMLElement)) {
                    return false;
                }

                const detailsToggle = activeSlide.querySelector("[data-inline-details-toggle]");
                const detailsPanel = activeSlide.querySelector("[data-inline-details-panel]");
                if (!(detailsToggle instanceof HTMLDetailsElement)
                    || !(detailsPanel instanceof HTMLElement)
                    || !detailsToggle.open
                    || detailsPanel.hasAttribute("hidden")
                    || detailsPanel.getAttribute("aria-hidden") !== "false") {
                    return false;
                }

                const activeRect = activeSlide.getBoundingClientRect();
                const previousRect = previousSlide.getBoundingClientRect();
                const nextRect = nextSlide.getBoundingClientRect();
                const viewportTop = viewport.getBoundingClientRect().top;

                return [
                    activeRect.height,
                    previousRect.height,
                    nextRect.height,
                    previousRect.top - viewportTop,
                    nextRect.top - viewportTop
                ];
            }
            """);

        var expandedMetrics = await expandedState.JsonValueAsync<double[]>();
        Assert.NotNull(expandedMetrics);
        Assert.Equal(5, expandedMetrics!.Length);
        Assert.Equal(4, driftMetrics.Length);
        Assert.True(driftMetrics[0] < 2.5, $"Expected previous preview slide to stay vertically stable. Drift={driftMetrics[0]}px.");
        Assert.True(driftMetrics[1] < 2.5, $"Expected next preview slide to stay vertically stable. Drift={driftMetrics[1]}px.");
        Assert.True(driftMetrics[2] < 2.5, $"Expected carousel viewport to stay vertically stable while opening details. Drift={driftMetrics[2]}px.");
        Assert.True(driftMetrics[3] < 2.5, $"Expected page scroll to stay stable while opening details. Drift={driftMetrics[3]}px.");
        Assert.True(expandedMetrics[0] > expandedMetrics[1] + 40);
        Assert.True(expandedMetrics[0] > expandedMetrics[2] + 40);
        Assert.True(Math.Abs(expandedMetrics[1] - expandedMetrics[2]) < 24);
        Assert.True(Math.Abs(expandedMetrics[3] - previewTopsBeforeExpand[0]) < 2.5);
        Assert.True(Math.Abs(expandedMetrics[4] - previewTopsBeforeExpand[1]) < 2.5);
    }

    [Fact]
    public async Task Desktop_AislePilotSupermarketSelection_PersistsAcrossFreshVisit()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var didSelectAldi = await page.EvaluateAsync<bool>(
            """
            () => {
                const aldiOption = Array.from(document.querySelectorAll("input[data-supermarket-option][value='Aldi']"))
                    .find(input => input instanceof HTMLInputElement && !input.matches(":disabled"));
                if (!(aldiOption instanceof HTMLInputElement)) {
                    return false;
                }

                aldiOption.checked = true;
                aldiOption.dispatchEvent(new Event("change", { bubbles: true }));
                return true;
            }
            """);
        Assert.True(didSelectAldi, "Expected an enabled Aldi supermarket option to be available.");

        var selectedAfterChange = await page.EvaluateAsync<string>(
            """
            () => {
                const selected = Array.from(document.querySelectorAll("input[data-supermarket-option]"))
                    .find(input => input instanceof HTMLInputElement && input.checked && !input.matches(":disabled"));
                return selected instanceof HTMLInputElement ? selected.value : "";
            }
            """);
        Assert.Equal("Aldi", selectedAfterChange);

        await page.GotoAsync($"{_appHost.BaseUrl}/blog");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var selectedAfterReturn = await page.EvaluateAsync<string>(
            """
            () => {
                const selected = Array.from(document.querySelectorAll("input[data-supermarket-option]"))
                    .find(input => input instanceof HTMLInputElement && input.checked && !input.matches(":disabled"));
                return selected instanceof HTMLInputElement ? selected.value : "";
            }
            """);
        Assert.Equal("Aldi", selectedAfterReturn);
    }

    [Fact]
    public async Task Desktop_AislePilotExports_UseRequestedOrder_AndResetChecklistDownloadButton()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var exportTab = page.Locator("#aislepilot-tab-export").First;
        await exportTab.ClickAsync();
        await page.Locator("#aislepilot-export[aria-hidden='false']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });

        var exportButtonLabels = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll("#aislepilot-export .aislepilot-export-btn"))
                .map(button => button.textContent?.replace(/\s+/g, " ").trim() ?? "")
            """);

        Assert.Equal(
            new[]
            {
                "Share shopping list",
                "Download plan pack (.pdf)",
                "Download checklist (.txt)",
                "Print meal and shopping view"
            },
            exportButtonLabels);

        var checklistButton = page.Locator("#aislepilot-export .aislepilot-export-btn").Nth(1);
        var checklistResponseTask = page.WaitForResponseAsync(response =>
            string.Equals(response.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
            response.Url.Contains("/projects/aisle-pilot/export/checklist", StringComparison.OrdinalIgnoreCase));

        await checklistButton.ClickAsync();
        _ = await checklistResponseTask;

        await page.WaitForFunctionAsync(
            """
            () => {
                const button = Array.from(document.querySelectorAll("#aislepilot-export .aislepilot-export-btn"))
                    .find(candidate => candidate.textContent?.includes("Download checklist"));
                return button instanceof HTMLButtonElement
                    && !button.disabled
                    && !button.classList.contains("is-loading")
                    && button.getAttribute("aria-busy") !== "true"
                    && button.textContent?.trim() === "Download checklist (.txt)";
            }
            """,
            null,
            new PageWaitForFunctionOptions
            {
                Timeout = 10000
            });
    }

    [Fact]
    public async Task Desktop_AislePilotLayout_UsesWideMainContainer()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var mainWidth = await page.EvaluateAsync<double>(
            """
            () => {
                const main = document.querySelector("main.app-shell-main.container");
                if (!(main instanceof HTMLElement)) {
                    return 0;
                }

                return main.getBoundingClientRect().width;
            }
            """);

        Assert.True(mainWidth >= 1300, $"Expected a wide desktop container for AislePilot. Actual main width={mainWidth:F1}px.");
    }

    [Fact]
    public async Task Desktop_AislePilotLayout_UsesComfortableMaxContainerWidth()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        await page.GotoAsync($"{_appHost.BaseUrl}/projects/aisle-pilot");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var mainWidth = await page.EvaluateAsync<double>(
            """
            () => {
                const main = document.querySelector("main.app-shell-main.aislepilot-main.container");
                if (!(main instanceof HTMLElement)) {
                    return 0;
                }

                return main.getBoundingClientRect().width;
            }
            """);

        Assert.True(mainWidth >= 1280, $"Expected a comfortable but still wide container floor. Actual width={mainWidth:F1}px.");
        Assert.True(mainWidth <= 1365, $"Expected container max width to avoid excessive eye travel. Actual width={mainWidth:F1}px.");
    }

    [Fact]
    public async Task Desktop_AislePilotShoppingGrid_UsesGridColumnsInsteadOfMasonryColumns()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var shoppingGridMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const grid = document.querySelector(".aislepilot-shopping-grid");
                if (!(grid instanceof HTMLElement)) {
                    return [-1, -1, -1];
                }

                const styles = window.getComputedStyle(grid);
                const display = styles.display;
                const columnCountRaw = (styles.columnCount || "").trim().toLowerCase();
                const usesMasonryColumns = columnCountRaw.length > 0 && columnCountRaw !== "auto" && columnCountRaw !== "normal";
                const templateColumns = (styles.gridTemplateColumns || "")
                    .split(" ")
                    .map(value => value.trim())
                    .filter(value => value.length > 0);
                return [
                    display === "grid" ? 1 : 0,
                    templateColumns.length,
                    usesMasonryColumns ? 1 : 0
                ];
            }
            """);

        Assert.Equal(3, shoppingGridMetrics.Length);
        Assert.Equal(1, shoppingGridMetrics[0]);
        Assert.True(shoppingGridMetrics[1] >= 2, $"Expected at least two shopping grid columns on desktop. Actual={shoppingGridMetrics[1]:F0}.");
        Assert.Equal(0, shoppingGridMetrics[2]);
    }

    [Fact]
    public async Task Desktop_AislePilotShoppingItems_CanBeCheckedOff()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var shoppingTab = page.Locator("#aislepilot-tab-shop").First;
        await shoppingTab.ClickAsync();
        await page.Locator("#aislepilot-shop[aria-hidden='false']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });

        var firstShoppingItem = page.Locator("#aislepilot-shop [data-shopping-item-label]").First;
        var firstShoppingItemCheckbox = firstShoppingItem.Locator("[data-shopping-item-input]").First;
        await firstShoppingItem.ScrollIntoViewIfNeededAsync();
        await firstShoppingItem.ClickAsync();

        Assert.True(await firstShoppingItemCheckbox.IsCheckedAsync());

        var shoppingItemMetrics = await firstShoppingItem.EvaluateAsync<object[]>(
            """
            label => {
                if (!(label instanceof HTMLElement)) {
                    return [0, "", 0];
                }

                const itemKey = label.dataset.shoppingItemKey ?? "";
                const text = label.querySelector("[data-shopping-item-text]");
                const checkbox = label.querySelector("[data-shopping-item-input]");
                const textStyle = text instanceof HTMLElement ? window.getComputedStyle(text) : null;
                let persisted = 0;
                try {
                    const raw = window.localStorage.getItem("aislepilot:shopping-item-state");
                    const parsed = raw ? JSON.parse(raw) : {};
                    persisted = parsed && parsed[itemKey] === true ? 1 : 0;
                } catch {
                    persisted = 0;
                }

                return [
                    label.classList.contains("is-checked") ? 1 : 0,
                    textStyle?.textDecorationLine ?? "",
                    checkbox instanceof HTMLInputElement && checkbox.checked ? persisted : 0
                ];
            }
            """);

        Assert.Equal(3, shoppingItemMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(shoppingItemMetrics[0]));
        Assert.Contains("line-through", Convert.ToString(shoppingItemMetrics[1]) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Convert.ToInt32(shoppingItemMetrics[2]));
    }

    [Fact]
    public async Task Desktop_AislePilotShoppingList_AllowsAddingCustomItems()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var shoppingTab = page.Locator("#aislepilot-tab-shop").First;
        await shoppingTab.ClickAsync();
        await page.Locator("#aislepilot-shop[aria-hidden='false']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 10000
        });

        var customItemInput = page.Locator("[data-custom-shopping-input]").First;
        var addItemButton = page.Locator("[data-custom-shopping-add]").First;
        await customItemInput.FillAsync("Toilet roll");
        await customItemInput.PressAsync("Enter");
        await customItemInput.FillAsync("Kitchen foil");
        await customItemInput.PressAsync("Enter");

        var customItem = page.Locator("[data-custom-shopping-list] [data-shopping-item-text]").Filter(new LocatorFilterOptions
        {
            HasTextString = "Toilet roll"
        }).First;
        await customItem.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.Locator("[data-custom-shopping-list] [data-shopping-item-text]").Filter(new LocatorFilterOptions
        {
            HasTextString = "Kitchen foil"
        }).First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var customItemMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const addButton = document.querySelector("[data-custom-shopping-add]");
                const notesField = document.querySelector("[data-notes-export-content]");
                let hasStoredItem = 0;
                try {
                    const raw = window.localStorage.getItem("aislepilot:custom-shopping-items");
                    const parsed = raw ? JSON.parse(raw) : [];
                    hasStoredItem = Array.isArray(parsed)
                        && parsed.some(item => (item?.text || "") === "Toilet roll")
                        && parsed.some(item => (item?.text || "") === "Kitchen foil")
                        ? 1
                        : 0;
                } catch {
                    hasStoredItem = 0;
                }

                return [
                    Array.from(document.querySelectorAll("[data-custom-shopping-list] [data-shopping-item-text]"))
                        .some(item => item.textContent?.includes("Toilet roll")) ? 1 : 0,
                    Array.from(document.querySelectorAll("[data-custom-shopping-list] [data-shopping-item-text]"))
                        .some(item => item.textContent?.includes("Kitchen foil")) ? 1 : 0,
                    hasStoredItem,
                    notesField instanceof HTMLTextAreaElement
                        && notesField.value.includes("Your extra items")
                        && notesField.value.includes("Toilet roll")
                        && notesField.value.includes("Kitchen foil") ? 1 : 0,
                    addButton instanceof HTMLButtonElement && !addButton.disabled ? 1 : 0,
                    addButton instanceof HTMLButtonElement && !addButton.classList.contains("is-loading") ? 1 : 0,
                    addButton instanceof HTMLButtonElement && addButton.textContent?.trim() === "Add item" ? 1 : 0
                ];
            }
            """);

        Assert.Equal(7, customItemMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(customItemMetrics[0]));
        Assert.Equal(1, Convert.ToInt32(customItemMetrics[1]));
        Assert.Equal(1, Convert.ToInt32(customItemMetrics[2]));
        Assert.Equal(1, Convert.ToInt32(customItemMetrics[3]));
        Assert.Equal(1, Convert.ToInt32(customItemMetrics[4]));
        Assert.Equal(1, Convert.ToInt32(customItemMetrics[5]));
        Assert.Equal(1, Convert.ToInt32(customItemMetrics[6]));
        Assert.True(await addItemButton.IsEnabledAsync());
    }

    [Fact]
    public async Task Desktop_AislePilotOverview_UsesReadableMinimumLabelFontSizes()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var fontSizes = await page.EvaluateAsync<double[]>(
            """
            () => {
                const statLabel = document.querySelector(".aislepilot-stat-label");
                const daySummary = document.querySelector(".aislepilot-day-card-meta[data-day-card-summary]");
                if (!(statLabel instanceof HTMLElement) || !(daySummary instanceof HTMLElement)) {
                    return [-1, -1];
                }

                const statLabelFontSize = Number.parseFloat(window.getComputedStyle(statLabel).fontSize || "0");
                const daySummaryFontSize = Number.parseFloat(window.getComputedStyle(daySummary).fontSize || "0");
                return [statLabelFontSize, daySummaryFontSize];
            }
            """);

        Assert.Equal(2, fontSizes.Length);
        Assert.True(fontSizes[0] >= 11.5, $"Expected overview stat labels to be at least 11.5px. Actual={fontSizes[0]:F2}px.");
        Assert.True(fontSizes[1] >= 13, $"Expected day summary labels to be at least 13px. Actual={fontSizes[1]:F2}px.");
    }

    [Fact]
    public async Task Desktop_AislePilotOverview_KeepsSupermarketCardCompactAndShowsResearchInFullWidthStrip()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var snapshotMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const primaryGrid = document.querySelector("[data-overview-primary-grid]");
                const supportGrid = document.querySelector("[data-overview-support-grid]");
                const supermarket = document.querySelector("[data-supermarket-card]");
                const estimate = Array.from(document.querySelectorAll("[data-overview-primary-grid] .aislepilot-stat-card"))
                    .find(card => card.textContent?.toLowerCase().includes("meal ingredient estimate"));
                const featured = document.querySelector(".aislepilot-stat-card--featured");
                const strip = document.querySelector("[data-supermarket-insight-strip]");
                const summaryCards = Array.from(document.querySelectorAll("[data-overview-support-grid] .aislepilot-stat-card"))
                    .filter(node => node instanceof HTMLElement);
                const stripPanels = Array.from(document.querySelectorAll("[data-supermarket-insight-panel]"))
                    .filter(node => node instanceof HTMLElement);
                if (!(primaryGrid instanceof HTMLElement) || !(supportGrid instanceof HTMLElement) || !(supermarket instanceof HTMLElement) || !(estimate instanceof HTMLElement) || !(featured instanceof HTMLElement) || !(strip instanceof HTMLElement)) {
                    return [-1, -1, -1, -1, -1, -1, -1, -1];
                }

                const supermarketRect = supermarket.getBoundingClientRect();
                const estimateRect = estimate.getBoundingClientRect();
                const featuredRect = featured.getBoundingClientRect();
                const stripRect = strip.getBoundingClientRect();
                const primaryGridRect = primaryGrid.getBoundingClientRect();
                const supportGridRect = supportGrid.getBoundingClientRect();
                let summaryCoverage = 0;
                if (summaryCards.length > 0) {
                    const summaryRects = summaryCards.map(card => card.getBoundingClientRect());
                    const left = Math.min(...summaryRects.map(rect => rect.left));
                    const right = Math.max(...summaryRects.map(rect => rect.right));
                    summaryCoverage = (right - left) / supportGridRect.width;
                }

                return [
                    supermarketRect.height,
                    estimateRect.height,
                    featuredRect.height,
                    stripRect.width / primaryGridRect.width,
                    supportGridRect.top - primaryGridRect.bottom,
                    stripRect.top - supportGridRect.bottom,
                    stripPanels.length,
                    summaryCoverage,
                    estimate.querySelector(".aislepilot-card-meta") ? 1 : 0
                ];
            }
            """);

        Assert.Equal(9, snapshotMetrics.Length);
        Assert.True(snapshotMetrics[0] <= snapshotMetrics[2] + 18, $"Expected the supermarket card to stay close to the primary stat card height. Supermarket={snapshotMetrics[0]:F1}px Featured={snapshotMetrics[2]:F1}px.");
        Assert.True(snapshotMetrics[1] <= snapshotMetrics[2] + 18, $"Expected the estimate card to stay compact beside the other primary cards. Estimate={snapshotMetrics[1]:F1}px Featured={snapshotMetrics[2]:F1}px.");
        Assert.True(snapshotMetrics[3] >= 0.92, $"Expected the supermarket insight strip to use most of the snapshot width. Ratio={snapshotMetrics[3]:F2}.");
        Assert.True(snapshotMetrics[4] >= 8 && snapshotMetrics[4] <= 24, $"Expected the support grid to sit directly below the primary grid. Gap={snapshotMetrics[4]:F1}px.");
        Assert.True(snapshotMetrics[5] >= 8 && snapshotMetrics[5] <= 28, $"Expected the insight strip to sit directly below the support grid. Gap={snapshotMetrics[5]:F1}px.");
        Assert.True(snapshotMetrics[6] >= 2, $"Expected structured supermarket insight panels. Count={snapshotMetrics[6]:F0}.");
        Assert.True(snapshotMetrics[7] >= 0.92, $"Expected the summary row to use most of the snapshot width. Coverage={snapshotMetrics[7]:F2}.");
        Assert.Equal(0, snapshotMetrics[8]);
    }

}
