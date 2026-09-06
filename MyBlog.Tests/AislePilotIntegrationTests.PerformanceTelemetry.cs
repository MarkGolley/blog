using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Net.Http.Headers;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_WarmTemplatePost_CompletesWithinTwoSeconds()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        using var form = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "65"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        });

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.PostAsync("/projects/aisle-pilot", form);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Warm plan POST took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ClientPerformance_AcceptsPrivacySafeAllowlistedMetric()
    {
        using var client = CreateClient(allowAutoRedirect: false);

        using var response = await client.PostAsJsonAsync(
            "/projects/aisle-pilot/client-performance",
            new { metric = "lcp", valueMilliseconds = 1234.5, navigationType = "navigate", hasResult = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task ClientPerformance_RejectsUnknownMetric()
    {
        using var client = CreateClient(allowAutoRedirect: false);

        using var response = await client.PostAsJsonAsync(
            "/projects/aisle-pilot/client-performance",
            new { metric = "user_food_preferences", valueMilliseconds = 1, navigationType = "navigate" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClientPerformance_AcceptsAllowlistedJourneyEvent()
    {
        using var client = CreateClient(allowAutoRedirect: false);

        using var response = await client.PostAsJsonAsync(
            "/projects/aisle-pilot/client-performance",
            new { @event = "setup_abandoned", navigationType = "navigate", hasResult = false });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Index_LoadsPerformanceInstrumentation()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");
        var script = await client.GetStringAsync("/js/aisle-pilot/performance.js");
        var interactionScript = await client.GetStringAsync("/js/aisle-pilot.js");
        var imagePollingScript = await client.GetStringAsync("/js/aisle-pilot/meal-image-polling.js");
        var siteCss = await client.GetStringAsync("/css/site.css");

        Assert.Contains("/js/aisle-pilot/performance.js", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fonts.googleapis.com", siteCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", siteCss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/css/aisle-pilot.css", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/css/aisle-pilot-shell.css", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("rel=\"stylesheet\"[^>]+media=\"print\"[^>]+aisle-pilot-results\\.css", html);
        Assert.Matches("rel=\"stylesheet\"[^>]+media=\"print\"[^>]+aisle-pilot-actions\\.css", html);
        Assert.Contains("largest-contentful-paint", script, StringComparison.Ordinal);
        Assert.Contains("submit_to_plan_visible", script, StringComparison.Ordinal);
        Assert.Contains("setup_abandoned", script, StringComparison.Ordinal);
        Assert.Contains("unhandled_rejection", script, StringComparison.Ordinal);
        Assert.Contains("pantry_latency", script, StringComparison.Ordinal);
        Assert.Contains("AislePilotPerformance", script, StringComparison.Ordinal);
        Assert.Contains("swap_latency", interactionScript, StringComparison.Ordinal);
        Assert.Contains("save_latency", interactionScript, StringComparison.Ordinal);
        Assert.Contains("export_latency", interactionScript, StringComparison.Ordinal);
        Assert.Contains("image_placeholder_to_image", imagePollingScript, StringComparison.Ordinal);
        Assert.DoesNotContain("DislikesOrAllergens", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PantryItems", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StaticStylesheet_UsesBrotliCompression_WhenClientSupportsIt()
    {
        using var client = CreateClient(allowAutoRedirect: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/css/aisle-pilot-refresh.css");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
    }
}
