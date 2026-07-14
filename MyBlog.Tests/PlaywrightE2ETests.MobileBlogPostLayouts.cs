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

    [Fact]
    public async Task NarrowMobile_BlogPostJumpLinks_LandBelowStickyHeader()
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

        await page.GotoAsync($"{_appHost.BaseUrl}/blog/How_AI_Agents_Actually_Work");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator(".post-jump-nav a[href='#agent-context-and-tools']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        await page.Locator(".post-jump-nav a[href='#agent-context-and-tools']").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => window.location.hash === '#agent-context-and-tools'",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });
        await page.WaitForTimeoutAsync(150);

        var clearanceMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const header = document.querySelector(".site-header");
                const target = document.querySelector("#agent-context-and-tools");
                if (!(header instanceof HTMLElement) || !(target instanceof HTMLElement)) {
                    return [0, Number.NEGATIVE_INFINITY, Number.NEGATIVE_INFINITY];
                }

                const headerBottom = header.getBoundingClientRect().bottom;
                const targetTop = target.getBoundingClientRect().top;
                return [1, targetTop - headerBottom, window.scrollY];
            }
            """);

        Assert.Equal(3, clearanceMetrics.Length);
        Assert.Equal(1, Convert.ToInt32(clearanceMetrics[0]));
        Assert.True(
            clearanceMetrics[1] >= 8,
            $"Expected jump target to land below sticky header with breathing room. Clearance={clearanceMetrics[1]:F1}px.");
        Assert.True(clearanceMetrics[2] > 100, $"Expected jump link to scroll the post. ScrollY={clearanceMetrics[2]:F1}px.");
    }

    [Fact]
    public async Task NarrowMobile_BlogPostDiagrams_DoNotCauseHorizontalPageOverflow()
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

        await page.GotoAsync($"{_appHost.BaseUrl}/blog/How_AI_Agents_Actually_Work");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator(".post-diagram").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var diagramMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const doc = document.documentElement;
                const body = document.body;
                const scrollWidth = Math.max(doc?.scrollWidth ?? 0, body?.scrollWidth ?? 0);
                const pageOverflowX = Math.max(0, scrollWidth - window.innerWidth);
                const diagrams = Array.from(document.querySelectorAll(".post-diagram"));
                const padding = 4;
                const maxDiagramOverflowX = diagrams.reduce((max, diagram) => {
                    if (!(diagram instanceof HTMLElement)) {
                        return max;
                    }

                    const rect = diagram.getBoundingClientRect();
                    const overflowLeft = Math.max(0, padding - rect.left);
                    const overflowRight = Math.max(0, rect.right - (window.innerWidth - padding));
                    return Math.max(max, overflowLeft, overflowRight);
                }, 0);

                return [diagrams.length, pageOverflowX, maxDiagramOverflowX];
            }
            """);

        Assert.Equal(3, diagramMetrics.Length);
        Assert.True(diagramMetrics[0] >= 4, $"Expected the AI agents post to render multiple diagrams. Count={diagramMetrics[0]:F0}.");
        Assert.True(diagramMetrics[1] <= 1.5, $"Expected diagrams not to introduce page horizontal overflow. Overflow={diagramMetrics[1]:F1}px.");
        Assert.True(diagramMetrics[2] <= 1.5, $"Expected diagram containers to stay inside narrow mobile viewport. Overflow={diagramMetrics[2]:F1}px.");
    }
}
