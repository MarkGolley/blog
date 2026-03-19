using System.Globalization;
using MyBlog.Models;
using MyBlog.Services;
using MyBlog.Utilities;

namespace MyBlog.Tests;

public class AislePilotServiceTests
{
    private readonly AislePilotService _service = new();

    [Theory]
    [InlineData(0.32, "kg", "320 g")]
    [InlineData(1.00, "kg", "1000 g")]
    [InlineData(1.24, "kg", "1.2 kg")]
    [InlineData(2.06, "kg", "2.1 kg")]
    [InlineData(0.45, "bottle", "0.45 bottle")]
    [InlineData(6, "pcs", "6 pcs")]
    public void QuantityDisplayFormatter_FormatsQuantitiesAsExpected(decimal quantity, string unit, string expected)
    {
        var formatted = QuantityDisplayFormatter.Format(quantity, unit);

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void BuildPlan_Pescatarian_ShowsPrawnsInGramsInMealAndShoppingViews()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Pescatarian"],
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            CookDays = 7
        };

        var result = _service.BuildPlan(request);
        var prawnMeal = result.MealPlan.FirstOrDefault(meal =>
            meal.MealName.Equals("Prawn tomato pasta", StringComparison.OrdinalIgnoreCase));
        var prawnShoppingItem = Assert.Single(result.ShoppingItems, item =>
            item.Name.Equals("King prawns", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(prawnMeal);
        Assert.Contains("320 g King prawns", prawnMeal!.IngredientLines, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(" g", prawnShoppingItem.QuantityDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kg", prawnShoppingItem.QuantityDisplay, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasCompatibleMeals_ConflictingDietaryModes_ReturnsFalse()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegan", "Pescatarian"]
        };

        var hasCompatibleMeals = _service.HasCompatibleMeals(request);

        Assert.False(hasCompatibleMeals);
    }

    [Fact]
    public void BuildPlan_WithConflictingDietaryModes_ThrowsInvalidOperationException()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegan", "Pescatarian"]
        };

