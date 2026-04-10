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
}
