using System.Text.Json;

namespace MyBlog.Tests;

public sealed class ModelUsageExperimentIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public ModelUsageExperimentIntegrationTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Post_ProvidesRecordedReplayAndStaticResults()
    {
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/blog/Why_Does_My_AI_Allowance_Disappear_So_Quickly");
        Assert.Contains("data-model-replay=", html);
        Assert.Contains("Every trial, including all attempts", html);
        Assert.Contains("none of these valid trials needed a retry", html);
        Assert.Contains("/js/model-usage-replay.js", html);
        Assert.Contains("/css/model-usage-replay.css", html);
        Assert.Contains("<noscript>", html);
        Assert.DoesNotContain("—", html.Split("<div class=\"post-content\">")[1].Split("</body>")[0]);
        Assert.DoesNotContain("I have not run a fair comparison", html);
    }

    [Fact]
    public async Task Recording_IsCompleteAndUsageTotalsReconcile()
    {
        using var client = _factory.CreateClient();
        var text = await client.GetStringAsync("/experiments/model-usage/2026-09-27.json");
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal("complete", root.GetProperty("status").GetString());
        var trials = root.GetProperty("trials").EnumerateArray().ToArray();
        Assert.Equal(9, trials.Length);
        Assert.Equal(9, trials.Select(t => t.GetProperty("id").GetString()).Distinct().Count());
        foreach (var trial in trials)
        {
            foreach (var attempt in trial.GetProperty("attempts").EnumerateArray())
            {
                var usage = attempt.GetProperty("usage");
                Assert.Equal(usage.GetProperty("input_tokens").GetInt32() + usage.GetProperty("output_tokens").GetInt32(), usage.GetProperty("total_tokens").GetInt32());
                Assert.InRange(usage.GetProperty("output_tokens_details").GetProperty("reasoning_tokens").GetInt32(), 0, usage.GetProperty("output_tokens").GetInt32());
                Assert.True(attempt.GetProperty("estimated_cost_usd").GetDouble() > 0);
            }
        }
        Assert.DoesNotContain("Bearer ", text);
        Assert.DoesNotContain("OPENAI_API_KEY", text);
    }
}
