using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;
namespace MyBlog.Tests;
public sealed partial class PlaywrightE2ETests : IAsyncLifetime
{
    [Fact]
    public async Task NarrowMobile_AislePilotPeopleSlider_UsesThumbFriendlyTouchTarget()
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

        await page.EvaluateAsync(
            """
            () => {
                const servingDetails = Array.from(document.querySelectorAll(".aislepilot-collapsible"))
                    .find(section => section.querySelector(".aislepilot-collapsible-title")?.textContent?.trim() === "Cooking for");
                if (servingDetails instanceof HTMLDetailsElement) {
                    servingDetails.open = true;
                }
            }
            """);

        var peopleSlider = page.Locator(".aislepilot-slider-field--thumb-friendly input[name='Request.HouseholdSize']").First;
        await peopleSlider.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        var metricsText = await page.EvaluateAsync<string>(
            """
            () => {
                const field = document.querySelector(".aislepilot-slider-field--thumb-friendly");
                if (!(field instanceof HTMLElement)) {
                    return "NaN|NaN|NaN";
                }

                const input = field.querySelector("input[name='Request.HouseholdSize']");
                const valueBubble = field.querySelector("[data-number-slider-value]");
                if (!(input instanceof HTMLInputElement) || !(valueBubble instanceof HTMLElement)) {
                    return "NaN|NaN|NaN";
                }

                const inputRect = input.getBoundingClientRect();
                const valueRect = valueBubble.getBoundingClientRect();
                return `${inputRect.height}|${valueRect.width}|${valueRect.height}`;
            }
            """);

        static double ParseMetric(string raw)
        {
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.NaN;
        }

        var metrics = (metricsText ?? "NaN|NaN|NaN").Split('|');
        var inputHeight = metrics.Length > 0 ? ParseMetric(metrics[0]) : double.NaN;
        var bubbleWidth = metrics.Length > 1 ? ParseMetric(metrics[1]) : double.NaN;
        var bubbleHeight = metrics.Length > 2 ? ParseMetric(metrics[2]) : double.NaN;

