using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotControls_UseAlignedThemeConsistentSurfaces()
    {
        if (!IsE2EEnabled()) return;

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotSetupAsync(page);

        await page.Locator(".aislepilot-personalise > summary").First.ClickAsync();
        var setupMetrics = await page.EvaluateAsync<double[]>(
            """
            () => {
                const lockup = document.querySelector('.aislepilot-brand-lockup');
                const menu = document.querySelector('.aislepilot-head-menu-trigger');
                const disclosure = document.querySelector('.aislepilot-collapsible > summary');
                const portion = document.querySelector('.aislepilot-personalise-content .aislepilot-slider-field');
                if (!(lockup instanceof HTMLElement) || !(menu instanceof HTMLElement)
                    || !(disclosure instanceof HTMLElement) || !(portion instanceof HTMLElement)) return [-1, -1, -1, -1, -1];
                const lockupRect = lockup.getBoundingClientRect();
                const menuRect = menu.getBoundingClientRect();
                const disclosureArrow = getComputedStyle(disclosure, '::after');
                const mutedProbe = document.createElement('span');
                mutedProbe.style.color = 'var(--ap-refresh-text-muted)';
                disclosure.appendChild(mutedProbe);
                const mutedColour = getComputedStyle(mutedProbe).color;
                mutedProbe.remove();
                const portionStyle = getComputedStyle(portion);
                return [
                    Math.abs((lockupRect.top + lockupRect.height / 2) - (menuRect.top + menuRect.height / 2)),
                    lockupRect.right - menuRect.right,
                    disclosureArrow.borderRightColor === mutedColour ? 1 : 0,
                    Number.parseFloat(portionStyle.borderTopWidth || '0'),
                    Number.parseFloat(portionStyle.borderTopLeftRadius || '0')
                ];
            }
            """);

        Assert.InRange(setupMetrics[0], 0, 1.5);
        Assert.InRange(setupMetrics[1], 5, 10);
        Assert.Equal(1, setupMetrics[2]);
        Assert.Equal(0, setupMetrics[3]);
        Assert.Equal(0, setupMetrics[4]);

        await GoToAislePilotAndGeneratePlanAsync(page);
        var actions = page.Locator("[data-overview-actions-menu]");
        await actions.Locator("summary").ClickAsync();
        var menuMetrics = await actions.Locator(".aislepilot-overview-actions-menu").EvaluateAsync<double[]>(
            """
            panel => {
                const style = getComputedStyle(panel);
                const button = panel.querySelector('.aislepilot-overview-regenerate-btn');
                const buttonStyle = button instanceof HTMLElement ? getComputedStyle(button) : null;
                return [
                    style.backgroundImage === 'none' ? 1 : 0,
                    style.backgroundColor === 'rgba(0, 0, 0, 0)' ? 0 : 1,
                    buttonStyle && buttonStyle.backgroundColor !== 'rgba(0, 0, 0, 0)' ? 1 : 0
                ];
            }
            """);
        Assert.Equal(new double[] { 1, 1, 1 }, menuMetrics);
    }
}
