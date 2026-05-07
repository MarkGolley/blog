using System.Diagnostics;
using System.Text;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task AislePilotScript_CombinedBundles_AreValidJavaScript()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var script = await GetCombinedAislePilotScriptAsync(client);

        var tempScriptPath = Path.Combine(
            Path.GetTempPath(),
            $"aislepilot-script-{Guid.NewGuid():N}.js");
        await File.WriteAllTextAsync(tempScriptPath, script, Encoding.UTF8);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"--check \"{tempScriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            var diagnostics = string.Join(
                Environment.NewLine,
                new[] { standardOutput, standardError }.Where(chunk => !string.IsNullOrWhiteSpace(chunk)));

            Assert.True(
                process.ExitCode == 0,
                string.IsNullOrWhiteSpace(diagnostics)
                    ? "Expected combined AislePilot bundles to parse as valid JavaScript."
                    : $"Expected combined AislePilot bundles to parse as valid JavaScript.{Environment.NewLine}{diagnostics}");
        }
        finally
        {
            if (File.Exists(tempScriptPath))
            {
                File.Delete(tempScriptPath);
            }
        }
    }

    [Fact]
    public async Task AislePilotScript_MealImagePolling_UsesFasterEarlyFollowupIntervals()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await client.GetStringAsync("/js/aisle-pilot/meal-image-polling.js");

        Assert.Contains("const fastFollowupPollIntervalMs = 750;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const mediumFollowupPollIntervalMs = 1500;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (pollAttempts <= 4)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("return fastFollowupPollIntervalMs;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (pollAttempts <= 12)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("return mediumFollowupPollIntervalMs;", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotScript_DayCardReorder_ForcesVisiblePreviewSlotInReorderMode()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await GetCombinedAislePilotScriptAsync(client);

        Assert.Contains("const applyDayReorderPreviewSlotsToScope = scope => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const applyDayReorderPreviewSlotToCard = card => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("panel.setAttribute(\"aria-hidden\", isVisible ? \"false\" : \"true\");", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (isDayReorderMode) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("applyDayReorderPreviewSlotsToScope(carousel);", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotScript_DayCardReorder_SwapsMealPayloadsWithoutMovingDayCards()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await GetCombinedAislePilotScriptAsync(client);

        Assert.Contains("const swapCardMealPayloads = (firstCard, secondCard) => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"dayCardMealNames\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"dayCardIgnoredFlags\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"dayCardHasSpecialTreat\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const firstMealList = firstCard.querySelector(\"[data-day-reorder-meal-list]\");", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("firstMealList.innerHTML = secondMealList.innerHTML;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("moved = swapCardMealPayloads(card, cards[currentIndex - 1]);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hasMoved = swapCardMealPayloads(activeCard, activeDropTargetCard);", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("const firstSummary = firstCard.querySelector(\"[data-day-card-summary]\");", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("const swapCards = (firstCard, secondCard) => {", script, StringComparison.OrdinalIgnoreCase);
    }
}