        Assert.True(
            inputHeight >= 32,
            $"Expected the people slider input to keep at least a 32px mobile hit area. Actual={inputHeight}px.");
        Assert.True(
            bubbleWidth >= 34,
            $"Expected the people slider value bubble to stay wide enough for thumb dragging on mobile. Actual={bubbleWidth}px.");
        Assert.True(
            bubbleHeight >= 24,
            $"Expected the people slider value bubble to stay tall enough for thumb dragging on mobile. Actual={bubbleHeight}px.");
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
                    return [0, "missing", "missing", -1, Number.POSITIVE_INFINITY, 0, "missing", Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY];
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
                    return [0, "missing", "missing", -1, Number.POSITIVE_INFINITY, 0, "missing", Number.POSITIVE_INFINITY, Number.POSITIVE_INFINITY];
                }

                const styles = window.getComputedStyle(name);
                const rowRect = row.getBoundingClientRect();
                const panelRect = panel.getBoundingClientRect();
                const viewportPadding = 4;
                const rowVisibleWithinPanel =
                    rowRect.top >= panelRect.top - 1 &&
                    rowRect.bottom <= Math.min(panelRect.bottom, window.innerHeight - viewportPadding) + 1;

                const panelOverflowBottom = Math.max(0, panelRect.bottom - (window.innerHeight - viewportPadding));
                const sampleX = Math.max(8, Math.min(window.innerWidth - 8, panelRect.left + Math.min(24, panelRect.width / 2)));
                const sampleY = Math.max(8, Math.min(window.innerHeight - 8, panelRect.bottom - 12));
                const topElement = document.elementFromPoint(sampleX, sampleY);
                const panelIsTopLayer = topElement instanceof Element && panel.contains(topElement);
                return [
                    rowVisibleWithinPanel ? 1 : 0,
                    styles.whiteSpace || "",
                    styles.textOverflow || "",
                    rowRect.height,
                    panelOverflowBottom,
                    panelIsTopLayer ? 1 : 0,
                    topElement instanceof Element ? `${topElement.tagName}.${topElement.className}` : "none",
                    Math.max(0, -panelRect.left),
                    Math.max(0, panelRect.right - window.innerWidth)
                ];
            }
            """);

        Assert.Equal(9, menuMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(menuMetrics[0]));
        Assert.NotEqual("nowrap", Convert.ToString(menuMetrics[1]));
        Assert.NotEqual("ellipsis", Convert.ToString(menuMetrics[2]));
        Assert.True(Convert.ToDouble(menuMetrics[3]) >= 30, $"Expected saved meal rows to remain readable. Height={menuMetrics[3]}px.");
        Assert.True(
            Convert.ToDouble(menuMetrics[4]) <= 1.5,
            $"Expected saved meals menu to fit the viewport without forcing page scroll. Overflow={menuMetrics[4]}px.");
        Assert.True(
            Convert.ToInt32(menuMetrics[5]) == 1,
            $"Expected the saved menu to sit above page content. Top element={menuMetrics[6]}.");
        Assert.True(
            Convert.ToDouble(menuMetrics[7]) <= 1.5 && Convert.ToDouble(menuMetrics[8]) <= 1.5,
            $"Expected the saved menu to remain inside the viewport. Left overflow={menuMetrics[7]}px, right overflow={menuMetrics[8]}px.");
    }

    [Fact]
    public async Task Mobile_AislePilotPrimaryResultsNavigation_CanSwitchBetweenPanels()
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

        var shoppingJump = page.Locator(".aislepilot-window-tab[data-window-tab='aislepilot-shop']").First;
        await shoppingJump.ClickAsync();

        var shoppingPanel = page.Locator("#aislepilot-shop[aria-hidden='false']").First;
        await shoppingPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 15000
        });
        Assert.Equal("true", await shoppingJump.GetAttributeAsync("aria-selected"));

        var exportsJump = page.Locator(".aislepilot-window-tab[data-window-tab='aislepilot-export']").First;
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
    public async Task Mobile_AislePilotOverviewActions_UseHamburgerMenuForRefreshAndSave()
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
                const save = menu.querySelectorAll(".aislepilot-overview-regenerate-btn")[1];
                const refreshText = refresh instanceof HTMLElement ? (refresh.textContent || "").trim() : "";
                const saveText = save instanceof HTMLElement ? (save.textContent || "").trim() : "";
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
                    saveText,
                    isButtonCenterVisible(refresh) ? 1 : 0,
                    isButtonCenterVisible(save) ? 1 : 0,
                    Number.isFinite(menuZIndex) ? menuZIndex : -1,
                    Number.isFinite(mobileContextZIndex) ? mobileContextZIndex : -1,
                    overviewSection instanceof HTMLElement && overviewSection.classList.contains("is-actions-menu-open") ? 1 : 0
                ];
            }
            """);

        Assert.Equal(10, menuMetrics.Length);
        Assert.True(Convert.ToDouble(menuMetrics[0]) <= 1.5, $"Expected overview menu to stay inside viewport on left edge. Overflow={menuMetrics[0]}.");
        Assert.True(Convert.ToDouble(menuMetrics[1]) <= 1.5, $"Expected overview menu to stay inside viewport on right edge. Overflow={menuMetrics[1]}.");
        Assert.Equal(2, Convert.ToInt32(menuMetrics[2]));
        Assert.Contains("Refresh plan", Convert.ToString(menuMetrics[3]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Save week", Convert.ToString(menuMetrics[4]), StringComparison.OrdinalIgnoreCase);
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
    public async Task Mobile_AislePilotOverBudgetStatus_RemainsVisibleWithCollapsedDetailsAndRebalancesOnce()
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
        await page.Locator("input[name='Request.WeeklyBudget']:not(:disabled)").EvaluateAsync(
            """
            element => {
                element.value = '105';
                element.dispatchEvent(new Event('input', { bubbles: true }));
                element.dispatchEvent(new Event('change', { bubbles: true }));
            }
            """);
        await page.Locator("[data-mobile-setup-submit='planner']:visible, [data-setup-mode-submit='planner']:visible").First.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var details = page.Locator("[data-overview-content]");
        var toggle = page.Locator("[data-overview-toggle]");
        var warning = page.Locator(".aislepilot-overbudget-flag");
        var recovery = warning.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Refresh lower-cost plan" });
        Assert.True(await warning.IsVisibleAsync());
        Assert.True(await recovery.IsVisibleAsync());
        Assert.True(await details.IsHiddenAsync());
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.Contains("over budget", await page.Locator(".aislepilot-overview-caption").TextContentAsync(), StringComparison.OrdinalIgnoreCase);

        await toggle.ClickAsync();
        Assert.True(await details.IsVisibleAsync());
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await warning.IsVisibleAsync());
        await toggle.ClickAsync();
        Assert.True(await details.IsHiddenAsync());
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await warning.IsVisibleAsync());

        var navBottom = await page.Locator(".aislepilot-window-tabs").EvaluateAsync<double>("element => element.getBoundingClientRect().bottom");
        Assert.True(navBottom <= 844, $"Expected Meals, Shop and Export navigation in the first viewport. Bottom={navBottom:F1}px.");

        var rebalancePosts = 0;
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.Contains("rebalance-budget", StringComparison.OrdinalIgnoreCase))
            {
                rebalancePosts++;
            }
        };
        Assert.Equal("105", await warning.Locator("input[name='Request.WeeklyBudget']").InputValueAsync());
        await recovery.ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Equal(1, rebalancePosts);
        Assert.True(await page.Locator("input[name='Request.WeeklyBudget'][value='105']").CountAsync() > 0);
    }

    [Fact]
    public async Task Mobile_AislePilotDayCarousel_ShowsSinglePrimarySlideAndHeaderSummary()
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
                const carousel = document.querySelector("[data-day-card-carousel]");
                const viewport = carousel?.querySelector("[data-day-carousel-viewport]");
                const pagination = carousel?.querySelector("[data-day-carousel-pagination]");
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeSlide = slides.find(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                if (!(carousel instanceof HTMLElement) || !(viewport instanceof HTMLElement) || !(pagination instanceof HTMLElement) || !(activeSlide instanceof HTMLElement)) {
                    return [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "missing", "missing", "missing"];
                }

                const headerSummary = activeSlide.querySelector("[data-day-card-summary], .aislepilot-day-card-head-main .aislepilot-day-card-meta");
                const inlineSummary = activeSlide.querySelector("[data-day-meal-summary]");
                if (!(headerSummary instanceof HTMLElement) || !(inlineSummary instanceof HTMLElement)) {
                    return [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "missing", "missing", "missing"];
                }

                const activeCount = slides.filter(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false").length;
                const hiddenCount = slides.filter(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "true").length;
                const headerStyle = window.getComputedStyle(headerSummary);
                const inlineStyle = window.getComputedStyle(inlineSummary);
                const headerVisible = headerStyle.display !== "none" && headerSummary.getBoundingClientRect().height > 0 ? 1 : 0;
                const inlineVisible = inlineStyle.display !== "none" && inlineSummary.getBoundingClientRect().height > 0 ? 1 : 0;
                const hasHorizontalOverflow = viewport.scrollWidth > viewport.clientWidth + 16 ? 1 : 0;
                const viewportRect = viewport.getBoundingClientRect();
                const paginationRect = pagination.getBoundingClientRect();
                const activeRect = activeSlide.getBoundingClientRect();
                const activeWidthRatio = viewportRect.width > 0 ? activeRect.width / viewportRect.width : 0;
                const activeCenterDelta = viewportRect.width > 0
                    ? Math.abs((activeRect.left + (activeRect.width / 2)) - (viewportRect.left + (viewportRect.width / 2)))
                    : Number.POSITIVE_INFINITY;
                const nextSlide = slides.find(slide => slide instanceof HTMLElement && slide.dataset.dayCarouselPosition === "next");
                const previousSlide = slides.find(slide => slide instanceof HTMLElement && slide.dataset.dayCarouselPosition === "prev");
                const visibleWidthWithinViewport = slide => {
                    if (!(slide instanceof HTMLElement)) {
                        return 0;
                    }

                    const rect = slide.getBoundingClientRect();
                    return Math.max(0, Math.min(rect.right, viewportRect.right) - Math.max(rect.left, viewportRect.left));
                };
                const nextPeekWidth = visibleWidthWithinViewport(nextSlide);
                const previousPeekWidth = visibleWidthWithinViewport(previousSlide);
                const activeDot = carousel.querySelector("[data-day-carousel-dot][aria-selected='true']");
                const inactiveDot = carousel.querySelector("[data-day-carousel-dot][aria-selected='false']");
                const activeDotWidth = activeDot instanceof HTMLElement ? activeDot.getBoundingClientRect().width : 0;
                const inactiveDotWidth = inactiveDot instanceof HTMLElement ? inactiveDot.getBoundingClientRect().width : 0;
                const activeDotHeight = activeDot instanceof HTMLElement ? activeDot.getBoundingClientRect().height : 0;
                const inactiveDotHeight = inactiveDot instanceof HTMLElement ? inactiveDot.getBoundingClientRect().height : 0;
                const selectorAboveViewport = paginationRect.bottom <= viewportRect.top + 12 ? 1 : 0;
                const activeDotText = activeDot instanceof HTMLElement ? (activeDot.textContent || "").replace(/\s+/g, " ").trim() : "";
                const inactiveDotText = inactiveDot instanceof HTMLElement ? (inactiveDot.textContent || "").replace(/\s+/g, " ").trim() : "";
                const headerText = (headerSummary.textContent || "").replace(/\s+/g, " ").trim();
                return [
                    activeCount,
                    hiddenCount,
                    headerVisible,
                    inlineVisible,
                    hasHorizontalOverflow,
                    activeWidthRatio,
                    activeCenterDelta,
                    nextPeekWidth,
                    previousPeekWidth,
                    activeDotWidth,
                    inactiveDotWidth,
                    activeDotHeight,
                    inactiveDotHeight,
                    selectorAboveViewport,
                    activeDotText,
                    inactiveDotText,
                    headerText
                ];
            }
            """);

        Assert.Equal(17, summaryMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(summaryMetrics[0]));
        Assert.True(Convert.ToInt32(summaryMetrics[1]) >= 1, $"Expected at least one inactive slide. Hidden={summaryMetrics[1]}.");
        Assert.Equal(1, Convert.ToInt32(summaryMetrics[2]));
        Assert.Equal(0, Convert.ToInt32(summaryMetrics[3]));
        Assert.Equal(1, Convert.ToInt32(summaryMetrics[4]));
        Assert.InRange(Convert.ToDouble(summaryMetrics[5]), 0.9d, 1.04d);
        Assert.True(Convert.ToDouble(summaryMetrics[6]) <= 10d, $"Expected the active slide to stay centered in the viewport. Delta={summaryMetrics[6]}.");
        Assert.True(Convert.ToDouble(summaryMetrics[7]) <= 12d, $"Expected no distracting next-slide peek on mobile. Peek={summaryMetrics[7]}.");
        Assert.True(Convert.ToDouble(summaryMetrics[8]) <= 12d, $"Expected no distracting previous-slide peek on mobile. Peek={summaryMetrics[8]}.");
        Assert.True(Math.Abs(Convert.ToDouble(summaryMetrics[9]) - Convert.ToDouble(summaryMetrics[10])) <= 20d, $"Expected active and inactive day chips to keep a stable width. Active={summaryMetrics[9]}, inactive={summaryMetrics[10]}.");
        Assert.True(Convert.ToDouble(summaryMetrics[11]) >= 30d, $"Expected the active day chip to remain tappable. Height={summaryMetrics[11]}.");
        Assert.True(Convert.ToDouble(summaryMetrics[12]) >= 30d, $"Expected inactive day chips to remain tappable. Height={summaryMetrics[12]}.");
        Assert.Equal(1, Convert.ToInt32(summaryMetrics[13]));
        Assert.True((Convert.ToString(summaryMetrics[14]) ?? string.Empty).Length >= 3, $"Expected active day chip text to stay visible. Text='{summaryMetrics[14]}'.");
        Assert.True((Convert.ToString(summaryMetrics[15]) ?? string.Empty).Length >= 3, $"Expected inactive day chip text to stay visible. Text='{summaryMetrics[15]}'.");
        var summaryText = Convert.ToString(summaryMetrics[16]) ?? string.Empty;
        Assert.True(
            summaryText.Contains("mins", StringComparison.OrdinalIgnoreCase) ||
            summaryText.Contains("removed", StringComparison.OrdinalIgnoreCase) ||
            summaryText.Contains("£", StringComparison.OrdinalIgnoreCase),
            $"Expected visible header summary to include meal meta text. Text='{summaryText}'.");
    }

    [Fact]
    public async Task Mobile_AislePilotDayCarousel_NextButtonMovesToNextDay()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var initialMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const status = document.querySelector("[data-day-carousel-status]");
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                return [status instanceof HTMLElement ? (status.textContent || "").trim() : "", activeIndex, slides.length];
            }
            """);
        Assert.Equal(3, initialMetrics.Length);
        Assert.True(Convert.ToInt32(initialMetrics[2]) >= 2, $"Expected multiple day slides. Count={initialMetrics[2]}.");

        var secondDayTab = page.Locator("[data-day-carousel-dot][data-day-carousel-target='1']").First;
        await secondDayTab.ScrollIntoViewIfNeededAsync();
        await secondDayTab.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const status = document.querySelector("[data-day-carousel-status]");
                return activeIndex === 1 && status instanceof HTMLElement && /2 of/i.test(status.textContent || "");
            }
            """);

        var carouselMetrics = await page.EvaluateAsync<object[]>(
            """
            () => {
                const status = document.querySelector("[data-day-carousel-status]");
                const slides = Array.from(document.querySelectorAll("[data-day-card-slide]:not([data-day-carousel-ghost='true'])"));
                const activeSlides = slides.filter(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeIndex = slides.findIndex(slide => slide instanceof HTMLElement && slide.getAttribute("aria-hidden") === "false");
                const activeDot = document.querySelector("[data-day-carousel-dot][aria-selected='true']");
                return [
                    status instanceof HTMLElement ? (status.textContent || "").trim() : "",
                    activeIndex,
                    activeSlides.length,
                    activeDot instanceof HTMLElement ? Number.parseInt(activeDot.getAttribute("data-day-carousel-target") || "-1", 10) : -1
                ];
            }
            """);

        Assert.Equal(4, carouselMetrics.Length);
        Assert.Contains("2 of", Convert.ToString(carouselMetrics[0]) ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, Convert.ToInt32(carouselMetrics[1]));
        Assert.Equal(1, Convert.ToInt32(carouselMetrics[2]));
        Assert.Equal(1, Convert.ToInt32(carouselMetrics[3]));
    }


}
