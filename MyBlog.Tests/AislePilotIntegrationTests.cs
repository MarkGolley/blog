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
    public async Task ExportChecklist_WithConflictingCoreDietaryModes_ReturnsBadRequestValidationProblem()
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
        Assert.Contains("can't be combined", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportChecklist_WithMoreThanTwoDietaryModes_ReturnsBadRequestValidationProblem()
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
            new("Request.DietaryModes", "High-Protein"),
            new("Request.DietaryModes", "Pescatarian"),
            new("Request.DietaryModes", "Gluten-Free"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var response = await client.PostAsync("/projects/aisle-pilot/export/checklist", new FormUrlEncodedContent(formValues));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Choose up to 2 dietary options", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportChecklist_WithResult_ClarifiesEstimateCanBeLowerThanCheckoutTotal()
    {
        using var client = CreateClient(allowAutoRedirect: false);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync("/projects/aisle-pilot/export/checklist", new FormUrlEncodedContent(new Dictionary<string, string>
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
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Meal ingredient estimate:", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("This estimate covers the ingredients used in these meals.", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Actual checkout can be higher if shops only sell larger packs or bags.", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" - Est. ", body, StringComparison.OrdinalIgnoreCase);
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
    public async Task MoveDayCard_ValidRequest_ReordersCurrentPlanSequence()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var reorderedMealNames = new[]
        {
            "Chicken stir fry with rice",
            "Turkey chilli with beans"
        };
        var formValues = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
            new("Request.HouseholdSize", "2"),
            new("Request.PlanDays", "3"),
            new("Request.CookDays", "2"),
            new("Request.MealsPerDay", "1"),
            new("Request.SelectedMealTypes", string.Empty),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.CustomAisleOrder", string.Empty),
            new("Request.DislikesOrAllergens", string.Empty),
            new("Request.LeftoverCookDayIndexesCsv", "1"),
            new("Request.SwapHistoryState", "0:Turkey chilli with beans"),
            new("Request.PreferQuickMeals", "true"),
            new("Request.DietaryModes", "Balanced"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in reorderedMealNames)
        {
            formValues.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var response = await client.PostAsync("/projects/aisle-pilot/move-day-card", new FormUrlEncodedContent(formValues));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var renderedMealNames = ExtractRenderedMealNames(html);
        Assert.Equal(reorderedMealNames, renderedMealNames);
        Assert.Contains(
            "name=\"Request.LeftoverCookDayIndexesCsv\" value=\"1\"",
            html,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "name=\"Request.SwapHistoryState\" value=\"\"",
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
    public async Task Index_Post_WhenForceRefreshWeek_ExcludesCurrentWeekMealsFromRegeneratedPlan()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var baselineForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
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

        using var baselineResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(baselineForm));

        Assert.Equal(HttpStatusCode.OK, baselineResponse.StatusCode);
        var baselineHtml = await baselineResponse.Content.ReadAsStringAsync();
        var baselineMealNames = ExtractRenderedMealNames(baselineHtml);
        Assert.Equal(2, baselineMealNames.Count);

        antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        var refreshForm = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "90"),
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
            new("forceRefreshWeek", "true"),
            new("__RequestVerificationToken", antiForgeryToken)
        };
        foreach (var mealName in baselineMealNames)
        {
            refreshForm.Add(new KeyValuePair<string, string>("currentPlanMealNames", mealName));
        }

        using var refreshedResponse = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(refreshForm));

        Assert.Equal(HttpStatusCode.OK, refreshedResponse.StatusCode);
        var refreshedHtml = await refreshedResponse.Content.ReadAsStringAsync();
        var refreshedMealNames = ExtractRenderedMealNames(refreshedHtml);

        Assert.Equal(2, refreshedMealNames.Count);
        Assert.DoesNotContain(
            refreshedMealNames,
            mealName => baselineMealNames.Contains(mealName, StringComparer.OrdinalIgnoreCase));
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
        Assert.Contains("Plan snapshot", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-overview-content hidden", html, StringComparison.OrdinalIgnoreCase);
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

    private static string ExtractSavedWeekId(string html)
    {
        var match = Regex.Match(
            html,
            @"action=""[^""]*/projects/aisle-pilot/open-week[^""]*""[\s\S]*?<input[^>]*name=""weekId""[^>]*value=""(?<value>[^""]+)""",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Could not find an open-week form with weekId.");
        return WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
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
            @"<span class=""aislepilot-mobile-context-budget-status[^""]*"">\s*(?<value>[^<]+)\s*</span>",
            RegexOptions.IgnoreCase);
        Assert.True(match.Success, "Could not find weekly summary budget text.");
        var raw = WebUtility.HtmlDecode(match.Groups["value"].Value);
        return Regex.Replace(raw, @"\s+", " ").Trim();
    }

}
