using System.Text.Json;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    private static readonly IReadOnlyList<PublicUiRoute> PublicUiRoutes =
    [
        new("home", "/", 15, 11),
        new("projects", "/projects", 9, 5),
        new("articles", "/blog", 49, 42),
        new("about", "/about", 9, 5),
        new("contact", "/contact", 15, 11),
        new("aislepilot-setup", "/projects/aisle-pilot", 42, 26),
        new("not-found", "/public-ui-audit/not-found", 0, 0, EnforceDocumentStructure: false),
        new("server-error", "/home/error", 8, 5, EnforceDocumentStructure: false)
    ];

    private static readonly IReadOnlyList<PublicUiAxeException> PublicUiAxeExceptions =
    [
        new("not-found", "document-title", "Public shell", new DateOnly(2026, 9, 30),
            "The framework-generated 404 response has no document title until the Phase 2 branded error page replaces it."),
        new("not-found", "html-has-lang", "Public shell", new DateOnly(2026, 9, 30),
            "The framework-generated 404 response has no language attribute until the Phase 2 branded error page replaces it.")
    ];

    [Theory]
    [InlineData(360, 800, "small-mobile")]
    [InlineData(390, 844, "mobile")]
    [InlineData(768, 1024, "tablet")]
    [InlineData(1440, 900, "desktop")]
    [InlineData(1440, 1100, "tall-desktop")]
    public async Task PublicUiRoutes_CaptureThemeBaselinesAndRejectGeometryRegressions(
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

        var mediaVariants = new[]
        {
            new PublicUiMediaVariant("normal", ColorScheme.Light, ReducedMotion.NoPreference),
            new PublicUiMediaVariant("normal", ColorScheme.Dark, ReducedMotion.NoPreference),
            new PublicUiMediaVariant("reduced", ColorScheme.Light, ReducedMotion.Reduce),
            new PublicUiMediaVariant("reduced", ColorScheme.Dark, ReducedMotion.Reduce)
        };

        foreach (var media in mediaVariants)
        {
            await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ColorScheme = media.Theme,
                ReducedMotion = media.ReducedMotion,
                ViewportSize = new ViewportSize
                {
                    Width = viewportWidth,
                    Height = viewportHeight
                }
            });
            var page = await context.NewPageAsync();
            var routes = await ResolvePublicUiRoutesAsync(page, _appHost.BaseUrl);

            foreach (var route in routes)
            {
                var response = await page.GotoAsync(
                    $"{_appHost.BaseUrl}{route.Path}",
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                Assert.NotNull(response);

                await page.EmulateMediaAsync(new PageEmulateMediaOptions
                {
                    ColorScheme = media.Theme,
                    ReducedMotion = media.ReducedMotion
                });
                await page.WaitForTimeoutAsync(100);

                var audit = await ReadPublicUiAuditAsync(page);
                var themeName = media.Theme.ToString().ToLowerInvariant();
                var artifactStem = $"{route.Name}-{profile}-{themeName}-{media.MotionName}";
                await WritePublicUiArtifactsAsync(page, artifactStem, audit);

                Assert.False(
                    audit.HorizontalOverflow,
                    $"{artifactStem} overflowed horizontally. Viewport={audit.ViewportWidth}px, Document={audit.DocumentWidth}px.");

                if (route.EnforceDocumentStructure)
                {
                    Assert.Equal(1, audit.H1Count);
                    Assert.True(audit.MainCount >= 1, $"{artifactStem} did not expose a main landmark.");
                }

                var allowedSmallTargets = viewportWidth < 768
                    ? route.MobileSmallTargetBaseline
                    : route.DesktopSmallTargetBaseline;
                Assert.True(
                    audit.SmallTargets.Count <= allowedSmallTargets,
                    $"{artifactStem} increased the known small-target baseline. " +
                    $"Allowed={allowedSmallTargets}, Actual={audit.SmallTargets.Count}. " +
                    $"Targets={string.Join(" | ", audit.SmallTargets.Select(target => target.ToString()))}");
            }
        }
    }

    [Theory]
    [InlineData(390, 844, "mobile")]
    [InlineData(1440, 900, "desktop")]
    public async Task PublicUiRoutes_HaveNoSeriousOrCriticalAxeViolations(
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
            var routes = await ResolvePublicUiRoutesAsync(page, _appHost.BaseUrl);

            foreach (var route in routes)
            {
                var response = await page.GotoAsync(
                    $"{_appHost.BaseUrl}{route.Path}",
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                Assert.NotNull(response);
                await page.EmulateMediaAsync(new PageEmulateMediaOptions
                {
                    ColorScheme = theme,
                    ReducedMotion = ReducedMotion.Reduce
                });

                var axeResult = await page.RunAxe();
                var blockingViolations = axeResult.Violations
                    .Where(violation =>
                        violation.Impact.Equals("serious", StringComparison.OrdinalIgnoreCase) ||
                        violation.Impact.Equals("critical", StringComparison.OrdinalIgnoreCase))
                    .Where(violation => !IsCurrentAxeException(route.Name, violation.Id))
                    .ToList();
                var auditName = $"{route.Name}-{profile}-{theme.ToString().ToLowerInvariant()}";

                Assert.True(
                    blockingViolations.Count == 0,
                    $"{auditName} has serious or critical axe violations: " +
                    string.Join(" | ", blockingViolations.Select(violation =>
                        $"{violation.Id} ({violation.Impact}): " +
                        string.Join(", ", violation.Nodes.Select(node => node.Target.ToString())))));
            }
        }
    }

    private static bool IsCurrentAxeException(string routeName, string ruleId)
    {
        var matchingException = PublicUiAxeExceptions.FirstOrDefault(exception =>
            exception.RouteName.Equals(routeName, StringComparison.OrdinalIgnoreCase) &&
            exception.RuleId.Equals(ruleId, StringComparison.OrdinalIgnoreCase));
        if (matchingException is null)
        {
            return false;
        }

        Assert.True(
            matchingException.ExpiresOn >= DateOnly.FromDateTime(DateTime.UtcNow),
            $"Expired axe exception: route={routeName}, rule={ruleId}, owner={matchingException.Owner}, " +
            $"expired={matchingException.ExpiresOn:yyyy-MM-dd}. {matchingException.Reason}");
        return true;
    }

    private static async Task<IReadOnlyList<PublicUiRoute>> ResolvePublicUiRoutesAsync(IPage page, string baseUrl)
    {
        var routes = PublicUiRoutes.ToList();
        await page.GotoAsync($"{baseUrl}/blog", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var firstArticlePath = await page.Locator("main .post-title-link").First.GetAttributeAsync("href");
        if (!string.IsNullOrWhiteSpace(firstArticlePath))
        {
            routes.Insert(3, new PublicUiRoute("article", firstArticlePath, 50, 32));
        }

        return routes;
    }

    private static async Task<PublicUiAudit> ReadPublicUiAuditAsync(IPage page)
    {
        var auditJson = await page.EvaluateAsync<string>(
            """
            () => {
                const isVisible = element => {
                    const rect = element.getBoundingClientRect();
                    const style = getComputedStyle(element);
                    return rect.width > 0 &&
                        rect.height > 0 &&
                        style.display !== "none" &&
                        style.visibility !== "hidden" &&
                        style.opacity !== "0" &&
                        style.pointerEvents !== "none";
                };
                const describe = element =>
                    (element.getAttribute("aria-label") ||
                        element.textContent ||
                        element.getAttribute("name") ||
                        element.id ||
                        element.tagName)
                    .trim()
                    .replace(/\s+/g, " ")
                    .slice(0, 80);
                const interactive = Array.from(document.querySelectorAll(
                    "a[href],button,input:not([type='hidden']),select,textarea,summary,[role='button'],[role='tab']"))
                    .filter(isVisible)
                    .filter(element => element.tabIndex >= 0 && element.getAttribute("aria-hidden") !== "true");
                const targets = interactive.map(element => {
                    const rect = element.getBoundingClientRect();
                    return {
                        name: describe(element),
                        left: rect.left,
                        top: rect.top,
                        right: rect.right,
                        bottom: rect.bottom,
                        width: rect.width,
                        height: rect.height
                    };
                });
                const smallTargets = targets
                    .filter(target => target.width < 44 || target.height < 44)
                    .map(target => ({
                        name: target.name,
                        width: Math.round(target.width),
                        height: Math.round(target.height)
                    }));
                let closeTargetPairs = 0;
                for (let firstIndex = 0; firstIndex < targets.length; firstIndex++) {
                    for (let secondIndex = firstIndex + 1; secondIndex < targets.length; secondIndex++) {
                        const first = targets[firstIndex];
                        const second = targets[secondIndex];
                        const horizontalGap = Math.max(0, Math.max(first.left, second.left) - Math.min(first.right, second.right));
                        const verticalGap = Math.max(0, Math.max(first.top, second.top) - Math.min(first.bottom, second.bottom));
                        const overlapsHorizontally = first.left < second.right && second.left < first.right;
                        const overlapsVertically = first.top < second.bottom && second.top < first.bottom;
                        if ((overlapsVertically && horizontalGap > 0 && horizontalGap < 8) ||
                            (overlapsHorizontally && verticalGap > 0 && verticalGap < 8)) {
                            closeTargetPairs++;
                        }
                    }
                }
                const potentiallyObscuringElements = Array.from(document.querySelectorAll("body *"))
                    .filter(isVisible)
                    .filter(element => {
                        const position = getComputedStyle(element).position;
                        return position === "fixed" || position === "sticky";
                    })
                    .map(element => ({
                        name: describe(element),
                        position: getComputedStyle(element).position
                    }));

                return JSON.stringify({
                    viewportWidth: window.innerWidth,
                    documentWidth: document.documentElement.scrollWidth,
                    horizontalOverflow: document.documentElement.scrollWidth > window.innerWidth,
                    h1Count: document.querySelectorAll("h1").length,
                    mainCount: document.querySelectorAll("main,[role='main']").length,
                    visibleInteractiveCount: targets.length,
                    smallTargets,
                    closeTargetPairs,
                    potentiallyObscuringElements
                });
            }
            """);

        return JsonSerializer.Deserialize<PublicUiAudit>(
                   auditJson,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidOperationException("Could not parse the public UI audit payload.");
    }

    private static async Task WritePublicUiArtifactsAsync(
        IPage page,
        string artifactStem,
        PublicUiAudit audit)
    {
        var artifactRoot = Environment.GetEnvironmentVariable("PUBLIC_UI_ARTIFACT_ROOT");
        if (string.IsNullOrWhiteSpace(artifactRoot))
        {
            artifactRoot = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "artifacts", "public-ui"));
        }

        Directory.CreateDirectory(artifactRoot);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = true,
            Path = Path.Combine(artifactRoot, $"{artifactStem}.png")
        });
        await File.WriteAllTextAsync(
            Path.Combine(artifactRoot, $"{artifactStem}.json"),
            JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record PublicUiRoute(
        string Name,
        string Path,
        int DesktopSmallTargetBaseline,
        int MobileSmallTargetBaseline,
        bool EnforceDocumentStructure = true);

    private sealed record PublicUiMediaVariant(
        string MotionName,
        ColorScheme Theme,
        ReducedMotion ReducedMotion);

    private sealed record PublicUiAxeException(
        string RouteName,
        string RuleId,
        string Owner,
        DateOnly ExpiresOn,
        string Reason);

    private sealed record PublicUiAudit
    {
        public int ViewportWidth { get; init; }
        public int DocumentWidth { get; init; }
        public bool HorizontalOverflow { get; init; }
        public int H1Count { get; init; }
        public int MainCount { get; init; }
        public int VisibleInteractiveCount { get; init; }
        public IReadOnlyList<PublicUiSmallTarget> SmallTargets { get; init; } = [];
        public int CloseTargetPairs { get; init; }
        public IReadOnlyList<PublicUiPositionedElement> PotentiallyObscuringElements { get; init; } = [];
    }

    private sealed record PublicUiSmallTarget(string Name, int Width, int Height)
    {
        public override string ToString() => $"{Name} ({Width}x{Height})";
    }

    private sealed record PublicUiPositionedElement(string Name, string Position);
}
