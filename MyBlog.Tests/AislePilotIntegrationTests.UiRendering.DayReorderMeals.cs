using System.Net;
using System.Text.RegularExpressions;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersFullDayMealListForReorderMode()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var postData = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "110"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "3"),
            new("Request.SelectedMealTypes", "Breakfast"),
            new("Request.SelectedMealTypes", "Lunch"),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(postData));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-day-reorder-meal-list", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-reorder-meal-item", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-reorder-meal-thumbnail", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-day-reorder-meal-type", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-day-reorder-meal-name", html, StringComparison.OrdinalIgnoreCase);

        var reorderMealItemCount = Regex.Matches(html, "data-day-reorder-meal-item", RegexOptions.IgnoreCase).Count;
        Assert.True(reorderMealItemCount >= 6, $"Expected at least 6 reorder meal items for 2 days x 3 meals, but found {reorderMealItemCount}.");
    }
}
