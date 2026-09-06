using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotMealImagePolling_ShowsTerminalUnavailableState()
    {
        if (!IsE2EEnabled()) return;
        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotAndGeneratePlanAsync(page);
        Assert.True(await page.EvaluateAsync<bool>(
            """
            async () => {
                const root = document.querySelector("[data-meal-image-poll-root]");
                const image = root?.querySelector("img[data-meal-image][data-meal-name]");
                if (!(root instanceof HTMLElement) || !(image instanceof HTMLImageElement)) return false;
                root.dataset.mealImagePollEnabled = "true";
                image.src = root.dataset.fallbackMealImageUrl || "/projects/aisle-pilot/images/aislepilot-icon.svg";
                const originalFetch = window.fetch;
                window.fetch = async () => new Response(JSON.stringify({canGenerateImages:false,images:[]}), {status:200,headers:{"Content-Type":"application/json"}});
                const controller = window.AislePilotMealImagePolling?.createController({documentRef:document,maxAttempts:1});
                if (!controller) return false;
                await controller.pollOnce();
                window.fetch = originalFetch;
                return image.closest(".aislepilot-meal-image-shell")?.dataset.mealImageUnavailable === "true";
            }
            """));
        var shell = page.Locator(".aislepilot-meal-image-shell[data-meal-image-unavailable='true']").First;
        Assert.True(await shell.IsVisibleAsync());
        Assert.True(await shell.EvaluateAsync<bool>("el => getComputedStyle(el, '::after').content.includes('Photo unavailable')"));
        Assert.True(await page.Locator("[data-recipe-details-trigger]").First.IsEnabledAsync());
    }
}
