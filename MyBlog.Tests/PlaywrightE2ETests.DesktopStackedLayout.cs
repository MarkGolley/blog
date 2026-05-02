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
}
