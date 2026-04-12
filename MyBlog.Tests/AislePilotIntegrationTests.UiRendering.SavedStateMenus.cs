using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Services;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task SaveWeek_ThenOpenWeek_RestoresSavedMealPlan()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var generateForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        using var generatedResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(generateForm));

        Assert.Equal(HttpStatusCode.OK, generatedResponse.StatusCode);
        var generatedHtml = await generatedResponse.Content.ReadAsStringAsync();
        var generatedMealNames = ExtractRenderedMealNames(generatedHtml);
        Assert.Equal(2, generatedMealNames.Count);

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var saveWeekForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("weekLabel", "Family week"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in generatedMealNames)
        {
            saveWeekForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var savedResponse = await client.PostAsync("/projects/aisle-pilot/save-week", new FormUrlEncodedContent(saveWeekForm));

        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        var savedHtml = await savedResponse.Content.ReadAsStringAsync();
        Assert.Contains("Saved weeks", savedHtml, StringComparison.OrdinalIgnoreCase);
        var savedWeekId = ExtractSavedWeekId(savedHtml);
        Assert.False(string.IsNullOrWhiteSpace(savedWeekId));

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var openWeekForm = new List<KeyValuePair<string, string>>
        {
            new("weekId", savedWeekId),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var openedResponse = await client.PostAsync("/projects/aisle-pilot/open-week", new FormUrlEncodedContent(openWeekForm));

        Assert.Equal(HttpStatusCode.OK, openedResponse.StatusCode);
        var openedHtml = await openedResponse.Content.ReadAsStringAsync();
        var openedMealNames = ExtractRenderedMealNames(openedHtml);
        Assert.Equal(2, openedMealNames.Count);
        Assert.Contains("data-head-menu", openedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-head-week-list", openedHtml, StringComparison.OrdinalIgnoreCase);
        foreach (var mealName in generatedMealNames)
        {
            Assert.Contains(mealName, openedMealNames, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task SavedWeeks_AreRenderedInsideHeaderMenu_OnIndexLoad()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var generateForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        using var generatedResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(generateForm));
        Assert.Equal(HttpStatusCode.OK, generatedResponse.StatusCode);
        var generatedHtml = await generatedResponse.Content.ReadAsStringAsync();
        var generatedMealNames = ExtractRenderedMealNames(generatedHtml);
        Assert.Equal(2, generatedMealNames.Count);

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var saveWeekForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("weekLabel", "Header menu week"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in generatedMealNames)
        {
            saveWeekForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var savedResponse = await client.PostAsync("/projects/aisle-pilot/save-week", new FormUrlEncodedContent(saveWeekForm));
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);

        var indexHtml = await client.GetStringAsync("/projects/aisle-pilot");
        Assert.Contains("data-head-menu", indexHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-head-menu-section-title", indexHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Saved weeks<", indexHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id=\"aislepilot-saved-weeks\"", indexHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/projects/aisle-pilot/open-week", indexHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/projects/aisle-pilot/delete-week", indexHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_SupermarketPlannerSection_IsCollapsedByDefault()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        using var response = await client.GetAsync("/projects/aisle-pilot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var supermarketSectionTag = Regex.Match(
            html,
            @"<details[^>]*data-plan-basic-item=""supermarket""[^>]*>",
            RegexOptions.IgnoreCase);
        Assert.True(supermarketSectionTag.Success, "Supermarket planner section was not found.");
        Assert.DoesNotContain("open", supermarketSectionTag.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SavedMeals_AreRenderedInsideHeaderMenu_AndCanBeRemoved()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var generateForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        using var generatedResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(generateForm));
        Assert.Equal(HttpStatusCode.OK, generatedResponse.StatusCode);
        var generatedHtml = await generatedResponse.Content.ReadAsStringAsync();
        var generatedMealNames = ExtractRenderedMealNames(generatedHtml);
        Assert.Equal(2, generatedMealNames.Count);
        var targetMealName = generatedMealNames[0];

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var saveMealForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("Request.SavedEnjoyedMealNamesState", string.Empty),
            new("mealName", targetMealName),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in generatedMealNames)
        {
            saveMealForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var savedMealResponse = await client.PostAsync("/projects/aisle-pilot/toggle-enjoyed-meal", new FormUrlEncodedContent(saveMealForm));
        Assert.Equal(HttpStatusCode.OK, savedMealResponse.StatusCode);
        var savedMealHtml = await savedMealResponse.Content.ReadAsStringAsync();
        Assert.Contains("data-saved-meals-menu-section", savedMealHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aislepilot-head-saved-meal-list", savedMealHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/projects/aisle-pilot/remove-saved-meal", savedMealHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                $@"<span class=""aislepilot-head-saved-meal-name"">\s*{Regex.Escape(targetMealName)}\s*</span>",
                RegexOptions.IgnoreCase),
            savedMealHtml);

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var removeSavedMealForm = new List<KeyValuePair<string, string>>
        {
            new("mealName", targetMealName),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        using var removedSavedMealResponse = await client.PostAsync(
            "/projects/aisle-pilot/remove-saved-meal",
            new FormUrlEncodedContent(removeSavedMealForm));

        Assert.Equal(HttpStatusCode.OK, removedSavedMealResponse.StatusCode);
        var removedSavedMealHtml = await removedSavedMealResponse.Content.ReadAsStringAsync();
        Assert.Contains("No saved meals yet", removedSavedMealHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            new Regex(
                $@"<span class=""aislepilot-head-saved-meal-name"">\s*{Regex.Escape(targetMealName)}\s*</span>",
                RegexOptions.IgnoreCase),
            removedSavedMealHtml);
    }

    [Fact]
    public async Task ToggleEnjoyedMeal_WhenSavedMealsMenuIsAlreadyFull_ShowsNewestMealImmediately()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var generateForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        using var generatedResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(generateForm));
        Assert.Equal(HttpStatusCode.OK, generatedResponse.StatusCode);
        var generatedHtml = await generatedResponse.Content.ReadAsStringAsync();
        var generatedMealNames = ExtractRenderedMealNames(generatedHtml);
        Assert.Equal(2, generatedMealNames.Count);
        var targetMealName = generatedMealNames[0];

        var preSavedMeals = Enumerable.Range(1, 20)
            .Select(index => $"Previously saved meal {index}")
            .ToArray();

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var saveMealForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("Request.SavedEnjoyedMealNamesState", JsonSerializer.Serialize(preSavedMeals)),
            new("mealName", targetMealName),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in generatedMealNames)
        {
            saveMealForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var savedMealResponse = await client.PostAsync("/projects/aisle-pilot/toggle-enjoyed-meal", new FormUrlEncodedContent(saveMealForm));
        Assert.Equal(HttpStatusCode.OK, savedMealResponse.StatusCode);
        var savedMealHtml = await savedMealResponse.Content.ReadAsStringAsync();

        Assert.Matches(
            new Regex(
                $@"<span class=""aislepilot-head-saved-meal-name"">\s*{Regex.Escape(targetMealName)}\s*</span>",
                RegexOptions.IgnoreCase),
            savedMealHtml);
        Assert.DoesNotMatch(
            new Regex(
                $@"<span class=""aislepilot-head-saved-meal-name"">\s*{Regex.Escape(preSavedMeals[^1])}\s*</span>",
                RegexOptions.IgnoreCase),
            savedMealHtml);

        var savedState = ExtractHiddenInputValue(savedMealHtml, "Request.SavedEnjoyedMealNamesState");
        Assert.Contains(targetMealName, savedState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteWeek_RemovesSavedWeekAndBlocksOpen()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var generateForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        using var generatedResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(generateForm));

        Assert.Equal(HttpStatusCode.OK, generatedResponse.StatusCode);
        var generatedHtml = await generatedResponse.Content.ReadAsStringAsync();
        var generatedMealNames = ExtractRenderedMealNames(generatedHtml);
        Assert.Equal(2, generatedMealNames.Count);

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var saveWeekForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("weekLabel", "Delete me"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in generatedMealNames)
        {
            saveWeekForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var savedResponse = await client.PostAsync("/projects/aisle-pilot/save-week", new FormUrlEncodedContent(saveWeekForm));
        Assert.Equal(HttpStatusCode.OK, savedResponse.StatusCode);
        var savedHtml = await savedResponse.Content.ReadAsStringAsync();
        var savedWeekId = ExtractSavedWeekId(savedHtml);
        Assert.False(string.IsNullOrWhiteSpace(savedWeekId));

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var deleteForm = new List<KeyValuePair<string, string>>
        {
            new("weekId", savedWeekId),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        using var deletedResponse = await client.PostAsync("/projects/aisle-pilot/delete-week", new FormUrlEncodedContent(deleteForm));

        Assert.Equal(HttpStatusCode.OK, deletedResponse.StatusCode);
        var deletedHtml = await deletedResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(savedWeekId, deletedHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Delete me", deletedHtml, StringComparison.OrdinalIgnoreCase);

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var openDeletedWeekForm = new List<KeyValuePair<string, string>>
        {
            new("weekId", savedWeekId),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        using var openDeletedWeekResponse = await client.PostAsync("/projects/aisle-pilot/open-week", new FormUrlEncodedContent(openDeletedWeekForm));

        Assert.Equal(HttpStatusCode.OK, openDeletedWeekResponse.StatusCode);
        var openDeletedWeekHtml = await openDeletedWeekResponse.Content.ReadAsStringAsync();
        Assert.Contains("Saved week was not found", openDeletedWeekHtml, StringComparison.OrdinalIgnoreCase);
    }
}
