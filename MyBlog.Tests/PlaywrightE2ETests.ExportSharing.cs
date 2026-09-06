using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotShare_ReportsSuccessCancellationClipboardAndFailureAccurately()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);
        await page.Locator("[data-window-tab='aislepilot-export']").ClickAsync();
        var button = page.Locator("[data-notes-export-trigger]");
        await button.EvaluateAsync("element => element.dataset.notesLabelTimeoutMs = '800'");

        await SetShareMocksAsync(page, "success");
        await button.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('[data-notes-export-trigger]')?.textContent?.includes('Share sheet opened')");
        Assert.Equal(new[] { 1, 0 }, await ReadShareCountsAsync(page));
        await page.WaitForTimeoutAsync(850);

        await SetShareMocksAsync(page, "abort");
        await button.ClickAsync();
        await page.WaitForTimeoutAsync(100);
        Assert.Equal("Share shopping list", (await button.TextContentAsync())?.Trim());
        Assert.Equal(new[] { 1, 0 }, await ReadShareCountsAsync(page));

        await SetShareMocksAsync(page, "clipboard");
        await button.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('[data-notes-export-trigger]')?.textContent?.includes('copied')");
        Assert.Equal(new[] { 0, 1 }, await ReadShareCountsAsync(page));
        await page.WaitForTimeoutAsync(850);

        await SetShareMocksAsync(page, "failure");
        await button.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('[data-notes-export-trigger]')?.textContent?.includes('Could not share')");
        Assert.Equal(new[] { 1, 1 }, await ReadShareCountsAsync(page));
        await page.WaitForTimeoutAsync(850);
        Assert.Equal("Share shopping list", (await button.TextContentAsync())?.Trim());

        await page.Locator("[data-window-tab='aislepilot-shop']").ClickAsync();
        var firstItem = page.Locator("[data-shopping-department] [data-shopping-item-label]").First;
        var firstName = (await firstItem.Locator("[data-shopping-item-text]").TextContentAsync())?.Trim();
        await firstItem.ClickAsync();
        await page.Locator("[data-shopping-hide-checked]").CheckAsync();
        Assert.True(await firstItem.Locator("xpath=ancestor::li[1]").IsHiddenAsync());
        Assert.Contains(firstName!, await page.Locator("[data-notes-export-content]").InputValueAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SetShareMocksAsync(IPage page, string scenario)
    {
        await page.EvaluateAsync(
            """
            scenario => {
                window.__shareCalls = 0;
                window.__clipboardCalls = 0;
                const share = scenario === 'clipboard' ? undefined : async () => {
                    window.__shareCalls++;
                    if (scenario === 'abort') throw new DOMException('dismissed', 'AbortError');
                    if (scenario === 'failure') throw new Error('share failed');
                };
                Object.defineProperty(navigator, 'share', { configurable: true, value: share });
                Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
                    writeText: async () => {
                        window.__clipboardCalls++;
                        if (scenario === 'failure') throw new Error('clipboard failed');
                    }
                }});
                document.execCommand = () => false;
            }
            """,
            scenario);
    }

    private static Task<int[]> ReadShareCountsAsync(IPage page) =>
        page.EvaluateAsync<int[]>("() => [window.__shareCalls || 0, window.__clipboardCalls || 0]");
}
