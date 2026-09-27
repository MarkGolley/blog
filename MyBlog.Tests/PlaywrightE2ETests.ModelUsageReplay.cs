using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    private const string UsagePostPath = "/blog/Why_Does_My_AI_Allowance_Disappear_So_Quickly";

    [Theory]
    [InlineData(1440, "light")]
    [InlineData(390, "dark")]
    [InlineData(320, "light")]
    public async Task ModelUsageReplay_ControlsAndLayout(int width, string theme)
    {
        if (!IsE2EEnabled()) return;
        await using var context = await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(_appHost!.BaseUrl + UsagePostPath);
        await page.EvaluateAsync("theme => document.documentElement.dataset.theme = theme", theme);
        await page.Locator("[data-replay-controls]").WaitForAsync();
        Assert.Equal(9, await page.Locator(".model-replay tbody tr").CountAsync());
        await page.Locator("[data-finish]").ClickAsync();
        Assert.Equal("803", await page.Locator("[data-tokens]").First.InnerTextAsync());
        Assert.Equal("808", await page.Locator("[data-tokens]").Nth(1).InnerTextAsync());
        await page.Locator("[data-comparison]").SelectOptionAsync("effort");
        await page.Locator("[data-repetition]").SelectOptionAsync("3");
        await page.Locator("[data-finish]").ClickAsync();
        Assert.Equal("840", await page.Locator("[data-tokens]").First.InnerTextAsync());
        Assert.Equal("1,275", await page.Locator("[data-tokens]").Nth(1).InnerTextAsync());
        await page.Locator("[data-reset]").ClickAsync();
        Assert.Equal("Not reported yet", await page.Locator("[data-tokens]").First.InnerTextAsync());
        await page.Locator("[data-play]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-replay-status]")).ToContainTextAsync("Recording complete", new() { Timeout = 15000 });
        await page.Locator("[data-seek]").FocusAsync();
        await page.Keyboard.PressAsync("Home");
        Assert.Equal("Not reported yet", await page.Locator("[data-tokens]").First.InnerTextAsync());
        await Assertions.Expect(page.Locator("[data-replay-status]")).ToContainTextAsync("paused at 0.0");
        await page.Keyboard.PressAsync("End");
        Assert.Equal("1,275", await page.Locator("[data-tokens]").Nth(1).InnerTextAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ModelUsageReplay_FetchFailureKeepsStaticResults()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateDesktopContextAsync();
        await context.RouteAsync("**/experiments/model-usage/*.json", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync(_appHost!.BaseUrl + UsagePostPath);
        await Assertions.Expect(page.Locator("[data-replay-status]")).ToContainTextAsync("could not load");
        Assert.Equal(9, await page.Locator(".model-replay tbody tr").CountAsync());
        Assert.False(await page.Locator("[data-replay-controls]").IsVisibleAsync());
    }

    [Fact]
    public async Task ModelUsageReplay_NoJavascriptKeepsStaticResults()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await _browser!.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_appHost!.BaseUrl + UsagePostPath);
        Assert.Equal(9, await page.Locator(".model-replay tbody tr").CountAsync());
        Assert.False(await page.Locator("[data-replay-controls]").IsVisibleAsync());
    }
}
