using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
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

        var shopJump = page.Locator(".aislepilot-window-tab[data-window-tab='aislepilot-shop']").First;
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

        var exportJump = page.Locator(".aislepilot-window-tab[data-window-tab='aislepilot-export']").First;
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
