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
                "Share shopping list (works with Notes)",
                "Download shopping checklist (.txt)",
                "Download full plan pack (.pdf)",
                "Print current view"
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
                    .find(candidate => candidate.textContent?.includes("Download shopping checklist"));
                return button instanceof HTMLButtonElement
                    && !button.disabled
                    && !button.classList.contains("is-loading")
                    && button.getAttribute("aria-busy") !== "true"
                    && button.textContent?.trim() === "Download shopping checklist (.txt)";
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
}
