using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotCarousel_UsesConsistentListTypographyAcrossMacrosAndMethod()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();

        await GoToAislePilotAndGeneratePlanAsync(page);

        var viewToggle = page.Locator("[data-day-view-toggle]").First;
        await viewToggle.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var stackedModeEnabled = await page.EvaluateAsync<bool>(
            """
            () => {
                const carousel = document.querySelector("[data-day-card-carousel]");
                return carousel instanceof HTMLElement && carousel.dataset.dayStackedMode === "true";
            }
            """);

        if (stackedModeEnabled)
        {
            await viewToggle.ClickAsync();
            await page.WaitForFunctionAsync(
                """
                () => {
                    const carousel = document.querySelector("[data-day-card-carousel]");
                    return carousel instanceof HTMLElement && carousel.dataset.dayStackedMode !== "true";
                }
                """,
                null,
                new PageWaitForFunctionOptions { Timeout = 10000 });
        }

        var activeMealPanel = page.Locator(".aislepilot-day-meal-panel[aria-hidden='false']").First;
        await activeMealPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var viewSummaryButton = activeMealPanel.Locator(".aislepilot-meal-details-image-toggle > summary").First;
        var detailsPanel = activeMealPanel.Locator("[data-inline-details-panel]").First;
        await viewSummaryButton.ScrollIntoViewIfNeededAsync();
        await viewSummaryButton.ClickAsync();
        await detailsPanel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var nutritionSection = activeMealPanel.Locator("[data-meal-section='nutrition']").First;
        var methodSection = activeMealPanel.Locator("[data-meal-section='method']").First;
        var nutritionSummary = nutritionSection.Locator("[data-meal-section-summary]").First;
        var methodSummary = methodSection.Locator("[data-meal-section-summary]").First;
        var nutritionFirstItem = nutritionSection.Locator(".aislepilot-nutrition-list li").First;
        var methodFirstItem = methodSection.Locator("ol.case-list li").First;

        if (!await nutritionFirstItem.IsVisibleAsync())
        {
            await nutritionSummary.ScrollIntoViewIfNeededAsync();
            await nutritionSummary.ClickAsync();
            await nutritionFirstItem.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10000
            });
        }

        if (!await methodFirstItem.IsVisibleAsync())
        {
            await methodSummary.ScrollIntoViewIfNeededAsync();
            await methodSummary.ClickAsync();
            await methodFirstItem.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10000
            });
        }

        var nutritionTypography = await nutritionFirstItem.EvaluateAsync<string[]>(
            """
            element => {
                const style = getComputedStyle(element);
                return [style.fontFamily, style.fontSize, style.fontWeight, style.lineHeight, style.letterSpacing];
            }
            """);

        var methodTypography = await methodFirstItem.EvaluateAsync<string[]>(
            """
            element => {
                const style = getComputedStyle(element);
                return [style.fontFamily, style.fontSize, style.fontWeight, style.lineHeight, style.letterSpacing];
            }
            """);

        Assert.Equal(5, nutritionTypography.Length);
        Assert.Equal(5, methodTypography.Length);
        Assert.Equal(nutritionTypography[0], methodTypography[0]);
        Assert.Equal(nutritionTypography[1], methodTypography[1]);
        Assert.Equal(nutritionTypography[2], methodTypography[2]);
        Assert.Equal(nutritionTypography[3], methodTypography[3]);
        Assert.Equal(nutritionTypography[4], methodTypography[4]);
    }
}
