namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Theory]
    [InlineData("105", true)]
    [InlineData("600", false)]
    public async Task Index_Post_KeepsBudgetStatusVisibleWhilePlanDetailsStartCollapsed(string budget, bool expectOverBudget)
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = budget,
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "7",
            ["Request.CookDays"] = "7",
            ["Request.MealsPerDay"] = "3",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-overview-content hidden=\"hidden\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-overview-toggle", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-expanded=\"false\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectOverBudget ? "over budget" : "left in budget", html, StringComparison.OrdinalIgnoreCase);

        var detailsIndex = html.IndexOf("data-overview-content", StringComparison.OrdinalIgnoreCase);
        var rebalanceIndex = html.IndexOf("Refresh lower-cost plan", StringComparison.OrdinalIgnoreCase);
        if (expectOverBudget)
        {
            Assert.True(rebalanceIndex >= 0 && rebalanceIndex < detailsIndex, "The recovery action must remain outside the collapsed details region.");
            Assert.Contains("action=\"/projects/aisle-pilot/rebalance-budget#aislepilot-overview\"", html, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Equal(-1, rebalanceIndex);
        }
    }

    [Fact]
    public async Task Index_Post_RendersResultsInWeeklyDayMealShoppingExportOrder()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CookDays"] = "2",
            ["Request.MealsPerDay"] = "3",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var weeklyStatus = html.IndexOf("data-results-stage=\"weekly-status\"", StringComparison.OrdinalIgnoreCase);
        var selectedDay = html.IndexOf("data-results-stage=\"selected-day\"", StringComparison.OrdinalIgnoreCase);
        var mealDetails = html.IndexOf("data-results-stage=\"meal-details\"", StringComparison.OrdinalIgnoreCase);
        var shopping = html.IndexOf("data-results-stage=\"shopping\"", StringComparison.OrdinalIgnoreCase);
        var exports = html.IndexOf("data-results-stage=\"exports\"", StringComparison.OrdinalIgnoreCase);

        Assert.True(weeklyStatus >= 0);
        Assert.True(weeklyStatus < selectedDay, "Weekly status must precede the selected day.");
        Assert.True(selectedDay < mealDetails, "The selected day must precede its meal details.");
        Assert.True(mealDetails < shopping, "Meal details must precede shopping.");
        Assert.True(shopping < exports, "Shopping must precede exports.");
        Assert.Contains(">Weekly status<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Review days and meals first, then open shopping or exports.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-overview-toggle", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-overview-content hidden=\"hidden\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-recipe-details-trigger", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Recipe</button>", html, StringComparison.OrdinalIgnoreCase);
    }
}
