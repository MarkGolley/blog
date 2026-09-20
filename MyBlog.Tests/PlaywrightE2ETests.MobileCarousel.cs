using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests : IAsyncLifetime
{
    [Fact]
    public async Task Mobile_AislePilotDayTabs_UseSingleScrollableRail()
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

        var tabLayout = await pagination.EvaluateAsync<object[]>(
            """
            paginationRoot => {
                if (!(paginationRoot instanceof HTMLElement)) {
                    return [1, "", 0];
                }

                const activePanel = document.querySelector(".aislepilot-window-panel[aria-hidden='false']");
                const tabs = Array.from(paginationRoot.querySelectorAll("[data-day-carousel-dot]"));
                const rows = new Set(tabs.map(tab => Math.round(tab.getBoundingClientRect().top)));
                return [
                    paginationRoot.scrollWidth - paginationRoot.clientWidth,
                    activePanel instanceof HTMLElement ? (activePanel.id || "") : "",
                    rows.size
                ];
            }
            """);
        Assert.Equal(3, tabLayout.Length);
        Assert.True(Convert.ToDouble(tabLayout[0]) > 1d, "Expected the seven day tabs to use a horizontally scrollable rail.");
        Assert.Equal("aislepilot-meals", Convert.ToString(tabLayout[1]));
        Assert.Equal(1, Convert.ToInt32(tabLayout[2]));
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
    public async Task Mobile_AislePilotDayTabs_KeyboardWrapsAcrossWeekBoundaryWithoutArrows()
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
                return status instanceof HTMLElement &&
                    /Sunday/i.test(status.textContent || "");
            }
            """);

        Assert.Equal(0, await page.Locator("[data-day-carousel-prev], [data-day-carousel-next]").CountAsync());
        await sundayPill.FocusAsync();
        await sundayPill.PressAsync("ArrowRight");

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
    public async Task Mobile_AislePilotDayChange_CollapsesRecipeFromPreviousDay()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);

        var firstDay = page.Locator("[data-day-card-slide]:not([data-day-carousel-ghost='true'])").First;
        var recipe = firstDay.Locator("[data-recipe-details-trigger]").First;
        await recipe.ClickAsync();
        Assert.Equal("true", await recipe.GetAttributeAsync("aria-expanded"));

        await page.Locator("[data-day-carousel-dot][data-day-carousel-target='1']").First.ClickAsync();
        await page.WaitForFunctionAsync("""
            () => document.querySelector("[data-day-carousel-dot][data-day-carousel-target='1']")?.getAttribute("aria-selected") === "true"
                && document.querySelector("[data-day-card-slide]:not([data-day-carousel-ghost='true']) [data-recipe-details-trigger]")?.getAttribute("aria-expanded") === "false"
        """);

        Assert.Equal("false", await recipe.GetAttributeAsync("aria-expanded"));
        Assert.False(await firstDay.Locator("[data-inline-details-panel]").First.IsVisibleAsync());
    }
}
