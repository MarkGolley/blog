using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyBlog.Tests;

public class AislePilotIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly Regex AntiForgeryTokenRegexPrimary =
        new(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AntiForgeryTokenRegexFallback =
        new(@"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly TestWebApplicationFactory _factory;

    public AislePilotIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Index_Post_CustomSupermarketWithTooFewAisles_ShowsValidationError()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Custom",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = "Produce",
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Add at least 3 comma-separated aisles", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportPlanPack_InvalidCustomAisleOrder_ReturnsBadRequestValidationProblem()
    {
        using var client = CreateClient(allowAutoRedirect: false);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/export/plan-pack", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Custom",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = "Produce",
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Request.CustomAisleOrder", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportChecklist_NoCompatibleMeals_ReturnsBadRequestValidationProblem()
    {
        using var client = CreateClient(allowAutoRedirect: false);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var formValues = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "65"),
            new("Request.HouseholdSize", "2"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Vegan"),
            new("Request.DietaryModes", "Pescatarian"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var response = await client.PostAsync("/projects/aisle-pilot/export/checklist", new FormUrlEncodedContent(formValues));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("No meals match your dietary modes", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MealImages_WhenAiImageGenerationDisabled_ReturnsCanGenerateImagesFalse()
    {
        using var client = CreateClient(allowAutoRedirect: false);

        using var response = await client.GetAsync("/projects/aisle-pilot/meal-images?mealNames=Egg%20fried%20rice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("canGenerateImages", out var canGenerateImages));
        Assert.False(canGenerateImages.GetBoolean());
    }

    [Fact]
    public async Task MealImages_Response_UsesAislePilotImagePathPrefix()
    {
        using var client = CreateClient(allowAutoRedirect: false);

        using var response = await client.GetAsync("/projects/aisle-pilot/meal-images?mealNames=Egg%20fried%20rice");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var images = document.RootElement.GetProperty("images");
        Assert.Equal(1, images.GetArrayLength());

        var imageUrl = images[0].GetProperty("imageUrl").GetString();
        Assert.False(string.IsNullOrWhiteSpace(imageUrl));
        Assert.StartsWith("/projects/aisle-pilot/images/", imageUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SwapMeal_ValidRequest_ReturnsUpdatedPlanPage()
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
            ["dayIndex"] = "0",
            ["currentMealName"] = string.Empty,
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Meals", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Swap meal", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IgnoreMeal_ValidRequest_TogglesMealToIgnoredState()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var formValues = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "120"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "3"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.IgnoredMealSlotIndexesCsv", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("dayIndex", "1"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        var currentPlanMealNames = new[]
        {
            "Chicken stir fry with rice",
            "Tuna sweetcorn pasta salad",
            "Greek yogurt berry oat pots",
            "Turkey chilli with beans",
            "Mediterranean hummus wraps",
            "Spinach and tomato egg muffins"
        };
        foreach (var mealName in currentPlanMealNames)
        {
            formValues.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var response = await client.PostAsync("/projects/aisle-pilot/ignore-meal", new FormUrlEncodedContent(formValues));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Include meal", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ignored in this plan", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "name=\"Request.IgnoredMealSlotIndexesCsv\" value=\"1\"",
            html,
            StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task GeneratedPlan_RendersPreferQuickMealsHiddenValue_AsBooleanLiteral()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Request.PreferQuickMeals\" value=\"true\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name=\"Request.PreferQuickMeals\" value=\"value\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithFiveCookDays_ShowsTwoLeftoverDaysInOverview()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.CookDays"] = "5",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Cook days", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Leftovers", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2 day(s)", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Makes extra for", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_RendersPlanDaysSlider()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("name=\"Request.PlanDays\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-plan-days-slider", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_RendersMealsPerDayControl_DefaultingToThree()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("name=\"Request.MealsPerDay\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                "name=\"Request\\.MealsPerDay\"[^>]*value=\"3\"|value=\"3\"[^>]*name=\"Request\\.MealsPerDay\"",
                RegexOptions.IgnoreCase),
            html);
        Assert.Contains("name=\"Request.SelectedMealTypes\" value=\"Breakfast\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"Request.SelectedMealTypes\" value=\"Lunch\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"Request.SelectedMealTypes\" value=\"Dinner\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersMealSlotTabsOnDayCards()
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
        Assert.Contains("data-day-meal-tab", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Dinner<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Lunch<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Breakfast<", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithBreakfastOnlyMealSlot_RendersBreakfastCards()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "65"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Breakfast"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(
            2,
            Regex.Matches(html, "class=\"aislepilot-card-kicker\">Breakfast</p>", RegexOptions.IgnoreCase).Count);
    }

    [Fact]
    public async Task Index_Post_WhenCookDaysExceedPlanDays_ClampsCookDaysInRenderedState()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "1",
            ["Request.CookDays"] = "5",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Request.CookDays\" value=\"1\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 day(s)", html, StringComparison.OrdinalIgnoreCase);
    }

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
        Assert.Contains("Makes extra for 2 leftover day(s)", html, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient(bool allowAutoRedirect)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = true
        });

        client.DefaultRequestHeaders.Add("X-Forwarded-For", "10.20.30.40");
        return client;
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = AntiForgeryTokenRegexPrimary.Match(html);
        if (!match.Success)
        {
            match = AntiForgeryTokenRegexFallback.Match(html);
        }

        Assert.True(match.Success, $"Anti-forgery token was not found in response for '{path}'.");
        return match.Groups[1].Value;
    }

    private static IReadOnlyList<string> ExtractRenderedMealNames(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        return Regex.Matches(html, "data-meal-name=\"([^\"]+)\"", RegexOptions.IgnoreCase)
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ExtractHiddenInputValue(string html, string inputName)
    {
        var pattern = $@"<input[^>]*name=""{Regex.Escape(inputName)}""[^>]*value=""([^""]*)""";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"Hidden input '{inputName}' was not found.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
