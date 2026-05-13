using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Desktop_AislePilotStackedLayout_CollapsedMealPreview_DoesNotReserveEmptyDetailsColumn()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var stackedToggle = page.Locator(".aislepilot-day-view-toggle").First;
        await stackedToggle.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const carousel = document.querySelector("[data-day-card-carousel]");
                return carousel instanceof HTMLElement &&
                    carousel.getAttribute("data-day-stacked-mode") === "true" &&
                    carousel.getAttribute("data-day-reorder-mode") !== "true";
            }
            """);

        var collapsedMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const activeCard = document.querySelector("[data-day-card-slide][aria-hidden='false']:not([data-day-carousel-ghost='true'])");
                const panel = activeCard instanceof HTMLElement
                    ? activeCard.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const inlineToggle = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-toggle]")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                const imageShell = panel instanceof HTMLElement
                    ? panel.querySelector(".aislepilot-meal-image-shell")
                    : null;
                if (!(panel instanceof HTMLElement) ||
                    !(inlineToggle instanceof HTMLDetailsElement) ||
                    !(detailsPanel instanceof HTMLElement) ||
                    !(imageShell instanceof HTMLElement))
                {
                    return [-1, -1, -1, -1];
                }

                inlineToggle.open = false;
                inlineToggle.dispatchEvent(new Event("toggle"));

                const gridColumnsRaw = window.getComputedStyle(panel).gridTemplateColumns || "";
                const columnCount = gridColumnsRaw
                    .split(/\s+/)
                    .map(token => token.trim())
                    .filter(token => token.length > 0)
                    .length;
                const panelRect = panel.getBoundingClientRect();
                const imageRect = imageShell.getBoundingClientRect();

                return [
                    detailsPanel.hasAttribute("hidden") ? 1 : 0,
                    columnCount,
                    imageRect.height,
                    panelRect.height
                ];
            }
            """);

        Assert.Equal(4, collapsedMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(collapsedMetrics[0]));
        Assert.Equal(1, Convert.ToInt32(collapsedMetrics[1]));
        Assert.True(
            collapsedMetrics[3] <= collapsedMetrics[2] + 32,
            $"Expected collapsed stacked panel to stay compact without a stretched empty details column. Panel={collapsedMetrics[3]:F1}px Image={collapsedMetrics[2]:F1}px.");
    }

    [Fact]
    public async Task Desktop_AislePilotStackedLayout_AlignsTabsWithDetailsColumn()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var stackedToggle = page.Locator(".aislepilot-day-view-toggle").First;
        await stackedToggle.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const carousel = document.querySelector("[data-day-card-carousel]");
                return carousel instanceof HTMLElement &&
                    carousel.getAttribute("data-day-stacked-mode") === "true" &&
                    carousel.getAttribute("data-day-reorder-mode") !== "true";
            }
            """);

        var firstMealTab = page.Locator("[data-day-card-slide]:not([data-day-carousel-ghost='true'])").First
            .Locator("[data-day-meal-tab]").First;
        var detailsPanelInitiallyHidden = await page.EvaluateAsync<bool>(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const panel = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                return !(detailsPanel instanceof HTMLElement)
                    || detailsPanel.hasAttribute("hidden")
                    || detailsPanel.getAttribute("aria-hidden") === "true";
            }
            """);

        if (detailsPanelInitiallyHidden)
        {
            await firstMealTab.ClickAsync();
        }
        else
        {
            await firstMealTab.ClickAsync();
            await firstMealTab.ClickAsync();
        }

        await page.WaitForFunctionAsync(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const panel = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                return detailsPanel instanceof HTMLElement
                    && !detailsPanel.hasAttribute("hidden")
                    && detailsPanel.getAttribute("aria-hidden") !== "true";
            }
            """);

        var metrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const tabs = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-tabs")
                    : null;
                const panel = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                const imageShell = panel instanceof HTMLElement
                    ? panel.querySelector(".aislepilot-meal-image-shell")
                    : null;
                if (!(card instanceof HTMLElement) ||
                    !(tabs instanceof HTMLElement) ||
                    !(detailsPanel instanceof HTMLElement) ||
                    !(imageShell instanceof HTMLElement))
                {
                    return [-1, -1, -1, -1];
                }

                const cardRect = card.getBoundingClientRect();
                const tabsRect = tabs.getBoundingClientRect();
                const detailsRect = detailsPanel.getBoundingClientRect();
                const imageRect = imageShell.getBoundingClientRect();

                return [
                    imageRect.left - detailsRect.right,
                    tabsRect.right - detailsRect.right,
                    imageRect.left - tabsRect.right,
                    cardRect.bottom - imageRect.bottom
                ];
            }
            """);

        Assert.Equal(4, metrics.Length);
        Assert.True(
            metrics[0] >= 6,
            $"Expected stacked detail and image columns to keep a clear gutter. Gap={metrics[0]:F1}px.");
        Assert.True(
            metrics[1] <= 6,
            $"Expected stacked meal tabs to align with the details column edge. Overflow={metrics[1]:F1}px.");
        Assert.True(
            metrics[2] >= 6,
            $"Expected stacked meal tabs to stop before the desktop image column. Separation={metrics[2]:F1}px.");
        Assert.True(
            metrics[3] <= 28,
            $"Expected stacked card body to avoid excess desktop whitespace under the image. Gap={metrics[3]:F1}px.");
    }

    [Fact]
    public async Task Desktop_AislePilotStackedLayout_CollapsedCards_UseFullWidthTabs()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var stackedToggle = page.Locator(".aislepilot-day-view-toggle").First;
        await stackedToggle.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const carousel = document.querySelector("[data-day-card-carousel]");
                return carousel instanceof HTMLElement &&
                    carousel.getAttribute("data-day-stacked-mode") === "true" &&
                    carousel.getAttribute("data-day-reorder-mode") !== "true";
            }
            """);

        var firstMealTab = page.Locator("[data-day-card-slide]:not([data-day-carousel-ghost='true'])").First
            .Locator("[data-day-meal-tab]").First;
        var detailsVisible = await page.EvaluateAsync<bool>(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const panel = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                return detailsPanel instanceof HTMLElement &&
                    !detailsPanel.hasAttribute("hidden") &&
                    detailsPanel.getAttribute("aria-hidden") !== "true";
            }
            """);

        if (detailsVisible)
        {
            await firstMealTab.ClickAsync();
        }

        await page.WaitForFunctionAsync(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const panel = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                return card instanceof HTMLElement &&
                    card.getAttribute("data-day-card-expanded") !== "true" &&
                    (!(detailsPanel instanceof HTMLElement) ||
                        detailsPanel.hasAttribute("hidden") ||
                        detailsPanel.getAttribute("aria-hidden") === "true");
            }
            """);

        var metrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const body = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-card-body")
                    : null;
                const tabs = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-tabs")
                    : null;
                if (!(card instanceof HTMLElement) || !(tabs instanceof HTMLElement))
                {
                    return [-1, -1, -1];
                }

                const containerRect = body instanceof HTMLElement
                    ? body.getBoundingClientRect()
                    : card.getBoundingClientRect();
                const tabsRect = tabs.getBoundingClientRect();

                return [
                    containerRect.right - tabsRect.right,
                    tabsRect.left - containerRect.left,
                    tabsRect.width / Math.max(containerRect.width, 1)
                ];
            }
            """);

        Assert.Equal(3, metrics.Length);
        Assert.True(
            metrics[0] <= 26,
            $"Expected collapsed stacked tabs to align close to the card body right edge on desktop. Right gap={metrics[0]:F1}px.");
        Assert.True(
            metrics[1] <= 26,
            $"Expected collapsed stacked tabs to align close to the card body left edge on desktop. Left gap={metrics[1]:F1}px.");
        Assert.True(
            metrics[2] >= 0.9,
            $"Expected collapsed stacked tabs to use most of the available desktop width. Coverage={metrics[2]:F2}.");
    }

    [Fact]
    public async Task Desktop_AislePilotStackedLayout_UsesSingleInspectorSurfaceWhenExpanded()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var stackedToggle = page.Locator(".aislepilot-day-view-toggle").First;
        await stackedToggle.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const carousel = document.querySelector("[data-day-card-carousel]");
                return carousel instanceof HTMLElement &&
                    carousel.getAttribute("data-day-stacked-mode") === "true" &&
                    carousel.getAttribute("data-day-reorder-mode") !== "true";
            }
            """);

        var firstMealTab = page.Locator("[data-day-card-slide]:not([data-day-carousel-ghost='true'])").First
            .Locator("[data-day-meal-tab]").First;
        var detailsVisible = await page.EvaluateAsync<bool>(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const panel = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                return detailsPanel instanceof HTMLElement &&
                    !detailsPanel.hasAttribute("hidden") &&
                    detailsPanel.getAttribute("aria-hidden") !== "true";
            }
            """);

        if (!detailsVisible)
        {
            await firstMealTab.ClickAsync();
        }

        await page.WaitForFunctionAsync(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const panel = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                return detailsPanel instanceof HTMLElement &&
                    !detailsPanel.hasAttribute("hidden") &&
                    detailsPanel.getAttribute("aria-hidden") !== "true";
            }
            """);

        var metrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const tabs = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-tabs")
                    : null;
                const panel = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                const inspectorPanels = detailsPanel instanceof HTMLElement
                    ? detailsPanel.querySelector(".aislepilot-inspector-panels")
                    : null;

                if (!(tabs instanceof HTMLElement) ||
                    !(detailsPanel instanceof HTMLElement) ||
                    !(inspectorPanels instanceof HTMLElement))
                {
                    return [-1, -1, -1];
                }

                const tabsStyles = window.getComputedStyle(tabs);
                const detailsStyles = window.getComputedStyle(detailsPanel);
                const inspectorStyles = window.getComputedStyle(inspectorPanels);

                return [
                    parseFloat(tabsStyles.borderTopWidth || "0"),
                    parseFloat(detailsStyles.borderTopWidth || "0"),
                    parseFloat(inspectorStyles.borderTopWidth || "0")
                ];
            }
            """);

        Assert.Equal(3, metrics.Length);
        Assert.True(
            metrics[0] <= 0.5,
            $"Expected expanded stacked meal-strip container to be unframed. Border={metrics[0]:F1}px.");
        Assert.True(
            metrics[1] <= 0.5,
            $"Expected expanded stacked details wrapper to avoid a second frame. Border={metrics[1]:F1}px.");
        Assert.True(
            metrics[2] >= 1,
            $"Expected expanded stacked inspector content to use one clear surface border. Border={metrics[2]:F1}px.");
    }

    [Fact]
    public async Task Desktop_AislePilotStackedLayout_InspectorTabsStayInteractiveAfterSwap()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var stackedToggle = page.Locator(".aislepilot-day-view-toggle").First;
        await stackedToggle.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const carousel = document.querySelector("[data-day-card-carousel]");
                return carousel instanceof HTMLElement &&
                    carousel.getAttribute("data-day-stacked-mode") === "true" &&
                    carousel.getAttribute("data-day-reorder-mode") !== "true";
            }
            """);

        var firstMealTab = page.Locator("[data-day-card-slide]:not([data-day-carousel-ghost='true'])").First
            .Locator("[data-day-meal-tab]").First;
        var detailsVisible = await page.EvaluateAsync<bool>(
            """
            () => {
                const card = document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true'])");
                const panel = card instanceof HTMLElement
                    ? card.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']")
                    : null;
                const detailsPanel = panel instanceof HTMLElement
                    ? panel.querySelector("[data-inline-details-panel]")
                    : null;
                return detailsPanel instanceof HTMLElement &&
                    !detailsPanel.hasAttribute("hidden") &&
                    detailsPanel.getAttribute("aria-hidden") !== "true";
            }
            """);

        if (!detailsVisible)
        {
            await firstMealTab.ClickAsync();
        }

        var activeCard = page.Locator("[data-day-card-slide][aria-hidden='false']:not([data-day-carousel-ghost='true'])").First;
        var moreActionsSummary = activeCard.Locator("[data-day-card-header-actions].is-active [data-card-more-actions] > summary").First;
        await moreActionsSummary.ScrollIntoViewIfNeededAsync();
        await moreActionsSummary.ClickAsync();

        var swapButton = activeCard
            .Locator("[data-day-card-header-actions].is-active .aislepilot-card-more-actions-menu .aislepilot-swap-form button[type='submit']")
            .First;
        await swapButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var swapResponseTask = page.WaitForResponseAsync(response =>
            response.Ok &&
            response.Url.Contains("/projects/aisle-pilot/swap-meal", StringComparison.OrdinalIgnoreCase));
        await swapButton.ClickAsync();
        _ = await swapResponseTask;

        await page.WaitForFunctionAsync(
            """
            () => {
                const activeCard = document.querySelector("[data-day-card-slide][aria-hidden='false']:not([data-day-carousel-ghost='true'])");
                if (!(activeCard instanceof HTMLElement) || activeCard.getAttribute("aria-busy") === "true") {
                    return false;
                }

                const activePanel = activeCard.querySelector("[data-day-meal-panel][aria-hidden='false']");
                const detailsPanel = activePanel instanceof HTMLElement
                    ? activePanel.querySelector("[data-inline-details-panel]")
                    : null;
                if (!(detailsPanel instanceof HTMLElement)) {
                    return false;
                }

                return !detailsPanel.hasAttribute("hidden")
                    && detailsPanel.getAttribute("aria-hidden") !== "true"
                    && detailsPanel.querySelectorAll(".aislepilot-inspector-tab").length >= 4;
            }
            """);

        var inspectorStates = await page.EvaluateAsync<string[]>(
            """
            () => {
                const activeCard = document.querySelector("[data-day-card-slide][aria-hidden='false']:not([data-day-carousel-ghost='true'])");
                if (!(activeCard instanceof HTMLElement)) {
                    return [];
                }

                const activeKey = () => {
                    const activePanel = activeCard.querySelector(".aislepilot-inspector-panel.is-active");
                    return activePanel instanceof HTMLElement
                        ? (activePanel.dataset.inspectorPanel ?? "")
                        : "";
                };

                const clickTab = key => {
                    const button = activeCard.querySelector(`.aislepilot-inspector-tab[data-inspector-tab='${key}']`);
                    if (!(button instanceof HTMLButtonElement)) {
                        return false;
                    }

                    button.click();
                    return true;
                };

                const before = activeKey();
                const clickedMacros = clickTab("nutrition");
                const afterMacros = activeKey();
                const clickedIngredients = clickTab("ingredients");
                const afterIngredients = activeKey();
                const clickedMethod = clickTab("method");
                const afterMethod = activeKey();

                return [
                    before,
                    clickedMacros ? "1" : "0",
                    afterMacros,
                    clickedIngredients ? "1" : "0",
                    afterIngredients,
                    clickedMethod ? "1" : "0",
                    afterMethod
                ];
            }
            """);

        Assert.Equal(7, inspectorStates.Length);
        Assert.Equal("1", inspectorStates[1]);
        Assert.Equal("nutrition", inspectorStates[2]);
        Assert.Equal("1", inspectorStates[3]);
        Assert.Equal("ingredients", inspectorStates[4]);
        Assert.Equal("1", inspectorStates[5]);
        Assert.Equal("method", inspectorStates[6]);
    }

    [Fact]
    public async Task Desktop_AislePilotStackedLayout_SingleMealDays_ShowVisibleMealPanelInStackedView()
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

        await page.EvaluateAsync(
            """
            () => {
                const setMealType = (mealType, isChecked) => {
                    const input = document.querySelector(`input[type="checkbox"][name="Request.SelectedMealTypes"][value="${mealType}"]`);
                    if (!(input instanceof HTMLInputElement)) {
                        return;
                    }

                    input.checked = isChecked;
                    input.dispatchEvent(new Event("change", { bubbles: true }));
                };

                setMealType("Breakfast", false);
                setMealType("Lunch", false);
                setMealType("Dinner", true);
            }
            """);

        var generateButton = page.Locator("form.aislepilot-form button[type='submit']:has-text('Generate weekly plan')");
        await generateButton.ScrollIntoViewIfNeededAsync();
        await generateButton.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var stackedToggle = page.Locator(".aislepilot-day-view-toggle").First;
        await stackedToggle.ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const carousel = document.querySelector("[data-day-card-carousel]");
                return carousel instanceof HTMLElement &&
                    carousel.getAttribute("data-day-stacked-mode") === "true" &&
                    carousel.getAttribute("data-day-reorder-mode") !== "true";
            }
            """);

        var singleMealMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const activeCard = document.querySelector("[data-day-card-slide][aria-hidden='false']:not([data-day-carousel-ghost='true'])");
                if (!(activeCard instanceof HTMLElement)) {
                    return [-1, -1, -1, -1];
                }

                const activePanel = activeCard.querySelector(".aislepilot-day-meal-panel[aria-hidden='false']");
                const mealTitle = activePanel instanceof HTMLElement
                    ? (activePanel.querySelector(".aislepilot-day-meal-title")?.textContent ?? "").trim()
                    : "";
                const detailsPanel = activePanel instanceof HTMLElement
                    ? activePanel.querySelector("[data-inline-details-panel]")
                    : null;
                const slider = activeCard.querySelector(".aislepilot-day-meal-slider");
                const sliderDisplay = slider instanceof HTMLElement
                    ? window.getComputedStyle(slider).display
                    : "";
                const tabCount = activeCard.querySelectorAll("[data-day-meal-tab]").length;

                return [
                    tabCount,
                    activeCard.getAttribute("data-day-card-expanded") === "true" ? 1 : 0,
                    sliderDisplay !== "none" ? 1 : 0,
                    mealTitle.length > 0 ? 1 : 0,
                    detailsPanel instanceof HTMLElement
                    && !detailsPanel.hasAttribute("hidden")
                    && detailsPanel.getAttribute("aria-hidden") !== "true"
                        ? 1
                        : 0
                ];
            }
            """);

        Assert.Equal(5, singleMealMetrics.Length);
        Assert.Equal(0, Convert.ToInt32(singleMealMetrics[0]));
        Assert.Equal(1, Convert.ToInt32(singleMealMetrics[1]));
        Assert.Equal(1, Convert.ToInt32(singleMealMetrics[2]));
        Assert.Equal(1, Convert.ToInt32(singleMealMetrics[3]));
        Assert.Equal(1, Convert.ToInt32(singleMealMetrics[4]));
    }
}
