using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Desktop_AislePilotSettingsToggle_StacksSnapshotBelowSetupWhileEditing()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateDesktopContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var setupToggle = page.Locator("[data-setup-toggle]").First;
        await setupToggle.ClickAsync();

        await page.WaitForFunctionAsync(
            """
            () => {
                const layout = document.querySelector(".aislepilot-layout.has-results");
                const setup = document.querySelector("#aislepilot-setup");
                const overview = document.querySelector("#aislepilot-overview");
                const workspace = setup?.querySelector(".aislepilot-setup-workspace");
                const primary = setup?.querySelector(".aislepilot-setup-primary");
                const summary = setup?.querySelector(".aislepilot-setup-summary");
                if (!(layout instanceof HTMLElement)
                    || !(setup instanceof HTMLElement)
                    || !(overview instanceof HTMLElement)
                    || !(workspace instanceof HTMLElement)
                    || !(primary instanceof HTMLElement)
                    || !(summary instanceof HTMLElement)) {
                    return false;
                }

                if (setup.hasAttribute("hidden")) {
                    return false;
                }

                const layoutColumns = (window.getComputedStyle(layout).gridTemplateColumns || "")
                    .split(/\s+/)
                    .map(value => value.trim())
                    .filter(value => value.length > 0);
                const workspaceColumns = (window.getComputedStyle(workspace).gridTemplateColumns || "")
                    .split(/\s+/)
                    .map(value => value.trim())
                    .filter(value => value.length > 0);
                const setupRect = setup.getBoundingClientRect();
                const overviewRect = overview.getBoundingClientRect();
                const primaryRect = primary.getBoundingClientRect();
                const summaryRect = summary.getBoundingClientRect();

                return layout.classList.contains("has-visible-setup")
                    && layoutColumns.length === 1
                    && workspaceColumns.length === 1
                    && summaryRect.top >= primaryRect.bottom + 8
                    && Math.abs(summaryRect.left - primaryRect.left) <= 8
                    && overviewRect.top >= setupRect.bottom + 8
                    && Math.abs(overviewRect.left - setupRect.left) <= 8;
            }
            """,
            null,
            new PageWaitForFunctionOptions
            {
                Timeout = 10000
            });

        var layoutMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const layout = document.querySelector(".aislepilot-layout.has-results");
                const setup = document.querySelector("#aislepilot-setup");
                const overview = document.querySelector("#aislepilot-overview");
                const workspace = setup?.querySelector(".aislepilot-setup-workspace");
                const primary = setup?.querySelector(".aislepilot-setup-primary");
                const summary = setup?.querySelector(".aislepilot-setup-summary");
                if (!(layout instanceof HTMLElement)
                    || !(setup instanceof HTMLElement)
                    || !(overview instanceof HTMLElement)
                    || !(workspace instanceof HTMLElement)
                    || !(primary instanceof HTMLElement)
                    || !(summary instanceof HTMLElement)) {
                    return [-1, -1, -1, -1, -1, -1, -1];
                }

                const layoutColumns = (window.getComputedStyle(layout).gridTemplateColumns || "")
                    .split(/\s+/)
                    .map(value => value.trim())
                    .filter(value => value.length > 0);
                const workspaceColumns = (window.getComputedStyle(workspace).gridTemplateColumns || "")
                    .split(/\s+/)
                    .map(value => value.trim())
                    .filter(value => value.length > 0);
                const setupRect = setup.getBoundingClientRect();
                const overviewRect = overview.getBoundingClientRect();
                const primaryRect = primary.getBoundingClientRect();
                const summaryRect = summary.getBoundingClientRect();

                return [
                    layoutColumns.length,
                    workspaceColumns.length,
                    summaryRect.top - primaryRect.bottom,
                    Math.abs(summaryRect.left - primaryRect.left),
                    overviewRect.top - setupRect.bottom,
                    Math.abs(overviewRect.left - setupRect.left),
                    layout.classList.contains("has-visible-setup") ? 1 : 0
                ];
            }
            """);

        Assert.Equal(7, layoutMetrics.Length);
        Assert.Equal(1, layoutMetrics[0]);
        Assert.Equal(1, layoutMetrics[1]);
        Assert.True(layoutMetrics[2] >= 8, $"Expected setup summary to sit below the controls while editing. Gap={layoutMetrics[2]:F1}px.");
        Assert.True(layoutMetrics[3] <= 8, $"Expected setup summary to align with the controls column. Left delta={layoutMetrics[3]:F1}px.");
        Assert.True(layoutMetrics[4] >= 8, $"Expected overview to stack below settings while editing. Gap={layoutMetrics[4]:F1}px.");
        Assert.True(layoutMetrics[5] <= 8, $"Expected stacked sections to align on the same column. Left delta={layoutMetrics[5]:F1}px.");
        Assert.Equal(1, layoutMetrics[6]);
    }
}