        Assert.Throws<InvalidOperationException>(() => _service.BuildPlan(request));
    }

    [Fact]
    public void BuildPlan_WithStrictDietaryModes_OnlyUsesMatchingMeals()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            WeeklyBudget = 70m,
            HouseholdSize = 2
        };

        var result = _service.BuildPlan(request);
        var allowedMeals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Veggie lentil curry",
            "Paneer tikka tray bake",
            "Chickpea quinoa salad bowls"
        };

        Assert.Equal(7, result.MealPlan.Count);
        Assert.All(result.MealPlan, meal => Assert.Contains(meal.MealName, allowedMeals));
    }

    [Fact]
    public void BuildPlan_WithFiveCookDays_ReturnsFiveMealsAndTwoLeftoverDays()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 5
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(5, result.MealPlan.Count);
        Assert.Equal(5, result.CookDays);
        Assert.Equal(2, result.LeftoverDays);
        Assert.Equal(7, result.MealPlan.Sum(meal => 1 + meal.LeftoverDaysCovered));
        Assert.Contains(result.MealPlan, meal => meal.LeftoverDaysCovered > 0);
        Assert.All(result.MealPlan, meal => Assert.Equal(0, meal.EstimatedPrepMinutes % 5));
    }

    [Fact]
    public void BuildPlan_WithRequestedLeftoverSourceDays_AppliesCustomDistribution()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 5,
            LeftoverCookDayIndexesCsv = "4,4"
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(5, result.MealPlan.Count);
        Assert.Equal(2, result.LeftoverDays);
        Assert.Equal(0, result.MealPlan[0].LeftoverDaysCovered);
        Assert.Equal(0, result.MealPlan[1].LeftoverDaysCovered);
        Assert.Equal(0, result.MealPlan[2].LeftoverDaysCovered);
        Assert.Equal(0, result.MealPlan[3].LeftoverDaysCovered);
        Assert.Equal(2, result.MealPlan[4].LeftoverDaysCovered);
    }

    [Fact]
    public void BuildPlan_WithFiveCookDays_SpreadsCookSessionsAcrossWeekFromLeftoverAllocation()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 5
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(5, result.MealPlan.Count);
        Assert.Equal("Monday", result.MealPlan[0].Day);
        Assert.Equal("Wednesday", result.MealPlan[1].Day);
        Assert.Equal("Friday", result.MealPlan[2].Day);
        Assert.Equal("Saturday", result.MealPlan[3].Day);
        Assert.Equal("Sunday", result.MealPlan[4].Day);
    }

    [Fact]
    public void BuildPlan_WithOneCookDay_ScalesShoppingListToMatchSevenCookSessions()
    {
        var baseRequest = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            DislikesOrAllergens = "paneer, chickpea, quinoa",
            WeeklyBudget = 70m,
            HouseholdSize = 2
        };

        var oneCookDayRequest = new AislePilotRequestModel
        {
            Supermarket = baseRequest.Supermarket,
            WeeklyBudget = baseRequest.WeeklyBudget,
            HouseholdSize = baseRequest.HouseholdSize,
            CookDays = 1,
            DietaryModes = [.. baseRequest.DietaryModes],
            DislikesOrAllergens = baseRequest.DislikesOrAllergens
        };
        var sevenCookDayRequest = new AislePilotRequestModel
        {
            Supermarket = baseRequest.Supermarket,
            WeeklyBudget = baseRequest.WeeklyBudget,
            HouseholdSize = baseRequest.HouseholdSize,
            CookDays = 7,
            DietaryModes = [.. baseRequest.DietaryModes],
            DislikesOrAllergens = baseRequest.DislikesOrAllergens
        };

        var oneCookDayResult = _service.BuildPlan(oneCookDayRequest);
        var sevenCookDayResult = _service.BuildPlan(sevenCookDayRequest);

        Assert.Single(oneCookDayResult.MealPlan);
        Assert.Equal(6, oneCookDayResult.MealPlan[0].LeftoverDaysCovered);
        Assert.Equal(7, sevenCookDayResult.MealPlan.Count);
        Assert.All(sevenCookDayResult.MealPlan, meal =>
            Assert.Equal(oneCookDayResult.MealPlan[0].MealName, meal.MealName));

        Assert.Equal(sevenCookDayResult.ShoppingItems.Count, oneCookDayResult.ShoppingItems.Count);
        foreach (var oneCookDayItem in oneCookDayResult.ShoppingItems)
        {
            var sevenCookDayItem = Assert.Single(sevenCookDayResult.ShoppingItems, item =>
                item.Department.Equals(oneCookDayItem.Department, StringComparison.OrdinalIgnoreCase) &&
                item.Name.Equals(oneCookDayItem.Name, StringComparison.OrdinalIgnoreCase) &&
                item.Unit.Equals(oneCookDayItem.Unit, StringComparison.OrdinalIgnoreCase));

            Assert.Equal(oneCookDayItem.Quantity, sevenCookDayItem.Quantity);
            Assert.Equal(oneCookDayItem.EstimatedCost, sevenCookDayItem.EstimatedCost);
        }
    }

    [Fact]
    public void BuildPlan_CustomAisleOrderWithAtLeastThreeAisles_UsesCustomOrder()
    {
        var request = new AislePilotRequestModel
        {
            Supermarket = "Custom",
            CustomAisleOrder = "Frozen, Produce, Dairy & Eggs",
            DietaryModes = ["Balanced"]
        };

        var result = _service.BuildPlan(request);

        Assert.Equal("Frozen", result.AisleOrderUsed[0]);
        Assert.Equal("Produce", result.AisleOrderUsed[1]);
        Assert.Equal("Dairy & Eggs", result.AisleOrderUsed[2]);
        Assert.Contains("Other", result.AisleOrderUsed);
    }

    [Fact]
    public void BuildPlan_BudgetTips_UseUkCurrencyFormatting_WhenCurrentCultureIsDifferent()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            var request = new AislePilotRequestModel
            {
                WeeklyBudget = 15m,
                HouseholdSize = 8,
                DietaryModes = ["Balanced"]
            };

            var result = _service.BuildPlan(request);
            var firstTip = result.BudgetTips.FirstOrDefault() ?? string.Empty;

            Assert.Contains("£", firstTip);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

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
    public void SuggestMealsFromPantry_WithSpecificProteinAndStaple_ReturnsEmptyWhenMealIsNotFullyCovered()
    {
        var request = new AislePilotRequestModel
        {
            PantryItems = "chicken, rice",
            DietaryModes = ["Balanced"]
        };

        var suggestions = _service.SuggestMealsFromPantry(request, 8);

        Assert.Empty(suggestions);
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

        var swappedPlan = _service.SwapMealForDay(request, 0, currentMealName);

        Assert.Equal(7, swappedPlan.MealPlan.Count);
        Assert.NotEqual(currentMealName, swappedPlan.MealPlan[0].MealName);
    }

    [Fact]
    public void SwapMealForDay_InvalidDay_ThrowsArgumentOutOfRangeException()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SwapMealForDay(request, -1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SwapMealForDay(request, 7, null));
    }

    [Fact]
    public void SwapMealForDay_WhenCookDaysIsFive_DayFiveThrowsArgumentOutOfRangeException()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 5
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.SwapMealForDay(request, 5, null));
    }

    [Fact]
    public void SwapMealForDay_StrictDietaryModes_StillHonorsConstraints()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            WeeklyBudget = 70m,
            HouseholdSize = 2
        };

        var initialPlan = _service.BuildPlan(request);
        var currentMealName = initialPlan.MealPlan[2].MealName;
        var swappedPlan = _service.SwapMealForDay(request, 2, currentMealName);
        var allowedMeals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Veggie lentil curry",
            "Paneer tikka tray bake",
            "Chickpea quinoa salad bowls"
        };

        Assert.All(swappedPlan.MealPlan, meal => Assert.Contains(meal.MealName, allowedMeals));
    }
}
