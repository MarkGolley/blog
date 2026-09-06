using System.Net;
using System.Text.RegularExpressions;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_Post_MealImagesDeclareResponsiveCandidatesAndIntrinsicDimensions()
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

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Matches(
            new Regex(
                @"<img[^>]*class=""[^""]*aislepilot-meal-image[^""]*""(?=[^>]*srcset=""[^""]+\s+1024w"")(?=[^>]*sizes=""\(max-width: 760px\)[^""]+"")(?=[^>]*width=""1024"")(?=[^>]*height=""1024"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<img[^>]*class=""[^""]*aislepilot-day-reorder-meal-thumbnail[^""]*""(?=[^>]*width=""32"")(?=[^>]*height=""32"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
    }
}
