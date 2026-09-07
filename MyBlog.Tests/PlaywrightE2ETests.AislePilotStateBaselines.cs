using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Theory]
    [InlineData(390, 844, "mobile")]
    [InlineData(1440, 900, "desktop")]
    public async Task AislePilot_CapturesCoreWorkflowStateBaselines(
        int viewportWidth,
        int viewportHeight,
        string profile)
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        if (_browser is null || _appHost is null)
        {
            throw new InvalidOperationException("Playwright browser is not initialized.");
        }

        foreach (var theme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ColorScheme = theme,
                ReducedMotion = ReducedMotion.Reduce,
                ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
            });
            var page = await context.NewPageAsync();
            var themeName = theme.ToString().ToLowerInvariant();

            await GoToAislePilotSetupAsync(page);
            await WriteAislePilotStateScreenshotAsync(page, $"setup-{profile}-{themeName}");

            var appMenu = page.Locator("[data-head-menu]");
            await appMenu.Locator("summary").ClickAsync();
            await appMenu.Locator(".aislepilot-head-menu-panel").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5000
            });
            Assert.Contains("No saved weeks yet", await appMenu.InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains("No saved meals yet", await appMenu.InnerTextAsync(), StringComparison.Ordinal);
            await WriteAislePilotStateScreenshotAsync(page, $"empty-data-{profile}-{themeName}");
            await appMenu.Locator("summary").ClickAsync();

            var selectedMealTypes = page.Locator(
                "input[type='checkbox'][name='Request.SelectedMealTypes']:checked");
            while (await selectedMealTypes.CountAsync() > 0)
            {
                await selectedMealTypes.First.UncheckAsync(new LocatorUncheckOptions { Force = true });
            }

            var validationSubmit = GetAislePilotGenerateButton(page);
            await validationSubmit.ClickAsync(new LocatorClickOptions { Force = true });
            await page.Locator("[data-validation-summary]").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000
            });
            await WriteAislePilotStateScreenshotAsync(page, $"validation-{profile}-{themeName}");

            await context.ClearCookiesAsync();
            await GoToAislePilotSetupAsync(page);
            await page.EvaluateAsync(
                """
                () => {
                    if (!window.AislePilotCore?.showPlanLoadingShell) {
                        throw new Error("AislePilot loading-shell control is unavailable.");
                    }
                    window.AislePilotCore.showPlanLoadingShell();
                }
                """);
            var loadingShell = page.Locator("[data-plan-loading-shell]:not([hidden])");
            await loadingShell.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5000
            });
            await WriteAislePilotStateScreenshotAsync(
                page,
                $"loading-{profile}-{themeName}",
                fullPage: false);
            await page.EvaluateAsync("() => window.AislePilotCore.hidePlanLoadingShell()");

            var generateButton = GetAislePilotGenerateButton(page);
            await generateButton.ClickAsync(new LocatorClickOptions { Force = true });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.Locator("#aislepilot-meals[aria-hidden='false']").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000
            });
            await WriteAislePilotStateScreenshotAsync(page, $"results-{profile}-{themeName}");

            await page.Locator("#aislepilot-tab-shop").First.ClickAsync();
            await page.Locator("#aislepilot-shop[aria-hidden='false']").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5000
            });
            await WriteAislePilotStateScreenshotAsync(page, $"shopping-{profile}-{themeName}");

            await page.Locator("#aislepilot-tab-export").First.ClickAsync();
            await page.Locator("#aislepilot-export[aria-hidden='false']").WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5000
            });
            await WriteAislePilotStateScreenshotAsync(page, $"exports-{profile}-{themeName}");
        }
    }

    [Theory]
    [InlineData(390, 844, "mobile")]
    [InlineData(1440, 900, "desktop")]
    public async Task AislePilot_CapturesRateLimitResponseBaseline(
        int viewportWidth,
        int viewportHeight,
        string profile)
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        if (_browser is null)
        {
            throw new InvalidOperationException("Playwright browser is not initialized.");
        }

        await using var rateLimitedHost = await LocalAppHost.StartAsync(
            new Dictionary<string, string?>
            {
                ["AislePilot__EnableAiGeneration"] = "false",
                ["AislePilot__AllowTemplateFallback"] = "true",
                ["RateLimiting__AislePilotPermitLimit"] = "1",
                ["Observability__EnableOtlp"] = "false",
                ["Observability__LocalRunLogs__Enabled"] = "false"
            });

        await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ColorScheme = ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(
            $"{rateLimitedHost.BaseUrl}/projects/aisle-pilot",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await GetAislePilotGenerateButton(page).ClickAsync(new LocatorClickOptions { Force = true });
        await page.Locator("#aislepilot-meals[aria-hidden='false']").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        await page.GotoAsync(
            $"{rateLimitedHost.BaseUrl}/projects/aisle-pilot",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        var rateLimitResponseTask = page.WaitForResponseAsync(response =>
            response.Status == 429 &&
            response.Url.Contains("/projects/aisle-pilot", StringComparison.OrdinalIgnoreCase));
        await GetAislePilotGenerateButton(page).ClickAsync(new LocatorClickOptions { Force = true });
        var rateLimitResponse = await rateLimitResponseTask;

        Assert.Equal(429, rateLimitResponse.Status);
        Assert.Contains("Too many requests", await page.Locator("body").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        await WriteAislePilotStateScreenshotAsync(page, $"rate-limit-{profile}-light");
    }

    private async Task GoToAislePilotSetupAsync(IPage page)
    {
        if (_appHost is null)
        {
            throw new InvalidOperationException("App host is not initialized.");
        }

        var response = await page.GotoAsync(
            $"{_appHost.BaseUrl}/projects/aisle-pilot",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        Assert.NotNull(response);
        await GetAislePilotGenerateButton(page).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
    }

    private static ILocator GetAislePilotGenerateButton(IPage page) =>
        page.Locator(
            "form.aislepilot-form button[data-mobile-setup-submit='planner']:visible, " +
            "form.aislepilot-form button[data-setup-mode-submit='planner']:visible").First;

    private static async Task WriteAislePilotStateScreenshotAsync(
        IPage page,
        string artifactStem,
        bool fullPage = true)
    {
        var artifactRoot = Environment.GetEnvironmentVariable("PUBLIC_UI_ARTIFACT_ROOT");
        if (string.IsNullOrWhiteSpace(artifactRoot))
        {
            artifactRoot = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "artifacts", "public-ui"));
        }

        var stateRoot = Path.Combine(artifactRoot, "aislepilot-states");
        Directory.CreateDirectory(stateRoot);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = fullPage,
            Path = Path.Combine(stateRoot, $"{artifactStem}.png")
        });
    }
}
