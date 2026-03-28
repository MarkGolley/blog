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
}
