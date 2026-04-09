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
    public async Task SwapDessert_WithDessertAddOnEnabled_RotatesToDifferentDessert()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        const string currentDessertName = "Chocolate sponge tray bake";
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAislePilotService>();
        var expectedNextDessertName = service.ResolveNextDessertAddOnName(currentDessertName);
        Assert.False(
            currentDessertName.Equals(expectedNextDessertName, StringComparison.OrdinalIgnoreCase),
            "Test precondition failed: expected next dessert must differ from current.");

        var swapDessertForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "75"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.IncludeDessertAddOn", "true"),
            new("Request.SelectedDessertAddOnName", currentDessertName),
            new("Request.DietaryModes", "Balanced"),
            new("currentDessertAddOnName", currentDessertName),
            new("currentPlanMealNames", "Chicken stir fry with rice"),
            new("currentPlanMealNames", "Turkey chilli with beans"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var swappedResponse = await client.PostAsync("/projects/aisle-pilot/swap-dessert", new FormUrlEncodedContent(swapDessertForm));

        Assert.Equal(HttpStatusCode.OK, swappedResponse.StatusCode);
        var swappedHtml = await swappedResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            "Could not resolve the current plan for dessert swap",
            swappedHtml,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedNextDessertName, swappedHtml, StringComparison.OrdinalIgnoreCase);
        var selectedDessertValues = ExtractHiddenInputValues(swappedHtml, "Request.SelectedDessertAddOnName");
        Assert.Contains(
            selectedDessertValues,
            value => value.Equals(expectedNextDessertName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Swap dessert", swappedHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestFromPantry_ValidRequest_ReturnsPantryIdeas()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "eggs, rice, frozen mixed veg, soy sauce",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Meal suggestions", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Egg fried rice", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Need to buy", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Show 3 more ideas", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Swap idea", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, Regex.Matches(html, "data-meal-name=\"", RegexOptions.IgnoreCase).Count);
    }

    [Fact]
    public async Task SuggestFromPantry_UsesPriorityImageLoadingForTopSuggestions()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "eggs, rice, frozen mixed veg, soy sauce",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var eagerHighMatches = Regex.Matches(
            html,
            @"<img[^>]*class=""[^""]*aislepilot-meal-image[^""]*""(?=[^>]*loading=""eager"")(?=[^>]*fetchpriority=""high"")[^>]*>",
            RegexOptions.IgnoreCase);
        Assert.True(eagerHighMatches.Count >= 2, $"Expected at least two top suggestion images to be eager/high priority, found {eagerHighMatches.Count}.");
        Assert.Matches(
            new Regex(
                @"<img[^>]*class=""[^""]*aislepilot-meal-image[^""]*""(?=[^>]*loading=""lazy"")(?=[^>]*fetchpriority=""low"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task SwapPantrySuggestion_ValidRequest_ExcludesCurrentMealFromSuggestions()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/swap-pantry-suggestion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "eggs, rice, frozen mixed veg, soy sauce, chicken",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["currentMealName"] = "Egg fried rice",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Meal suggestions", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-meal-name=\"Egg fried rice\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestFromPantry_KnownMeal_UsesBundledMealImagePath()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "eggs, rice, frozen mixed veg, soy sauce",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Egg fried rice", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "/projects/aisle-pilot/images/aislepilot-meals/egg-fried-rice.png",
            html,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwapPantrySuggestion_ReplacesOnlyClickedCard()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var initialResponse = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "eggs, rice, frozen mixed veg, soy sauce, chicken, spinach, noodles",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);
        var initialHtml = await initialResponse.Content.ReadAsStringAsync();
        var initialMealNames = ExtractRenderedMealNames(initialHtml);
        Assert.True(initialMealNames.Count >= 3);

        var pantryHistoryState = ExtractHiddenInputValue(initialHtml, "Request.PantrySuggestionHistoryState");
        Assert.False(string.IsNullOrWhiteSpace(pantryHistoryState));

        var swapFormValues = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "65"),
            new("Request.HouseholdSize", "2"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PantryItems", "eggs, rice, frozen mixed veg, soy sauce, chicken, spinach, noodles"),
            new("Request.PantrySuggestionHistoryState", pantryHistoryState),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("currentMealName", initialMealNames[0]),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in initialMealNames)
        {
            swapFormValues.Add(new KeyValuePair<string, string>("currentSuggestionMealNames", mealName));
        }

        using var swapResponse = await client.PostAsync("/projects/aisle-pilot/swap-pantry-suggestion", new FormUrlEncodedContent(swapFormValues));

        Assert.Equal(HttpStatusCode.OK, swapResponse.StatusCode);
        var swappedHtml = await swapResponse.Content.ReadAsStringAsync();
        var swappedMealNames = ExtractRenderedMealNames(swappedHtml);
        Assert.Equal(3, swappedMealNames.Count);
        var overlapCount = swappedMealNames.Count(
            mealName => initialMealNames.Contains(mealName, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(2, overlapCount);
        Assert.Contains(
            swappedMealNames,
            mealName => !initialMealNames.Contains(mealName, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuggestFromPantry_ShowThreeMoreIdeas_ReplacesAllCards()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var initialResponse = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "eggs, rice, frozen mixed veg, soy sauce, chicken, spinach, noodles",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);
        var initialHtml = await initialResponse.Content.ReadAsStringAsync();
        var initialMealNames = ExtractRenderedMealNames(initialHtml);
        Assert.Equal(3, initialMealNames.Count);

        var pantryHistoryState = ExtractHiddenInputValue(initialHtml, "Request.PantrySuggestionHistoryState");
        Assert.False(string.IsNullOrWhiteSpace(pantryHistoryState));

        var refreshFormValues = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "65"),
            new("Request.HouseholdSize", "2"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PantryItems", "eggs, rice, frozen mixed veg, soy sauce, chicken, spinach, noodles"),
            new("Request.PantrySuggestionHistoryState", pantryHistoryState),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in initialMealNames)
        {
            refreshFormValues.Add(new KeyValuePair<string, string>("excludedMealNames", mealName));
        }

        using var refreshResponse = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(refreshFormValues));

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshedHtml = await refreshResponse.Content.ReadAsStringAsync();
        var refreshedMealNames = ExtractRenderedMealNames(refreshedHtml);
        Assert.Equal(3, refreshedMealNames.Count);
        Assert.DoesNotContain(
            refreshedMealNames,
            mealName => initialMealNames.Contains(mealName, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuggestFromPantry_EmptyPantry_ShowsValidationError()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Add a few pantry ingredients", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestFromPantry_UnrelatedPantry_ShowsNoMatchesMessage()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "coffee, cocoa powder, marshmallows",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("No close meals found", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestFromPantry_ChickenAndRice_ShowsClosestMatches()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "chicken, rice",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Meal suggestions", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chicken stir fry with rice", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Need to buy", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestFromPantry_StrictCoreMode_ChickenAndRice_ShowsStrictModeNoMatchMessage()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "chicken, rice",
            ["Request.RequireCorePantryIngredients"] = "true",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("No meals fit strict core mode", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestFromPantry_StrictCoreMode_WithCompleteCoreIngredients_ShowsResult()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "eggs, rice, frozen mixed veg, soy sauce",
            ["Request.RequireCorePantryIngredients"] = "true",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Meal suggestions", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Egg fried rice", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestFromPantry_StrictCoreMode_WithTrayBakeCoreIngredients_ShowsTrayBake()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "chicken breasts, peppers, courgettes, sweet potatoes, oil, herbs",
            ["Request.RequireCorePantryIngredients"] = "true",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Roast chicken and veg tray bake", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cook now", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestFromPantry_NaturalPantryInput_ShowsEggFriedRice()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "rice, eggs, peas, oil, chicken, various sauces",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Meal suggestions", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Egg fried rice", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuggestFromPantry_ChickenLeakPastryInput_ShowsUsefulMeal()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/suggest-from-pantry", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PantryItems"] = "chicken, leak, pastry, milk, cream, salt, pepper",
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Meal suggestions", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chicken and leek cream pie", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No close meals found", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwapMeal_InvalidDay_ShowsValidationError()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/swap-meal", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["dayIndex"] = "20",
            ["currentMealName"] = string.Empty,
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Selected day was out of range", html, StringComparison.OrdinalIgnoreCase);
    }
}
