using System.Net;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Services;

namespace MyBlog.Tests;

public class AislePilotIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private static int _clientIpCounter = 40;
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
    public async Task ToggleEnjoyedMeal_ValidRequest_PersistsSavedMealState()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var baselineForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.EnableSavedMealRepeats", "true"),
            new("Request.SavedMealRepeatRatePercent", "35"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var baselineResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(baselineForm));

        Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
        var baselineHtml = await baselineResponse.Content.ReadAsStringAsync();
        var currentPlanMealNames = ExtractRenderedMealNames(baselineHtml);
        Assert.True(currentPlanMealNames.Count >= 2);
        var targetMealName = currentPlanMealNames[0];

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var toggleForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "85"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "2"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.EnableSavedMealRepeats", "true"),
            new("Request.SavedMealRepeatRatePercent", "35"),
            new("Request.SavedEnjoyedMealNamesState", string.Empty),
            new("Request.DietaryModes", "Balanced"),
            new("mealName", targetMealName),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in currentPlanMealNames)
        {
            toggleForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var toggleResponse = await client.PostAsync("/projects/aisle-pilot/toggle-enjoyed-meal", new FormUrlEncodedContent(toggleForm));

        Assert.Equal(HttpStatusCode.OK, toggleResponse.StatusCode);
        var toggledHtml = await toggleResponse.Content.ReadAsStringAsync();
        var savedState = ExtractHiddenInputValue(toggledHtml, "Request.SavedEnjoyedMealNamesState");
        Assert.Contains(targetMealName, savedState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("title=\"Unsave meal\"", toggledHtml, StringComparison.OrdinalIgnoreCase);
    }

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
    public async Task Index_Post_WhenOnlyPortionSizeChanges_UsesCurrentPlanMealNames()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var baselineResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "70"),
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
        }));

        Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var postedCurrentPlanMealNames = new[]
        {
            "Greek yogurt berry oat pots",
            "Chicken and leek cream pie"
        };
        var portionUpdateForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "70"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Small"),
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
        foreach (var mealName in postedCurrentPlanMealNames)
        {
            portionUpdateForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var portionUpdateResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(portionUpdateForm));

        Assert.Equal(HttpStatusCode.OK, portionUpdateResponse.StatusCode);
        var html = await portionUpdateResponse.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Request.PortionSize\" value=\"Small\"", html, StringComparison.OrdinalIgnoreCase);
        var renderedMealNames = ExtractRenderedMealNames(html);
        Assert.Equal(2, renderedMealNames.Count);
        Assert.Contains("Greek yogurt berry oat pots", renderedMealNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Chicken and leek cream pie", renderedMealNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WhenOnlyLeftoverRebalanceChanges_UsesCurrentPlanMealNames()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var baselineForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "3"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.LeftoverCookDayIndexesCsv", "0"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var baselineResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(baselineForm));

        Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
        var baselineHtml = await baselineResponse.Content.ReadAsStringAsync();
        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var postedCurrentPlanMealNames = ExtractRenderedMealNames(baselineHtml);
        Assert.Equal(2, postedCurrentPlanMealNames.Count);
        var leftoverRebalanceForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "3"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.LeftoverCookDayIndexesCsv", "1"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in postedCurrentPlanMealNames)
        {
            leftoverRebalanceForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var rebalanceResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(leftoverRebalanceForm));

        Assert.Equal(HttpStatusCode.OK, rebalanceResponse.StatusCode);
        var html = await rebalanceResponse.Content.ReadAsStringAsync();
        Assert.Contains(
            "name=\"Request.LeftoverCookDayIndexesCsv\" value=\"1\"",
            html,
            StringComparison.OrdinalIgnoreCase);
        var renderedMealNames = ExtractRenderedMealNames(html);
        Assert.Equal(2, renderedMealNames.Count);
        foreach (var mealName in postedCurrentPlanMealNames)
        {
            Assert.Contains(mealName, renderedMealNames, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Index_Post_WhenLeftoverRebalanceReducesCookDays_UsesCurrentPlanMealPrefix()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var baselineForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "3"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.LeftoverCookDayIndexesCsv", "0"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var baselineResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(baselineForm));

        Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
        var baselineHtml = await baselineResponse.Content.ReadAsStringAsync();
        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var baselineMealNames = ExtractRenderedMealNames(baselineHtml);
        Assert.Equal(2, baselineMealNames.Count);
        var reorderedCurrentPlanMealNames = baselineMealNames
            .AsEnumerable()
            .Reverse()
            .ToList();
        Assert.False(
            string.Equals(reorderedCurrentPlanMealNames[0], baselineMealNames[0], StringComparison.OrdinalIgnoreCase));

        var leftoverRebalanceForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "3"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.LeftoverCookDayIndexesCsv", "0,1"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in reorderedCurrentPlanMealNames)
        {
            leftoverRebalanceForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var rebalanceResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(leftoverRebalanceForm));

        Assert.Equal(HttpStatusCode.OK, rebalanceResponse.StatusCode);
        var html = await rebalanceResponse.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Request.CookDays\" value=\"1\"", html, StringComparison.OrdinalIgnoreCase);
        var renderedMealNames = ExtractRenderedMealNames(html);
        Assert.Single(renderedMealNames);
        Assert.Equal(reorderedCurrentPlanMealNames[0], renderedMealNames[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WhenLeftoverRebalanceIncreasesCookDays_TopsUpFromCurrentPlanWithoutFullRegenerate()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var baselineForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "3"),
            new("Request.CookDays", "1"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.LeftoverCookDayIndexesCsv", "0,1"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var baselineResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(baselineForm));

        Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
        var baselineHtml = await baselineResponse.Content.ReadAsStringAsync();
        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var baselineMealNames = ExtractRenderedMealNames(baselineHtml);
        Assert.Single(baselineMealNames);

        var leftoverRebalanceForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "3"),
            new("Request.CookDays", "1"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.LeftoverCookDayIndexesCsv", "0"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        leftoverRebalanceForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", baselineMealNames[0]));

        using var rebalanceResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(leftoverRebalanceForm));

        Assert.Equal(HttpStatusCode.OK, rebalanceResponse.StatusCode);
        var html = await rebalanceResponse.Content.ReadAsStringAsync();
        Assert.Contains("name=\"Request.CookDays\" value=\"2\"", html, StringComparison.OrdinalIgnoreCase);
        var renderedMealNames = ExtractRenderedMealNames(html);
        Assert.Equal(2, renderedMealNames.Count);
        Assert.Equal(baselineMealNames[0], renderedMealNames[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WhenRemovingOneLeftoverAssignment_PreservesOtherAssignedDay()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var baselineForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "7"),
            new("Request.CookDays", "5"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.LeftoverCookDayIndexesCsv", "0,2"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var baselineResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(baselineForm));

        Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
        var baselineHtml = await baselineResponse.Content.ReadAsStringAsync();
        var baselineAssignedIndexes = ExtractAssignedLeftoverDayIndexesFromCards(baselineHtml);
        Assert.Equal(2, baselineAssignedIndexes.Count);
        var keptIndex = baselineAssignedIndexes[1];
        var baselineMealNames = ExtractRenderedMealNames(baselineHtml);
        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var rebalanceForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
            new("Request.HouseholdSize", "2"),
            new("Request.PortionSize", "Medium"),
            new("Request.PlanDays", "7"),
            new("Request.CookDays", "5"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.PreferQuickMeals", "true"),
            new("Request.LeftoverCookDayIndexesCsv", $"{keptIndex}"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in baselineMealNames)
        {
            rebalanceForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var rebalanceResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(rebalanceForm));

        Assert.Equal(HttpStatusCode.OK, rebalanceResponse.StatusCode);
        var html = await rebalanceResponse.Content.ReadAsStringAsync();
        var assignedIndexesAfterRemoval = ExtractAssignedLeftoverDayIndexesFromCards(html);
        Assert.Single(assignedIndexesAfterRemoval);
        Assert.Equal(keptIndex, assignedIndexesAfterRemoval[0]);
    }

    [Fact]
    public async Task Index_Post_WithLeftoverDayIndexesCsv_ShowsTwoLeftoverDaysInOverview()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "7",
            ["Request.LeftoverCookDayIndexesCsv"] = "0,1",
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
        Assert.Contains("class=\"aislepilot-mobile-context-meta-values\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Makes extra for", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            "data-overview-content(?:\\s+[^>]*)?\\s+hidden|hidden(?:\\s+[^>]*)?\\s+data-overview-content",
            html);
    }

    [Fact]
    public async Task Index_Post_WeeklySummaryBudgetDirection_MatchesOverviewBudgetDifferenceSign()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "7",
            ["Request.LeftoverCookDayIndexesCsv"] = "0,1",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var budgetDifferenceText = ExtractOverviewBudgetDifferenceText(html);
        var weeklySummaryText = ExtractWeeklyBudgetSummaryText(html);

        Assert.True(
            decimal.TryParse(
                budgetDifferenceText.Replace("\u00A0", " "),
                NumberStyles.Currency,
                CultureInfo.GetCultureInfo("en-GB"),
                out var budgetDifferenceValue),
            $"Could not parse budget difference value '{budgetDifferenceText}'.");

        if (budgetDifferenceValue < 0m)
        {
            Assert.Contains("over", weeklySummaryText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("under", weeklySummaryText, StringComparison.OrdinalIgnoreCase);
            return;
        }

        if (budgetDifferenceValue > 0m)
        {
            Assert.Contains("under", weeklySummaryText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("over", weeklySummaryText, StringComparison.OrdinalIgnoreCase);
            return;
        }

        Assert.Contains("on budget", weeklySummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithNoLeftovers_HidesLeftoversOverviewStat()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "7",
            ["Request.CookDays"] = "7",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<p class=\"aislepilot-stat-label\">Leftovers</p>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Rebalance leftovers", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithSinglePersonAndSpecialOptions_RendersSingularServingSummaryAndPersistsFlags()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "1",
            ["Request.CookDays"] = "2",
            ["Request.PlanDays"] = "2",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.IncludeSpecialTreatMeal"] = "true",
            ["Request.IncludeDessertAddOn"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("1 person -", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Treat meal on", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dessert add-on", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chocolate sponge tray bake", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plated dessert", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Special treat<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"Request.IncludeSpecialTreatMeal\" value=\"true\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"Request.IncludeDessertAddOn\" value=\"true\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_RendersPlanDaysSlider()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("name=\"Request.PlanDays\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-plan-days-slider", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-cook-days-slider", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<input[^>]*name=""Request\.CookDays""[^>]*type=""hidden""|<input[^>]*type=""hidden""[^>]*name=""Request\.CookDays""",
                RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task Index_Get_RendersSavedMealRepeatStrengthSliderWithPlanBasicsFrameStyle()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Matches(
            new Regex(
                @"<div[^>]*(?:class=""[^""]*aislepilot-slider-field--plan-basics-frame[^""]*""[^>]*data-saved-repeat-rate-field|data-saved-repeat-rate-field[^>]*class=""[^""]*aislepilot-slider-field--plan-basics-frame[^""]*"")",
                RegexOptions.IgnoreCase),
            html);
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
    public async Task Index_Get_RendersPlanLoadingSkeletonShellAndPlannerTrigger()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("data-plan-loading-shell", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-setup-mode-submit=\"planner\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-show-plan-skeleton", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_UsesSimplifiedAislePilotLayoutWithoutDecorativeOverlays()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.DoesNotContain("class=\"page-aurora\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-scroll-progress", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_UsesLargerControlHeightTokensForReadableTapTargets()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await client.GetStringAsync("/css/aisle-pilot.css");

        Assert.Contains("--ap-control-height: 2.5rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-control-height-lg: 2.75rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-control-height-touch: 2.75rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--ap-overview-button-size: 2.75rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min-height: var(--ap-overview-button-size) !important;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min-width: 8.8rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-meal-panel.has-open-actions", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow-y: visible;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overflow: hidden;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-card-action-row :is(.aislepilot-meal-details > summary, .aislepilot-swap-btn, .aislepilot-more-actions-trigger):focus-visible", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inset 0 0 0 2px var(--ap-focus-ring-color)", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-card-action-row :is(.aislepilot-meal-details > summary, .aislepilot-swap-btn, .aislepilot-more-actions-trigger):focus", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outline: none;", css, StringComparison.OrdinalIgnoreCase);
        var importantCount = Regex.Matches(css, "!important", RegexOptions.IgnoreCase).Count;
        Assert.True(importantCount <= 205, $"Expected CSS override count to stay under control, but found {importantCount} '!important' usages.");
    }

    [Fact]
    public async Task AislePilotScript_PreservesIconOnlyButtonsWhenSubmitLoadingStarts()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await client.GetStringAsync("/js/aisle-pilot.js");

        Assert.Contains("if (submitButton.classList.contains(\"is-icon-only\"))", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("submitButton.setAttribute(\"aria-label\", nextAriaLabel);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (!button.classList.contains(\"is-icon-only\") && originalLabel && originalLabel.length > 0)", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotScript_LeftoverPlanner_SubmitsImmediatelyFromCardToggle()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await client.GetStringAsync("/js/aisle-pilot.js");

        Assert.Contains("const leftoverRebalanceForms = scope instanceof Element", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const maxExtraRaw = Number.parseInt(", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const toggleButton = zone.querySelector(\"[data-leftover-toggle-sign]\")", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("zone.classList.toggle(\"is-leftover-locked\", !isAssigned && !canAssignMore);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toggleButton.textContent = \"++\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("toggleButton.textContent = \"-\";", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (isLeftoverRebalanceForm) {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clearPersistedSwapScroll();", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (getAssignedCount() >= maxExtraAllocations)", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("submitLeftoverRebalance();", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("persistSwapScrollPosition(leftoverRebalanceForm);", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotScript_AjaxSwapResponse_RestoresMealImagesToReduceFlash()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var script = await client.GetStringAsync("/js/aisle-pilot.js");

        Assert.Contains("const captureRenderedMealImageSources = scope => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const restoreRenderedMealImageSources = (scope, imageSrcByMealName) => {", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("imageElement.replaceWith(preservedImageElement);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("const preservedMealImageSources = captureRenderedMealImageSources(document);", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restoreRenderedMealImageSources(document, preservedMealImageSources);", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_MealMethodListsRenderMarkersInsidePanelBounds()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await client.GetStringAsync("/css/aisle-pilot.css");

        Assert.Contains(".aislepilot-day-meal-panel > .aislepilot-meal-details-panel ol.case-list", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("list-style-position: inside;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("padding-left: 0;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-day-card:hover {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transform: none;", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_MoreActionsMenu_UsesReadableLabelledActionButtons()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await client.GetStringAsync("/css/aisle-pilot.css");

        Assert.Contains(".aislepilot-card-more-actions-menu {", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("min-width: 8.8rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("width: max-content;", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("width: 3.4rem;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ".aislepilot-card-more-action-form:not(.aislepilot-favorite-form):not(.aislepilot-ignore-form) .aislepilot-swap-btn.is-secondary.is-compact",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            ".aislepilot-card-more-action-form.aislepilot-favorite-form .aislepilot-swap-btn.is-secondary.is-compact:not(.is-saved-meal)",
            css,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".aislepilot-card-more-actions-menu .aislepilot-swap-btn.is-compact .aislepilot-btn-label", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fill: currentColor;", css, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stroke: none;", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AislePilotStylesheet_LeftoverCardToggle_IsPlainSymbolWithoutButtonChrome()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var css = await client.GetStringAsync("/css/aisle-pilot.css");
        var stepButtonBlockMatch = Regex.Match(
            css,
            @"\.aislepilot-leftover-step-btn\s*\{(?<body>[\s\S]*?)\}",
            RegexOptions.IgnoreCase);

        Assert.True(stepButtonBlockMatch.Success, "Expected .aislepilot-leftover-step-btn CSS block.");
        var stepButtonBlock = stepButtonBlockMatch.Groups["body"].Value;
        Assert.Contains("border: none;", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("border-radius: 0;", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background: transparent;", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border: 1px solid rgba(16, 63, 101, 0.3);", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("border-radius: 999px;", stepButtonBlock, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains(">Breakfast<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Lunch<", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Dinner<", html, StringComparison.OrdinalIgnoreCase);

        var mealTabsMatches = Regex.Matches(
            html,
            @"<div class=""aislepilot-day-meal-tabs""[^>]*>(?<tabs>[\s\S]*?)</div>",
            RegexOptions.IgnoreCase);
        Assert.True(mealTabsMatches.Count > 0, "Expected meal slot tab list to be rendered.");

        string tabsMarkup = string.Empty;
        foreach (Match match in mealTabsMatches)
        {
            var candidate = match.Groups["tabs"].Value;
            if (Regex.IsMatch(candidate, @">\s*Breakfast\s*<", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(candidate, @">\s*Lunch\s*<", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(candidate, @">\s*Dinner\s*<", RegexOptions.IgnoreCase))
            {
                tabsMarkup = candidate;
                break;
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(tabsMarkup), "Expected at least one meal tab list to include Breakfast, Lunch, and Dinner.");
        var breakfastMatch = Regex.Match(tabsMarkup, @">\s*Breakfast\s*<", RegexOptions.IgnoreCase);
        var lunchMatch = Regex.Match(tabsMarkup, @">\s*Lunch\s*<", RegexOptions.IgnoreCase);
        var dinnerMatch = Regex.Match(tabsMarkup, @">\s*Dinner\s*<", RegexOptions.IgnoreCase);
        Assert.True(breakfastMatch.Success && lunchMatch.Success && dinnerMatch.Success, "Expected Breakfast, Lunch, and Dinner tabs.");
        Assert.True(
            breakfastMatch.Index < lunchMatch.Index && lunchMatch.Index < dinnerMatch.Index,
            $"Expected meal tabs in Breakfast -> Lunch -> Dinner order, but indexes were {breakfastMatch.Index}, {lunchMatch.Index}, {dinnerMatch.Index}.");
        Assert.DoesNotContain("class=\"aislepilot-day-card-meta\">3 meals</p>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"Selected meal cost and cook time\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-summary-value=", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<section[^>]*class=""[^""]*aislepilot-day-meal-panel[^""]*""[^>]*>[\s\S]*?<h3>[^<]+</h3>[\s\S]*?<div class=""aislepilot-meal-image-shell"">",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<p class=""aislepilot-day-card-meta""[^>]*>[\s\S]*?(?:\u00A3|&#xA3;|&pound;)[\s\S]*?mins\s*</p>",
                RegexOptions.IgnoreCase),
            html);
        Assert.DoesNotContain("class=\"aislepilot-meal-details-grid\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-nutrition-details\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-recipe-details\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersSwapInsideMoreActionsMenu()
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

        Assert.Contains("class=\"aislepilot-swap-btn is-secondary is-compact\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-swap-action-label\">Swap</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-swap-action-label\">Actions</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-loading-delay-ms=\"320\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-card-header-actions", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-card-more-actions", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"More actions\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<div[^>]*class=""[^""]*aislepilot-card-more-actions-menu[^""]*""[^>]*>[\s\S]*?<form[^>]*action=""[^""]*/swap-meal[^""]*""",
                RegexOptions.IgnoreCase),
            html);
        Assert.DoesNotMatch(
            new Regex(
                @"<form[^>]*action=""[^""]*/swap-meal[^""]*""[^>]*class=""[^""]*aislepilot-card-action-inline[^""]*""",
                RegexOptions.IgnoreCase),
            html);
        Assert.DoesNotContain("class=\"aislepilot-card-kicker\">Breakfast</p>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-card-kicker\">Lunch</p>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-card-kicker\">Dinner</p>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersCompactActionControlsWithVisibleLabels()
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

        Assert.Contains("class=\"aislepilot-overview-regenerate-btn is-icon-only\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-edit-setup-btn is-icon-only\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-more-actions-trigger\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-more-actions-trigger is-icon-only\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-swap-btn is-secondary is-compact\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("is-secondary is-icon-only is-compact", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-swap-action-label\">Save</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-swap-action-label\">Ignore</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-meal-image-summary\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"sr-only\">Refresh plan</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"sr-only\">Edit settings</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"sr-only\">View details</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<span class=\"sr-only\">Swap meal</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span>View details</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">View recipe<", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersMobileStickyContextAndPlanRefreshSkeletonTriggers()
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

        Assert.Contains("class=\"aislepilot-mobile-context\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-mobile-context-jump is-active\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-mobile-context-jump\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-window-tab=\"aislepilot-meals\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-window-tab=\"aislepilot-shop\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-window-tab=\"aislepilot-export\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-mobile-context-jump-text\">Meals</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-mobile-context-jump-text\">Shop</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-mobile-context-jump-text\">Export</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Use tabs to switch between meals, shopping, and exports.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-label=\"Refresh plan\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-show-plan-skeleton", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersAccessibleQuickJumpTabsAndReadableSummarySeparator()
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

        Assert.Contains("class=\"aislepilot-mobile-context-jumps\" role=\"tablist\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<button[^>]*class=""[^""]*aislepilot-mobile-context-jump[^""]*""(?=[^>]*data-window-tab=""aislepilot-meals"")(?=[^>]*role=""tab"")(?=[^>]*aria-controls=""aislepilot-meals"")(?=[^>]*aria-selected=""true"")(?=[^>]*aria-current=""page"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<button[^>]*class=""[^""]*aislepilot-mobile-context-jump[^""]*""(?=[^>]*data-window-tab=""aislepilot-shop"")(?=[^>]*role=""tab"")(?=[^>]*aria-controls=""aislepilot-shop"")(?=[^>]*aria-selected=""false"")(?=[^>]*aria-current=""false"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<button[^>]*class=""[^""]*aislepilot-mobile-context-jump[^""]*""(?=[^>]*data-window-tab=""aislepilot-export"")(?=[^>]*role=""tab"")(?=[^>]*aria-controls=""aislepilot-export"")(?=[^>]*aria-selected=""false"")(?=[^>]*aria-current=""false"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(new Regex(@"(&middot;|·)", RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_RendersWindowTabsAndMealActionsWithImprovedAriaContracts()
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
                @"<button[^>]*id=""aislepilot-tab-meals""(?=[^>]*class=""[^""]*aislepilot-window-tab[^""]*is-active[^""]*"")(?=[^>]*aria-controls=""aislepilot-meals"")(?=[^>]*aria-selected=""true"")(?=[^>]*aria-current=""page"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<button[^>]*id=""aislepilot-tab-shop""(?=[^>]*class=""[^""]*aislepilot-window-tab[^""]*"")(?=[^>]*aria-controls=""aislepilot-shop"")(?=[^>]*aria-selected=""false"")(?=[^>]*aria-current=""false"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<button[^>]*id=""aislepilot-tab-export""(?=[^>]*class=""[^""]*aislepilot-window-tab[^""]*"")(?=[^>]*aria-controls=""aislepilot-export"")(?=[^>]*aria-selected=""false"")(?=[^>]*aria-current=""false"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Contains("<span class=\"aislepilot-tab-text\">Meals</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-tab-text\">Shop</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<span class=\"aislepilot-tab-text\">Export</span>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aria-haspopup=\"menu\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("role=\"menuitem\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-inline-details-panel hidden aria-hidden=\"true\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_PrioritizesFirstMealImagesForFasterPerceivedLoad()
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
                @"<img[^>]*class=""[^""]*aislepilot-meal-image[^""]*""(?=[^>]*loading=""eager"")(?=[^>]*fetchpriority=""high"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(
                @"<img[^>]*class=""[^""]*aislepilot-meal-image[^""]*""(?=[^>]*loading=""lazy"")(?=[^>]*fetchpriority=""low"")[^>]*>",
                RegexOptions.IgnoreCase),
            html);
    }

    [Fact]
    public async Task Index_Post_WithThreeMealsPerDay_SwapFormsIncludeCurrentPlanMealNames()
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
        var swapMealFormMatch = Regex.Match(
            html,
            @"<form[^>]*action=""[^""]*/swap-meal[^""]*""[^>]*>(?<formBody>[\s\S]*?)</form>",
            RegexOptions.IgnoreCase);
        Assert.True(swapMealFormMatch.Success, "Expected at least one swap-meal form in generated plan.");
        Assert.Contains(
            "name=\"currentPlanMealNames\"",
            swapMealFormMatch.Groups["formBody"].Value,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithResult_RendersGenericShareShoppingListLabel()
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
        Assert.Contains("Share shopping list", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iPhone Notes", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WithBreakfastOnlyMealSlot_UsesBreakfastTabsWithoutDuplicateCardKicker()
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
        Assert.Contains(">Breakfast<", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"aislepilot-card-kicker\">Breakfast</p>", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Post_WhenCookDaysExceedPlanDays_DoesNotRenderCookDaysSliderAndKeepsHiddenCookDays()
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
        Assert.DoesNotContain("data-cook-days-slider", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<p class=\"aislepilot-stat-label\">Leftovers</p>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Adjust cook-extra days", html, StringComparison.OrdinalIgnoreCase);
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
        Assert.DoesNotContain("Adjust cook-extra days", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-leftover-planner", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-rebalance-form", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-max-extra=\"6\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(
                @"<form[^>]*class=""[^""]*aislepilot-leftover-rebalance-form[^""]*""[^>]*(data-leftover-rebalance-form[^>]*data-ajax-swap-form|data-ajax-swap-form[^>]*data-leftover-rebalance-form)",
                RegexOptions.IgnoreCase),
            html);
        Assert.Contains("data-leftover-toggle-sign", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-day-card-leftover-controls", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-day-name=\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"aislepilot-leftover-day-count\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-leftover-day-count", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Makes extra for", html, StringComparison.OrdinalIgnoreCase);
    }

    private HttpClient CreateClient(bool allowAutoRedirect)
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = true
        });

        var ipSuffix = Interlocked.Increment(ref _clientIpCounter) % 240;
        if (ipSuffix < 2)
        {
            ipSuffix += 2;
        }

        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.20.30.{ipSuffix}");
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

    private static IReadOnlyList<string> ExtractHiddenInputValues(string html, string inputName)
    {
        var pattern = $@"<input[^>]*name=""{Regex.Escape(inputName)}""[^>]*value=""([^""]*)""";
        return Regex.Matches(html, pattern, RegexOptions.IgnoreCase)
            .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static IReadOnlyList<int> ExtractAssignedLeftoverDayIndexesFromCards(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return [];
        }

        var assignedDayIndexes = new List<int>();
        var zoneMatches = Regex.Matches(
            html,
            @"<div[^>]*data-leftover-day-zone[^>]*>",
            RegexOptions.IgnoreCase);
        foreach (Match zoneMatch in zoneMatches)
        {
            var zoneMarkup = zoneMatch.Value;
            var dayIndexMatch = Regex.Match(zoneMarkup, @"data-day-index=""(?<value>\d+)""", RegexOptions.IgnoreCase);
            var countMatch = Regex.Match(zoneMarkup, @"data-leftover-count=""(?<value>\d+)""", RegexOptions.IgnoreCase);
            if (!dayIndexMatch.Success || !countMatch.Success)
            {
                continue;
            }

            if (!int.TryParse(dayIndexMatch.Groups["value"].Value, out var dayIndex) ||
                !int.TryParse(countMatch.Groups["value"].Value, out var count) ||
                dayIndex < 0 ||
                count <= 0)
            {
                continue;
            }

            for (var i = 0; i < count; i++)
            {
                assignedDayIndexes.Add(dayIndex);
            }
        }

        return assignedDayIndexes;
    }

    private static string ExtractOverviewBudgetDifferenceText(string html)
    {
        var match = Regex.Match(
            html,
            @"<p class=""aislepilot-stat-label"">\s*Budget difference\s*</p>\s*<p class=""aislepilot-stat-value[^""]*"">\s*(?<value>[^<]+)\s*</p>",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Could not find overview budget difference text.");
        return WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
    }

    private static string ExtractWeeklyBudgetSummaryText(string html)
    {
        var match = Regex.Match(
            html,
            @"<span class=""aislepilot-mobile-context-meta-values"">\s*(?<value>[\s\S]*?)\s*</span>",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Could not find weekly summary budget text.");
        var raw = WebUtility.HtmlDecode(match.Groups["value"].Value);
        return Regex.Replace(raw, @"\s+", " ").Trim();
    }

}
