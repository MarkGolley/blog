using System.Net;
using System.Text.Json;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task BackgroundStatus_RequiresAdminKey()
    {
        using var client = CreateClient(allowAutoRedirect: false);

        using var response = await client.GetAsync("/admin/aisle-pilot/background-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BackgroundStatus_ReturnsOnlyBoundedOperationalState()
    {
        using var client = CreateClient(allowAutoRedirect: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/aisle-pilot/background-status");
        request.Headers.Add("X-Admin-Key", "integration-aislepilot-key");

        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(32, payload.RootElement.GetProperty("capacity").GetInt32());
        Assert.Equal(3, payload.RootElement.GetProperty("concurrency").GetInt32());
        Assert.True(payload.RootElement.TryGetProperty("queuedByJob", out _));
        Assert.DoesNotContain("mealName", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allergen", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pantry", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshCaches_RequiresAdminKeyAndRunsExistingWarmupPath()
    {
        using var client = CreateClient(allowAutoRedirect: false);
        using var unauthorized = await client.PostAsync(
            "/admin/aisle-pilot/refresh-caches",
            new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/aisle-pilot/refresh-caches")
        {
            Content = new FormUrlEncodedContent([])
        };
        request.Headers.Add("X-Admin-Key", "integration-aislepilot-key");
        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"success\":true", json, StringComparison.Ordinal);
    }
}
