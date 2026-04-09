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
}
