using System.Net;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_Post_WithVariableLayoutSupermarket_RendersLayoutProvenanceAndReviewNote()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Aldi",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Curated chain default", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Branch aisle order can vary. Use Custom if the order looks wrong.", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithStoreAdjustedPricing_RendersPriceProvenance()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Aldi",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Reviewed public basket benchmark.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("15% below Tesco baseline", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Relative to Tesco standard basket pricing", html, StringComparison.OrdinalIgnoreCase);
    }
}
