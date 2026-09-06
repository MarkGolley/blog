using System.Net;
using System.Text.RegularExpressions;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_Post_WithCustomLeftoverAssignment_ShowsDoubleLeftoverOnRequestedDay()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CookDays"] = "5",
            ["Request.LeftoverCookDayIndexesCsv"] = "4,4",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Adjust cook-extra days", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-leftover-planner", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-rebalance-form", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-max-extra=\"6\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<form[^>]*class=""[^""]*aislepilot-leftover-rebalance-form[^""]*""[^>]*(data-leftover-rebalance-form[^>]*data-ajax-swap-form|data-ajax-swap-form[^>]*data-leftover-rebalance-form)",
                RegexOptions.IgnoreCase),
            html);
        Assert.DoesNotContain("data-leftover-toggle-sign", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-day-card-leftover-controls", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-name=\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-leftover-day-count\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-day-count", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Makes extra for", html, StringComparison.OrdinalIgnoreCase);
    }
}
