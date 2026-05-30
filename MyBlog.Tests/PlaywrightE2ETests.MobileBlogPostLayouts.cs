using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task NarrowMobile_BlogPostCodeBlocks_DoNotCauseHorizontalPageOverflow()
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

        await page.GotoAsync($"{_appHost.BaseUrl}/blog/Why_AI_Permission_Popups_Matter");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("#post-title").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var codeBlockMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const doc = document.documentElement;
                const body = document.body;
                const scrollWidth = Math.max(doc?.scrollWidth ?? 0, body?.scrollWidth ?? 0);
                const pageOverflowX = Math.max(0, scrollWidth - window.innerWidth);

                const codeBlock = document.querySelector(".post-content pre");
                if (!(codeBlock instanceof HTMLElement)) {
                    return [0, pageOverflowX, Number.POSITIVE_INFINITY, 0, Number.POSITIVE_INFINITY];
                }

                const rect = codeBlock.getBoundingClientRect();
                const padding = 4;
                const blockOverflowLeft = Math.max(0, padding - rect.left);
                const blockOverflowRight = Math.max(0, rect.right - (window.innerWidth - padding));
                const blockOverflowX = Math.max(blockOverflowLeft, blockOverflowRight);
                const overflowX = window.getComputedStyle(codeBlock).overflowX;
                const allowsHorizontalScroll = overflowX === "auto" || overflowX === "scroll" ? 1 : 0;
                const internalOverflowX = Math.max(0, codeBlock.scrollWidth - codeBlock.clientWidth);

                return [1, pageOverflowX, blockOverflowX, allowsHorizontalScroll, internalOverflowX];
            }
            """);

        Assert.Equal(5, codeBlockMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(codeBlockMetrics[0]));
        Assert.True(codeBlockMetrics[1] <= 1.5, $"Expected blog post code blocks not to introduce page horizontal overflow. Overflow={codeBlockMetrics[1]:F1}px.");
        Assert.True(codeBlockMetrics[2] <= 1.5, $"Expected code block container to stay inside narrow mobile viewport. Overflow={codeBlockMetrics[2]:F1}px.");
        Assert.Equal(1, Convert.ToInt32(codeBlockMetrics[3]));
        Assert.True(codeBlockMetrics[4] >= 0, $"Expected code block internal overflow metric to be measured. Value={codeBlockMetrics[4]:F1}px.");
    }
}
