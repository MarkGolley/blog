using System.Globalization;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MyBlog.Models;
using MyBlog.Services;
using MyBlog.Utilities;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{

    [Fact]
    public void SuggestMealsFromPantry_WithKnownIngredients_RanksBestMatchFirst()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "eggs, rice, frozen mixed veg, soy sauce",
            DietaryModes = ["Balanced"]
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 4);

        Assert.NotEmpty(suggestions);
        Assert.Equal("Egg fried rice", suggestions[0].MealName);
        Assert.Equal(100, suggestions[0].MatchPercent);
        Assert.Empty(suggestions[0].MissingIngredients);
        Assert.Equal(0m, suggestions[0].MissingIngredientsEstimatedCost);
    }

    [Fact]
    public void SuggestMealsFromPantry_WithKnownIngredients_FillsRequestedSuggestionCount()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "eggs, rice, frozen mixed veg, soy sauce",
            DietaryModes = ["Balanced"]
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 3);

        Assert.Equal(3, suggestions.Count);
        Assert.Equal("Egg fried rice", suggestions[0].MealName);
    }

    [Fact]
    public void OrderPantrySuggestionsByMatch_WhenMatchIsEqual_SortsByLowerTopUpCostFirst()
    {
        var seedMealTemplate = GetMealTemplateSeed();
        var mealTemplates = CreateMealTemplatesFromSeed(seedMealTemplate, ["Higher top-up", "Lower top-up"]);
        var suggestions = new[]
        {
            new AislePilotPantrySuggestionViewModel
            {
                MealName = "Higher top-up",
                MatchPercent = 75,
                MissingIngredientsEstimatedCost = 1.00m,
                MissingCoreIngredientCount = 0
            },
            new AislePilotPantrySuggestionViewModel
            {
                MealName = "Lower top-up",
                MatchPercent = 75,
                MissingIngredientsEstimatedCost = 0.50m,
                MissingCoreIngredientCount = 2
            }
        };

        var ordered = InvokeOrderPantrySuggestionsByMatch(mealTemplates, suggestions);

        Assert.Equal("Lower top-up", ordered[0].MealName);
        Assert.Equal("Higher top-up", ordered[1].MealName);
    }

    [Fact]
    public void SuggestMealsFromPantry_WithExcludedMealNames_DoesNotReturnExcludedMeals()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "eggs, rice, frozen mixed veg, soy sauce, chicken",
            DietaryModes = ["Balanced"]
        };

        var initialSuggestions = _service.SuggestMealsFromPantry(request, 3);
        Assert.NotEmpty(initialSuggestions);

        var excludedMealNames = initialSuggestions
            .Select(suggestion => suggestion.MealName)
            .ToList();
        var refreshedSuggestions = _service.SuggestMealsFromPantry(
            request,
            3,
            excludedMealNames,
            generationNonce: "refresh-pass-1");

        Assert.NotEmpty(refreshedSuggestions);
        Assert.DoesNotContain(
            refreshedSuggestions,
            suggestion => excludedMealNames.Contains(suggestion.MealName, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestMealsFromPantry_EmptyPantry_ReturnsEmpty()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = string.Empty,
            DietaryModes = ["Balanced"]
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 5);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void SuggestMealsFromPantry_WithUnrelatedIngredients_ReturnsEmpty()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "coffee, cocoa powder, marshmallows",
            DietaryModes = ["Balanced"]
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 5);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void SuggestMealsFromPantry_WithSpecificProteinAndStaple_ReturnsClosestMatch()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "chicken, rice",
            DietaryModes = ["Balanced"]
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 8);

        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, suggestion =>
            suggestion.MealName.Equals("Chicken stir fry with rice", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(suggestions, suggestion => suggestion.MatchPercent < 100);
        Assert.Contains(suggestions, suggestion => suggestion.MissingIngredientsEstimatedCost > 0m);
    }

    [Fact]
    public void SuggestMealsFromPantry_WithNaturalPantryInput_SuggestsEggFriedRice()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "rice, eggs, peas, oil, chicken, various sauces",
            DietaryModes = ["Balanced"]
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 6);

        Assert.Contains(suggestions, suggestion =>
            suggestion.MealName.Equals("Egg fried rice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestMealsFromPantry_WithChickenLeakPastryPantry_ReturnsUsefulSuggestions()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "chicken, leak, pastry, milk, cream, salt, pepper",
            DietaryModes = ["Balanced"]
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 3);

        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, suggestion =>
            suggestion.MealName.Equals("Chicken and leek cream pie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestMealsFromPantry_WithRiceEggChickenPantry_PrioritizesRelevantMeals()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "rice, eggs, peas, oil, chicken, various sauces",
            DietaryModes = ["Balanced"]
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 3);

        Assert.Equal(3, suggestions.Count);
        Assert.Contains(suggestions, suggestion =>
            suggestion.MealName.Equals("Egg fried rice", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(suggestions, suggestion =>
            suggestion.MealName.Equals("Chicken stir fry with rice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(suggestions, suggestion =>
            suggestion.MealName.Equals("Smoky chickpea tomato stew", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(suggestions, suggestion =>
            suggestion.MealName.Equals("Chickpea quinoa salad bowls", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestMealsFromPantry_StrictCoreMode_WithCompleteCoreIngredients_ReturnsReadyMeal()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "eggs, rice, frozen mixed veg, soy sauce",
            DietaryModes = ["Balanced"],
            RequireCorePantryIngredients = true
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 6);

        Assert.NotEmpty(suggestions);
        Assert.Contains(suggestions, suggestion =>
            suggestion.MealName.Equals("Egg fried rice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SuggestMealsFromPantry_StrictCoreMode_WithTrayBakeIngredients_ReturnsTrayBake()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "chicken breasts, peppers, courgettes, sweet potatoes, oil, herbs",
            DietaryModes = ["Balanced"],
            RequireCorePantryIngredients = true
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 3);

        var trayBakeSuggestion = suggestions.FirstOrDefault(suggestion =>
            suggestion.MealName.Equals("Roast chicken and veg tray bake", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(trayBakeSuggestion);
        Assert.True(trayBakeSuggestion!.CanCookNow);
        Assert.Equal(0, trayBakeSuggestion.MissingCoreIngredientCount);
    }

    [Fact]
    public void SuggestMealsFromPantry_StrictCoreMode_WhenCoreIngredientsMissing_ReturnsEmpty()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "chicken, rice",
            DietaryModes = ["Balanced"],
            RequireCorePantryIngredients = true
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 6);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void SwapMealForDay_ValidDay_ReplacesMealForThatDay()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2
        };

        var initialPlan = _service.BuildPlan(request);
        var currentMealName = initialPlan.MealPlan[0].MealName;
        var currentPlanMealNames = initialPlan.MealPlan.Select(meal => meal.MealName).ToList();

        var swappedPlan = _service.SwapMealForDay(request, 0, currentMealName, currentPlanMealNames, [currentMealName]);

        Assert.Equal(7, swappedPlan.MealPlan.Count);
        Assert.NotEqual(currentMealName, swappedPlan.MealPlan[0].MealName);
        Assert.Equal(
            swappedPlan.MealPlan.Count,
            swappedPlan.MealPlan.Select(meal => meal.MealName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void SwapMealForDay_InvalidDay_ThrowsArgumentOutOfRangeException()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SwapMealForDay(request, -1, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SwapMealForDay(request, 7, null, null, null));
    }

    [Fact]
    public void SwapMealForDay_WhenCookDaysIsFive_DayFiveThrowsArgumentOutOfRangeException()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 5
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SwapMealForDay(request, 5, null, null, null));
    }

    [Fact]
    public void SwapMealForDay_StrictDietaryModes_StillHonorsConstraints()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            CookDays = 4
        };

        var initialPlan = _service.BuildPlan(request);
        var currentMealName = initialPlan.MealPlan[2].MealName;
        var currentPlanMealNames = initialPlan.MealPlan.Select(meal => meal.MealName).ToList();
        var swappedPlan = _service.SwapMealForDay(request, 2, currentMealName, currentPlanMealNames, [currentMealName]);
        var allowedMeals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Veggie lentil curry",
            "Paneer tikka tray bake",
            "Chickpea quinoa salad bowls",
            "Black bean sweet potato chilli",
            "Mushroom spinach risotto",
            "Halloumi and harissa roast veg tray bake"
        };

        Assert.All(swappedPlan.MealPlan, meal => Assert.Contains(meal.MealName, allowedMeals));
    }

    [Fact]
    public void SwapMealForDay_WhenNoUniqueCandidateExists_ThrowsInvalidOperationException()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            CookDays = 7
        };

        var initialPlan = _service.BuildPlan(request);
        var currentMealName = initialPlan.MealPlan[2].MealName;
        var currentPlanMealNames = initialPlan.MealPlan.Select(meal => meal.MealName).ToList();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.SwapMealForDay(request, 2, currentMealName, currentPlanMealNames, [currentMealName]));
        Assert.Contains("No unique compatible replacement meal is available", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SwapMealForDay_WhenAllSlotCompatibleTemplatesAreSeen_ThrowsInvalidOperationException()
    {
        ClearAiPool();

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var initialPlan = _service.BuildPlan(request);
        Assert.Single(initialPlan.MealPlan);
        var currentMealName = initialPlan.MealPlan[0].MealName;
        var currentPlanMealNames = initialPlan.MealPlan.Select(meal => meal.MealName).ToList();
        var slotCompatibleMealNames = GetCompatibleTemplateMealNamesForSlot(
            request.DietaryModes,
            request.DislikesOrAllergens,
            "Breakfast");
        Assert.True(slotCompatibleMealNames.Count > 1, "Expected multiple breakfast-compatible templates for the test.");

        var seenMealNames = slotCompatibleMealNames
            .Where(name => !name.Equals(currentMealName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(seenMealNames);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _service.SwapMealForDay(
                request,
                dayIndex: 0,
                currentMealName,
                currentPlanMealNames,
                seenMealNames));
        Assert.Contains("No unique compatible replacement meal is available", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SwapMealForDay_WhenSessionAlreadySawCandidate_SkipsSeenMealName()
    {
        ClearAiPool();

        var currentMeal = CreateMealTemplateWithIngredients(
            "Current sesame chicken bowl",
            [
                ("Chicken breast", "Meat & Fish", 2m, "fillets", 3.40m),
                ("Rice", "Tins & Dry Goods", 180m, "g", 0.60m),
                ("Broccoli", "Produce", 1m, "head", 0.85m)
            ]);
        var seenMeal = CreateMealTemplateWithIngredients(
            "Aubergine chicken skillet",
            [
                ("Chicken breast", "Meat & Fish", 2m, "fillets", 3.10m),
                ("Aubergine", "Produce", 1m, "pcs", 0.90m),
                ("Tomatoes", "Produce", 3m, "pcs", 0.75m)
            ]);
        var unseenMeal = CreateMealTemplateWithIngredients(
            "Zesty chickpea tray bake",
            [
                ("Chickpeas", "Tins & Dry Goods", 2m, "tins", 1.10m),
                ("Courgettes", "Produce", 2m, "pcs", 1.00m),
                ("Lemon", "Produce", 1m, "pcs", 0.40m)
            ]);

        InvokeAddMealsToAiPool([currentMeal, seenMeal, unseenMeal]);

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"]
        };

        var swappedPlan = _service.SwapMealForDay(
            request,
            dayIndex: 0,
            currentMealName: "Current sesame chicken bowl",
            currentPlanMealNames: ["Current sesame chicken bowl"],
            seenMealNames: ["Current sesame chicken bowl", "Aubergine chicken skillet"]);

        Assert.Single(swappedPlan.MealPlan);
        Assert.Equal("Zesty chickpea tray bake", swappedPlan.MealPlan[0].MealName);
    }

    [Fact]
    public void SwapMealForDay_WhenWarmAiPoolCanSatisfySwap_DoesNotRequireFirestoreHydration()
    {
        ClearAiPool();

        var currentMeal = CreateMealTemplateWithIngredients(
            "Chicken stir fry with rice",
            [
                ("Chicken breast", "Meat & Fish", 2m, "fillets", 3.40m),
                ("Rice", "Tins & Dry Goods", 180m, "g", 0.60m),
                ("Broccoli", "Produce", 1m, "head", 0.85m)
            ]);

        var replacementMeal = CreateMealTemplateWithIngredients(
            "Egg fried rice",
            [
                ("Eggs", "Dairy & Eggs", 4m, "pcs", 1.20m),
                ("Rice", "Tins & Dry Goods", 180m, "g", 0.55m),
                ("Frozen mixed veg", "Frozen", 0.3m, "bag", 0.85m)
            ]);

        InvokeAddMealsToAiPool([currentMeal, replacementMeal]);

        var request = new AislePilotRequestModel
        {
            Supermarket = "Custom",
            CustomAisleOrder = "Produce, Bakery, Dairy & Eggs",
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"]
        };

        var service = new AislePilotService(
            configuration: BuildFastPathConfiguration(),
            db: CreateUnreachableFirestoreDb(),
            webHostEnvironment: CreateTestWebHostEnvironment());

        var swappedPlan = service.SwapMealForDay(
            request,
            dayIndex: 0,
            currentMealName: "Chicken stir fry with rice",
            currentPlanMealNames: ["Chicken stir fry with rice"],
            seenMealNames: ["Chicken stir fry with rice"]);

        Assert.Single(swappedPlan.MealPlan);
        Assert.Equal("Egg fried rice", swappedPlan.MealPlan[0].MealName);
    }

    [Fact]
    public void SwapMealForDay_WhenCandidateSharesKeyIngredient_PrefersMoreAdventurousReplacement()
    {
        ClearAiPool();

        var currentMeal = CreateMealTemplateWithIngredients(
            "Mackerel rice bowl",
            [
                ("Mackerel fillets", "Meat & Fish", 2m, "fillets", 4.20m),
                ("Rice", "Tins & Dry Goods", 180m, "g", 0.55m),
                ("Spinach", "Produce", 1m, "bag", 0.95m)
            ]);
        var similarMeal = CreateMealTemplateWithIngredients(
            "Aubergine mackerel skillet",
            [
                ("Mackerel", "Meat & Fish", 2m, "fillets", 4.10m),
                ("Aubergine", "Produce", 1m, "pcs", 0.90m),
                ("Tomatoes", "Produce", 2m, "pcs", 0.70m)
            ]);
        var adventurousMeal = CreateMealTemplateWithIngredients(
            "Zesty chickpea tray bake",
            [
                ("Chickpeas", "Tins & Dry Goods", 2m, "tins", 1.10m),
                ("Courgettes", "Produce", 2m, "pcs", 1.00m),
                ("Lemon", "Produce", 1m, "pcs", 0.40m)
            ]);

        InvokeAddMealsToAiPool([currentMeal, similarMeal, adventurousMeal]);

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"]
        };

        var swappedPlan = _service.SwapMealForDay(
            request,
            dayIndex: 0,
            currentMealName: "Mackerel rice bowl",
            currentPlanMealNames: ["Mackerel rice bowl"],
            seenMealNames: ["Mackerel rice bowl"]);

        Assert.Single(swappedPlan.MealPlan);
        Assert.Equal("Zesty chickpea tray bake", swappedPlan.MealPlan[0].MealName);
        Assert.NotEqual("Aubergine mackerel skillet", swappedPlan.MealPlan[0].MealName);
    }

    [Fact]
    public void BuildPlan_WhenWarmAiPoolCanCoverRequest_DoesNotRequireFirestoreHydration()
    {
        ClearAiPool();

        var pooledMeal = CreateMealTemplateWithIngredients(
            "Egg fried rice",
            [
                ("Eggs", "Dairy & Eggs", 4m, "pcs", 1.20m),
                ("Rice", "Tins & Dry Goods", 180m, "g", 0.55m),
                ("Frozen mixed veg", "Frozen", 0.3m, "bag", 0.85m)
            ]);
        InvokeAddMealsToAiPool([pooledMeal]);

        var request = new AislePilotRequestModel
        {
            Supermarket = "Custom",
            CustomAisleOrder = "Produce, Bakery, Dairy & Eggs",
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"]
        };

        var service = new AislePilotService(
            configuration: BuildFastPathConfiguration(),
            db: CreateUnreachableFirestoreDb(),
            webHostEnvironment: CreateTestWebHostEnvironment());

        var plan = service.BuildPlan(request);

        Assert.Single(plan.MealPlan);
        Assert.Equal("Egg fried rice", plan.MealPlan[0].MealName);
        Assert.Equal("AI meal pool", plan.PlanSourceLabel);
    }

    [Fact]
    public void SwapMealForDay_BeforeThirdAttempt_DoesNotGenerateFreshAiMeal()
    {
        ClearAiPool();

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var initialPlan = _service.BuildPlan(request);
        Assert.Single(initialPlan.MealPlan);
        var currentMealName = initialPlan.MealPlan[0].MealName;
        var currentPlanMealNames = initialPlan.MealPlan.Select(meal => meal.MealName).ToList();

        ClearAiPool();

        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(new
                        {
                            name = $"Should not be used {Guid.NewGuid():N}",
                            baseCostForTwo = 5.6m,
                            isQuick = true,
                            tags = new[] { "Balanced" },
                            recipeSteps = new[]
                            {
                                "Warm the pan.",
                                "Cook the vegetables.",
                                "Fold through the protein.",
                                "Season well.",
                                "Serve immediately."
                            },
                            ingredients = new object[]
                            {
                                new { name = "Eggs", department = "Dairy & Eggs", quantityForTwo = 4m, unit = "pcs", estimatedCostForTwo = 1.2m }
                            }
                        })
                    }
                }
            }
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var swappedPlan = service.SwapMealForDay(
            request,
            dayIndex: 0,
            currentMealName,
            currentPlanMealNames,
            [currentMealName]);

        Assert.Equal(0, handler.CallCount);
        Assert.NotEqual(currentMealName, swappedPlan.MealPlan[0].MealName);
        Assert.Equal("Template swap", swappedPlan.PlanSourceLabel);
    }

    [Fact]
    public void SwapMealForDay_BeforeThirdAttempt_WhenNoCachedOrTemplateReplacementExists_UsesFreshAiMeal()
    {
        ClearAiPool();

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var initialPlan = _service.BuildPlan(request);
        Assert.Single(initialPlan.MealPlan);
        var currentMealName = initialPlan.MealPlan[0].MealName;
        var currentPlanMealNames = initialPlan.MealPlan.Select(meal => meal.MealName).ToList();
        var seenMealNames = GetCompatibleTemplateMealNamesForSlot(
                request.DietaryModes,
                request.DislikesOrAllergens,
                "Breakfast")
            .ToList();

        var uniqueMealName = $"Turmeric chickpea breakfast scramble {Guid.NewGuid():N}";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = JsonSerializer.Serialize(new
                        {
                            name = uniqueMealName,
                            baseCostForTwo = 5.4m,
                            isQuick = true,
                            tags = new[] { "Balanced" },
                            recipeSteps = new[]
                            {
                                "Warm a non-stick pan over a medium heat.",
                                "Cook the chickpeas with turmeric and smoked paprika for two minutes.",
                                "Stir in the spinach until wilted and tender.",
                                "Fold in the eggs and scramble until softly set.",
                                "Season with black pepper and serve straight away."
                            },
                            ingredients = new object[]
                            {
                                new { name = "Eggs", department = "Dairy & Eggs", quantityForTwo = 4m, unit = "pcs", estimatedCostForTwo = 1.2m },
                                new { name = "Chickpeas", department = "Tins & Dry Goods", quantityForTwo = 1m, unit = "tin", estimatedCostForTwo = 0.7m },
                                new { name = "Spinach", department = "Produce", quantityForTwo = 0.18m, unit = "kg", estimatedCostForTwo = 0.9m },
                                new { name = "Smoked Paprika", department = "Spices & Sauces", quantityForTwo = 2m, unit = "g", estimatedCostForTwo = 0.10m },
                                new { name = "Turmeric", department = "Spices & Sauces", quantityForTwo = 2m, unit = "g", estimatedCostForTwo = 0.10m },
                                new { name = "Black pepper", department = "Spices & Sauces", quantityForTwo = 0.5m, unit = "g", estimatedCostForTwo = 0.00m }
                            }
                        })
                    }
                }
            }
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var swappedPlan = service.SwapMealForDay(
            request,
            dayIndex: 0,
            currentMealName,
            currentPlanMealNames,
            seenMealNames);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(uniqueMealName, swappedPlan.MealPlan[0].MealName);
        Assert.Equal("OpenAI swap", swappedPlan.PlanSourceLabel);
    }

    [Fact]
    public void SwapMealForDay_WhenAiSwapFirstResponseRepeatsMeal_RetriesUntilUniqueReplacement()
    {
        ClearAiPool();

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var initialPlan = _service.BuildPlan(request);
        Assert.Single(initialPlan.MealPlan);
        var currentMealName = initialPlan.MealPlan[0].MealName;
        request.SwapHistoryState = $"0:Earlier breakfast one|Earlier breakfast two|{currentMealName}";
        var currentPlanMealNames = initialPlan.MealPlan.Select(meal => meal.MealName).ToList();
        var seenMealNames = GetCompatibleTemplateMealNamesForSlot(
                request.DietaryModes,
                request.DislikesOrAllergens,
                "Breakfast")
            .Where(name => !name.Equals(currentMealName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var duplicateResponseContent = JsonSerializer.Serialize(new
        {
            name = currentMealName,
            baseCostForTwo = 4.8m,
            isQuick = true,
            tags = new[] { "Balanced" },
            recipeSteps = new[]
            {
                "Toast oats and seeds in a dry pan for two minutes.",
                "Warm the fruit gently until softened and juicy.",
                "Fold the yogurt through the oats until creamy.",
                "Top with the fruit and a spoonful of seeds.",
                "Serve straight away while chilled ingredients stay cool."
            },
            ingredients = new object[]
            {
                new { name = "Greek yogurt", department = "Dairy & Eggs", quantityForTwo = 0.3m, unit = "kg", estimatedCostForTwo = 1.4m },
                new { name = "Rolled oats", department = "Tins & Dry Goods", quantityForTwo = 0.14m, unit = "kg", estimatedCostForTwo = 0.4m },
                new { name = "Blueberries", department = "Produce", quantityForTwo = 0.15m, unit = "kg", estimatedCostForTwo = 1.1m }
            }
        });
        var uniqueMealName = $"Pear quinoa breakfast bowl {Guid.NewGuid():N}";
        var uniqueResponseContent = JsonSerializer.Serialize(new
        {
            name = uniqueMealName,
            baseCostForTwo = 5.6m,
            isQuick = true,
            tags = new[] { "Balanced" },
            recipeSteps = new[]
            {
                "Rinse the quinoa well and simmer it until fluffy.",
                "Slice the pears and warm them gently with cinnamon until soft.",
                "Stir almond butter through the warm quinoa until glossy.",
                "Spoon over the pears and sprinkle over chopped almonds.",
                "Serve warm as a breakfast bowl."
            },
            ingredients = new object[]
            {
                new { name = "Quinoa", department = "Tins & Dry Goods", quantityForTwo = 0.18m, unit = "kg", estimatedCostForTwo = 0.9m },
                new { name = "Pears", department = "Produce", quantityForTwo = 2m, unit = "pcs", estimatedCostForTwo = 0.8m },
                new { name = "Almond butter", department = "Spices & Sauces", quantityForTwo = 0.12m, unit = "jar", estimatedCostForTwo = 1.4m }
            }
        });

        var duplicateResponseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = duplicateResponseContent
                    }
                }
            }
        });
        var uniqueResponseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = uniqueResponseContent
                    }
                }
            }
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        using var handler = new SequentialResponseHandler(
            (HttpStatusCode.OK, duplicateResponseBody),
            (HttpStatusCode.OK, uniqueResponseBody));
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        try
        {
            var swappedPlan = service.SwapMealForDay(
                request,
                dayIndex: 0,
                currentMealName,
                currentPlanMealNames,
                seenMealNames);

            Assert.Equal(2, handler.CallCount);
            Assert.NotEqual(currentMealName, swappedPlan.MealPlan[0].MealName);
            Assert.Equal(uniqueMealName, swappedPlan.MealPlan[0].MealName);
            Assert.Equal("Breakfast", swappedPlan.MealPlan[0].MealType);
        }
        finally
        {
            ClearAiPool();
        }
    }

    [Fact]
    public void SwapMealForDay_WhenAiSwapIncludesSpoonMeasuredSeasonings_AcceptsReplacementWithoutRetry()
    {
        ClearAiPool();

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var initialPlan = _service.BuildPlan(request);
        Assert.Single(initialPlan.MealPlan);
        var currentMealName = initialPlan.MealPlan[0].MealName;
        request.SwapHistoryState = $"0:Earlier breakfast one|Earlier breakfast two|{currentMealName}";
        var currentPlanMealNames = initialPlan.MealPlan.Select(meal => meal.MealName).ToList();
        var seenMealNames = GetCompatibleTemplateMealNamesForSlot(
                request.DietaryModes,
                request.DislikesOrAllergens,
                "Breakfast")
            .Where(name => !name.Equals(currentMealName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var uniqueMealName = $"Turmeric chickpea breakfast scramble {Guid.NewGuid():N}";
        var aiResponseContent = JsonSerializer.Serialize(new
        {
            name = uniqueMealName,
            baseCostForTwo = 5.4m,
            isQuick = true,
            tags = new[] { "Balanced" },
            recipeSteps = new[]
            {
                "Warm a non-stick pan over a medium heat.",
                "Cook the chickpeas with turmeric and smoked paprika for two minutes.",
                "Stir in the spinach until wilted and tender.",
                "Fold in the eggs and scramble until softly set.",
                "Season with black pepper and serve straight away."
            },
            ingredients = new object[]
            {
                new { name = "Eggs", department = "Dairy & Eggs", quantityForTwo = 4m, unit = "pcs", estimatedCostForTwo = 1.2m },
                new { name = "Chickpeas", department = "Tins & Dry Goods", quantityForTwo = 1m, unit = "tin", estimatedCostForTwo = 0.7m },
                new { name = "Spinach", department = "Produce", quantityForTwo = 0.18m, unit = "kg", estimatedCostForTwo = 0.9m },
                new { name = "Smoked Paprika", department = "Spices & Sauces", quantityForTwo = 1m, unit = "tsp", estimatedCostForTwo = 0.02m },
                new { name = "Turmeric", department = "Spices & Sauces", quantityForTwo = 1m, unit = "tsp", estimatedCostForTwo = 0.02m },
                new { name = "Salt", department = "Spices & Sauces", quantityForTwo = 0.5m, unit = "tsp", estimatedCostForTwo = 0.01m }
            }
        });
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = aiResponseContent
                    }
                }
            }
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        try
        {
            var swappedPlan = service.SwapMealForDay(
                request,
                dayIndex: 0,
                currentMealName,
                currentPlanMealNames,
                seenMealNames);

            Assert.Equal(1, handler.CallCount);
            Assert.Equal(uniqueMealName, swappedPlan.MealPlan[0].MealName);
            Assert.Equal("Breakfast", swappedPlan.MealPlan[0].MealType);
        }
        finally
        {
            ClearAiPool();
        }
    }
}
