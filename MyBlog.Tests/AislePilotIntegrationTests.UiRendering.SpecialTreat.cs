using System.Net;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_Post_WithSpecialTreatMeal_RendersVisibleTreatBadgeOnMealCard()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "85",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.IncludeSpecialTreatMeal"] = "true",
            ["Request.SelectedSpecialTreatCookDayIndex"] = "1",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-special-treat-badge", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Treat meal", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-has-special-treat=\"true\"", html, StringComparison.OrdinalIgnoreCase);
    }
}
