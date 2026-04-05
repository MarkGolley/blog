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

public class AislePilotServiceTests
{
    private readonly AislePilotService _service = new();

    [Theory]
    [InlineData(0.32, "kg", "320 g")]
    [InlineData(1.00, "kg", "1000 g")]
    [InlineData(1.24, "kg", "1.2 kg")]
    [InlineData(2.06, "kg", "2.1 kg")]
    [InlineData(0.45, "bottle", "0.45 bottle")]
    [InlineData(1.2, "bottle", "1.2 bottles")]
    [InlineData(0.06, "jar", "0.06 jar")]
    [InlineData(6, "pcs", "6 pcs")]
    public void QuantityDisplayFormatter_FormatsQuantitiesAsExpected(decimal quantity, string unit, string expected)
    {
        var formatted = QuantityDisplayFormatter.Format(quantity, unit);

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(0.45, "bottle", "225 ml")]
    [InlineData(0.06, "jar", "15 ml")]
    [InlineData(0.19, "pot", "60 ml")]
    [InlineData(0.18, "L", "180 ml")]
    [InlineData(1.5, "litres", "1500 ml")]
    [InlineData(1.24, "kg", "1.2 kg")]
    public void QuantityDisplayFormatter_RecipeFormatting_PreservesPreciseAmounts(decimal quantity, string unit, string expected)
    {
        var formatted = QuantityDisplayFormatter.FormatForRecipe(quantity, unit);

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void QuantityDisplayFormatter_ShoppingListFormatting_StillRoundsWholePurchaseUnits()
    {
        var formatted = QuantityDisplayFormatter.Format(0.45m, "tin");

        Assert.Equal("1 tin", formatted);
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
            "Chickpea quinoa salad bowls",
            "Black bean sweet potato chilli",
            "Mushroom spinach risotto",
            "Halloumi and harissa roast veg tray bake"
        };

        Assert.Equal(7, result.MealPlan.Count);
        Assert.All(result.MealPlan, meal => Assert.Contains(meal.MealName, allowedMeals));
    }

    [Fact]
    public void BuildPlan_VegetarianWithSevenCookDays_DoesNotRepeatMealsWithinTheWeek()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian"],
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            CookDays = 7
        };

        var result = _service.BuildPlan(request);
        var distinctMealCount = result.MealPlan
            .Select(meal => meal.MealName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.Equal(7, result.MealPlan.Count);
        Assert.Equal(result.MealPlan.Count, distinctMealCount);
    }

    [Fact]
    public void BuildPlan_VeganWithSevenCookDays_DoesNotRepeatMealsWithinTheWeek()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegan"],
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            CookDays = 7
        };

        var result = _service.BuildPlan(request);
        var distinctMealCount = result.MealPlan
            .Select(meal => meal.MealName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.Equal(7, result.MealPlan.Count);
        Assert.Equal(result.MealPlan.Count, distinctMealCount);
    }

    [Fact]
    public void BuildPlan_WhenUniqueMealsAreInsufficient_ReusesMealsOnlyAfterUsingAllAvailableOptions()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            CookDays = 7
        };

        var result = _service.BuildPlan(request);
        var distinctMealCount = result.MealPlan
            .Select(meal => meal.MealName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Assert.Equal(7, result.MealPlan.Count);
        Assert.Equal(6, distinctMealCount);
    }

    [Fact]
    public void BuildPlan_RecipeMethods_AreDetailedWithAtLeastFiveSteps()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            CookDays = 7
        };

        var result = _service.BuildPlan(request);

        Assert.NotEmpty(result.MealPlan);
        Assert.All(result.MealPlan, meal =>
        {
            Assert.True(meal.RecipeSteps.Count >= 5, $"Expected at least 5 steps for '{meal.MealName}' but got {meal.RecipeSteps.Count}.");
            Assert.All(meal.RecipeSteps, step => Assert.False(string.IsNullOrWhiteSpace(step)));
        });
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_TurkeyChilliRecipe_DoesNotMentionMissingOnionOrGarlic()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 200m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"]
        };

        var result = await _service.BuildPlanFromCurrentMealsAsync(request, ["Turkey chilli with beans"]);

        Assert.Single(result.MealPlan);
        Assert.Equal("Turkey chilli with beans", result.MealPlan[0].MealName, ignoreCase: true);
        Assert.DoesNotContain(
            result.MealPlan[0].RecipeSteps,
            step => step.Contains("onion", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.MealPlan[0].RecipeSteps,
            step => step.Contains("garlic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_PopulatesCaloriesAndMacros_ForEveryMeal()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            CookDays = 7
        };

        var result = _service.BuildPlan(request);

        Assert.NotEmpty(result.MealPlan);
        Assert.All(result.MealPlan, meal =>
        {
            Assert.True(meal.CaloriesPerServing > 0);
            Assert.True(meal.ProteinGramsPerServing > 0);
            Assert.True(meal.CarbsGramsPerServing > 0);
            Assert.True(meal.FatGramsPerServing > 0);
        });
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
    public void BuildPlan_WithThreeMealsPerDay_ReturnsMealsInBreakfastLunchDinnerOrderPerCookDay()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 3,
            CookDays = 3,
            MealsPerDay = 3
        };

        var result = _service.BuildPlan(request);
        var groupedByDay = result.MealPlan
            .GroupBy(meal => meal.Day)
            .Select(group => group.Select(meal => meal.MealType).ToList())
            .ToList();

        Assert.Equal(3, result.MealsPerDay);
        Assert.Equal(9, result.MealPlan.Count);
        Assert.Equal(3, groupedByDay.Count);
        Assert.All(groupedByDay, mealTypes =>
            Assert.Equal(["Breakfast", "Lunch", "Dinner"], mealTypes));
    }

    [Fact]
    public void BuildPlan_WithThreeMealsPerDay_UsesBreakfastAndLunchAppropriateMeals()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 3,
            CookDays = 3,
            MealsPerDay = 3
        };

        var result = _service.BuildPlan(request);
        var breakfastFriendlyMeals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Greek yogurt berry oat pots",
            "Spinach and tomato egg muffins",
            "Tofu spinach breakfast scramble",
            "Smoked salmon spinach egg scramble",
            "Smoked salmon scrambled eggs on toast"
        };
        var lunchFriendlyMeals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Greek yogurt chicken wraps",
            "Chickpea quinoa salad bowls",
            "Egg fried rice",
            "Halloumi couscous bowls",
            "Spinach and tomato egg muffins",
            "Tofu spinach breakfast scramble",
            "Smoked salmon scrambled eggs on toast",
            "Smoked salmon spinach egg scramble",
            "Mediterranean hummus wraps",
            "Tuna sweetcorn pasta salad",
            "Chicken couscous lunch bowls",
            "Lentil vegetable soup bowls"
        };

        var breakfastMeals = result.MealPlan
            .Where(meal => meal.MealType.Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
            .Select(meal => meal.MealName)
            .ToList();
        var lunchMeals = result.MealPlan
            .Where(meal => meal.MealType.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
            .Select(meal => meal.MealName)
            .ToList();

        Assert.Equal(3, breakfastMeals.Count);
        Assert.Equal(3, lunchMeals.Count);
        Assert.All(breakfastMeals, mealName => Assert.Contains(mealName, breakfastFriendlyMeals));
        Assert.All(lunchMeals, mealName => Assert.Contains(mealName, lunchFriendlyMeals));
    }

    [Fact]
    public void BuildPlan_WithThreeMealsPerDay_DinnerSlotsRemainUniqueAcrossCookDays()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 7,
            CookDays = 7,
            MealsPerDay = 3
        };

        var result = _service.BuildPlan(request);
        var dinnerMeals = result.MealPlan
            .Where(meal => meal.MealType.Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .Select(meal => meal.MealName)
            .ToList();

        Assert.Equal(7, dinnerMeals.Count);
        Assert.Equal(dinnerMeals.Count, dinnerMeals.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void BuildPlan_WithThreeMealsPerDay_LunchSlotsDoNotRepeatSingleMealAcrossWholeWeek()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 7,
            CookDays = 7,
            MealsPerDay = 3
        };

        var result = _service.BuildPlan(request);
        var lunchMeals = result.MealPlan
            .Where(meal => meal.MealType.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
            .Select(meal => meal.MealName)
            .ToList();
        var maxLunchRepeatCount = lunchMeals
            .GroupBy(mealName => mealName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();

        Assert.Equal(7, lunchMeals.Count);
        Assert.True(
            maxLunchRepeatCount <= 2,
            $"Expected each lunch to appear at most twice, but found a repeat count of {maxLunchRepeatCount}.");
    }

    [Fact]
    public void BuildPlan_WithSavedMealRepeatsEnabled_PrefersSavedMealInWeeklyPlan()
    {
        const string savedMealName = "Greek yogurt berry oat pots";
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 7,
            CookDays = 7,
            MealsPerDay = 3,
            SelectedMealTypes = ["Breakfast", "Lunch", "Dinner"],
            EnableSavedMealRepeats = true,
            SavedMealRepeatRatePercent = 100,
            SavedEnjoyedMealNamesState = JsonSerializer.Serialize(new[] { savedMealName })
        };

        var result = _service.BuildPlan(request);
        var savedMealCount = result.MealPlan.Count(meal =>
            meal.MealName.Equals(savedMealName, StringComparison.OrdinalIgnoreCase));

        Assert.True(
            savedMealCount >= 2,
            $"Expected saved meal '{savedMealName}' to repeat at least twice, but it appeared {savedMealCount} time(s).");
    }

    [Fact]
    public void BuildPlan_WithThreeMealsPerDay_Vegan_KeepsBreakfastAndLunchSlotAppropriate()
    {
        static bool IsBreakfastLike(string mealName)
        {
            var keywords = new[]
            {
                "breakfast", "oat", "porridge", "granola", "muesli", "omelette", "omelet",
                "scrambled egg", "yogurt", "yoghurt", "toast", "pancake", "chia"
            };

            return keywords.Any(keyword => mealName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        static bool IsLunchLike(string mealName)
        {
            var keywords = new[]
            {
                "lunch", "salad", "pasta salad", "wrap", "wraps", "sandwich", "toastie",
                "panini", "soup", "couscous bowl", "couscous bowls", "grain bowl",
                "grain bowls", "poke bowl", "poke bowls"
            };

            return keywords.Any(keyword => mealName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegan"],
            PlanDays = 4,
            CookDays = 4,
            MealsPerDay = 3
        };

        var result = _service.BuildPlan(request);
        var breakfastMeals = result.MealPlan
            .Where(meal => meal.MealType.Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
            .Select(meal => meal.MealName)
            .ToList();
        var lunchMeals = result.MealPlan
            .Where(meal => meal.MealType.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
            .Select(meal => meal.MealName)
            .ToList();

        Assert.Equal(4, breakfastMeals.Count);
        Assert.Equal(4, lunchMeals.Count);
        Assert.All(breakfastMeals, mealName => Assert.True(IsBreakfastLike(mealName)));
        Assert.All(lunchMeals, mealName => Assert.True(IsLunchLike(mealName) || IsBreakfastLike(mealName)));
    }

    [Fact]
    public void BuildPlan_WithBreakfastOnlySlot_ReturnsBreakfastMeals()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(1, result.MealsPerDay);
        Assert.Equal(2, result.MealPlan.Count);
        Assert.All(result.MealPlan, meal => Assert.Equal("Breakfast", meal.MealType));
    }

    [Fact]
    public void HasCompatibleMeals_WithHighProteinBreakfastOnly_ReturnsTrue()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["High-Protein"],
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var hasCompatibleMeals = _service.HasCompatibleMeals(request);

        Assert.True(hasCompatibleMeals);
    }

    [Fact]
    public void BuildPlan_WithHighProteinBreakfastOnlySlot_ReturnsBreakfastMeals()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["High-Protein"],
            PlanDays = 3,
            CookDays = 3,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(1, result.MealsPerDay);
        Assert.Equal(3, result.MealPlan.Count);
        Assert.All(result.MealPlan, meal => Assert.Equal("Breakfast", meal.MealType));
    }

    [Fact]
    public void HasCompatibleMeals_WithHighProteinGlutenFreeBreakfastOnly_ReturnsTrue()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["High-Protein", "Gluten-Free"],
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var hasCompatibleMeals = _service.HasCompatibleMeals(request);

        Assert.True(hasCompatibleMeals);
    }

    [Fact]
    public void BuildPlan_WithHighProteinGlutenFreeBreakfastOnlySlot_ReturnsBreakfastMeals()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["High-Protein", "Gluten-Free"],
            PlanDays = 3,
            CookDays = 3,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(1, result.MealsPerDay);
        Assert.Equal(3, result.MealPlan.Count);
        Assert.All(result.MealPlan, meal => Assert.Equal("Breakfast", meal.MealType));
    }

    [Fact]
    public void HasCompatibleMeals_WithPescatarianGlutenFreeBreakfastOnly_ReturnsTrue()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Pescatarian", "Gluten-Free"],
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var hasCompatibleMeals = _service.HasCompatibleMeals(request);

        Assert.True(hasCompatibleMeals);
    }

    [Fact]
    public void BuildPlan_WithPescatarianGlutenFreeBreakfastOnlySlot_ReturnsBreakfastMeals()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Pescatarian", "Gluten-Free"],
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(1, result.MealsPerDay);
        Assert.Equal(2, result.MealPlan.Count);
        Assert.All(result.MealPlan, meal => Assert.Equal("Breakfast", meal.MealType));
    }

    [Fact]
    public void HasCompatibleMeals_WithBreakfastOnly_WhenHardModesConflict_ReturnsFalse()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegan", "Pescatarian"],
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var hasCompatibleMeals = _service.HasCompatibleMeals(request);

        Assert.False(hasCompatibleMeals);
    }

    [Fact]
    public void HasCompatibleMeals_WithBreakfastOnlyAndAiAvailable_AllowsAiAttemptWhenTemplatesDontMatch()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true"
            })
            .Build();
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler);
        var aiCapableService = new AislePilotService(httpClient, configuration);
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["High-Protein", "Pescatarian", "Gluten-Free"],
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var hasCompatibleMeals = aiCapableService.HasCompatibleMeals(request);

        Assert.True(hasCompatibleMeals);
    }

    [Fact]
    public void BuildPlan_WithBreakfastOnlyAndDinnerOnlyAiPool_FallsBackWithoutConstraintFailure()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var seedMealTemplate = GetMealTemplateSeed();
        var dinnerOnlyPoolNames = new[]
        {
            $"Weeknight roast bowl {Guid.NewGuid():N}",
            $"Hearty skillet supper {Guid.NewGuid():N}",
            $"Oven-baked comfort plate {Guid.NewGuid():N}"
        };

        InvokeAddMealsToAiPool(CreateMealTemplatesFromSeed(seedMealTemplate, dinnerOnlyPoolNames));

        try
        {
            var request = new AislePilotRequestModel
            {
                DietaryModes = ["High-Protein"],
                PlanDays = 2,
                CookDays = 2,
                MealsPerDay = 1,
                SelectedMealTypes = ["Breakfast"]
            };

            var result = service.BuildPlan(request);

            Assert.Equal(1, result.MealsPerDay);
            Assert.Equal(2, result.MealPlan.Count);
            Assert.All(result.MealPlan, meal => Assert.Equal("Breakfast", meal.MealType));
        }
        finally
        {
            ClearAiPool();
        }
    }

    [Fact]
    public void BuildPlan_WithLunchAndDinnerSlots_ReturnsLunchThenDinnerPerCookDay()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 2,
            SelectedMealTypes = ["Lunch", "Dinner"]
        };

        var result = _service.BuildPlan(request);
        var groupedByDay = result.MealPlan
            .GroupBy(meal => meal.Day)
            .Select(group => group.Select(meal => meal.MealType).ToList())
            .ToList();

        Assert.Equal(2, result.MealsPerDay);
        Assert.Equal(4, result.MealPlan.Count);
        Assert.All(groupedByDay, mealTypes => Assert.Equal(["Lunch", "Dinner"], mealTypes));
    }

    [Fact]
    public void SwapMealForDay_WithThreeMealsPerDay_AllowsSwappingAnyMealSlotIndex()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 3
        };

        var initialPlan = _service.BuildPlan(request);
        var currentMealName = initialPlan.MealPlan[4].MealName;
        var currentPlanMealNames = initialPlan.MealPlan
            .Select(meal => meal.MealName)
            .ToList();

        var swappedPlan = _service.SwapMealForDay(
            request,
            dayIndex: 4,
            currentMealName,
            currentPlanMealNames,
            seenMealNames: []);

        Assert.Equal(6, swappedPlan.MealPlan.Count);
        Assert.NotEqual(currentMealName, swappedPlan.MealPlan[4].MealName);
    }

    [Fact]
    public void SwapMealForDay_WithThreeMealsPerDay_KeepsBreakfastSwapBreakfastAppropriate()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 3
        };
        var breakfastFriendlyMeals = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Greek yogurt berry oat pots",
            "Spinach and tomato egg muffins",
            "Tofu spinach breakfast scramble",
            "Smoked salmon spinach egg scramble",
            "Smoked salmon scrambled eggs on toast"
        };

        var initialPlan = _service.BuildPlan(request);
        var breakfastSlotIndex = 2;
        var currentMealName = initialPlan.MealPlan[breakfastSlotIndex].MealName;
        var currentPlanMealNames = initialPlan.MealPlan
            .Select(meal => meal.MealName)
            .ToList();

        var swappedPlan = _service.SwapMealForDay(
            request,
            dayIndex: breakfastSlotIndex,
            currentMealName,
            currentPlanMealNames,
            seenMealNames: [currentMealName]);

        Assert.Equal(6, swappedPlan.MealPlan.Count);
        Assert.NotEqual(currentMealName, swappedPlan.MealPlan[breakfastSlotIndex].MealName);
        Assert.Equal("Breakfast", swappedPlan.MealPlan[breakfastSlotIndex].MealType);
        Assert.Contains(swappedPlan.MealPlan[breakfastSlotIndex].MealName, breakfastFriendlyMeals);
    }

    [Fact]
    public void BuildPlan_WithPlanLengthFiveAndFiveCookDays_ReturnsNoLeftovers()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 5,
            CookDays = 5
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(5, result.PlanDays);
        Assert.Equal(5, result.CookDays);
        Assert.Equal(0, result.LeftoverDays);
        Assert.Equal(5, result.MealPlan.Count);
        Assert.Equal(5, result.MealPlan.Sum(meal => 1 + meal.LeftoverDaysCovered));
    }

    [Fact]
    public void BuildPlan_WhenCookDaysExceedPlanLength_ClampsCookDaysToPlanLength()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 1,
            CookDays = 6
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(1, result.PlanDays);
        Assert.Equal(1, result.CookDays);
        Assert.Equal(0, result.LeftoverDays);
        Assert.Single(result.MealPlan);
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
            DislikesOrAllergens = "lentils, coconut milk, chickpea, quinoa, black beans, sweet potatoes, mushrooms, risotto rice, spinach",
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
    public void BuildPlan_Aldi_UsesNormalizedAisleOrderWithoutDuplicates()
    {
        var request = new AislePilotRequestModel
        {
            Supermarket = "Aldi",
            DietaryModes = ["Balanced"]
        };

        var result = _service.BuildPlan(request);

        Assert.Equal("Aldi", result.Supermarket);
        Assert.Equal(
            result.AisleOrderUsed.Count,
            result.AisleOrderUsed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Produce", result.AisleOrderUsed, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Bakery", result.AisleOrderUsed, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Other", result.AisleOrderUsed, StringComparer.OrdinalIgnoreCase);

        var expectedPrefix = new[]
        {
            "Produce",
            "Tins & Dry Goods",
            "Meat & Fish",
            "Dairy & Eggs",
            "Frozen",
            "Bakery"
        };
        for (var i = 0; i < expectedPrefix.Length; i++)
        {
            Assert.Equal(expectedPrefix[i], result.AisleOrderUsed[i]);
        }
    }

    [Fact]
    public void BuildPlan_ShoppingItems_FollowSelectedSupermarketAisleOrder()
    {
        var request = new AislePilotRequestModel
        {
            Supermarket = "Aldi",
            WeeklyBudget = 75m,
            HouseholdSize = 2,
            CookDays = 7,
            DietaryModes = ["Balanced"]
        };

        var result = _service.BuildPlan(request);
        var aisleRanks = result.AisleOrderUsed
            .Select((department, index) => new { department, index })
            .ToDictionary(x => x.department, x => x.index, StringComparer.OrdinalIgnoreCase);

        var previousRank = -1;
        foreach (var item in result.ShoppingItems)
        {
            var rank = aisleRanks.GetValueOrDefault(item.Department, int.MaxValue);
            Assert.True(rank >= previousRank);
            previousRank = rank;
        }
    }

    [Fact]
    public void BuildPlan_CustomAisleOrder_AliasNamesAreNormalized()
    {
        var request = new AislePilotRequestModel
        {
            Supermarket = "Custom",
            CustomAisleOrder = "produce, tins, dairy, frozen, bakery",
            DietaryModes = ["Balanced"]
        };

        var result = _service.BuildPlan(request);

        Assert.Equal("Produce", result.AisleOrderUsed[0]);
        Assert.Equal("Tins & Dry Goods", result.AisleOrderUsed[1]);
        Assert.Equal("Dairy & Eggs", result.AisleOrderUsed[2]);
        Assert.Equal("Frozen", result.AisleOrderUsed[3]);
        Assert.Equal("Bakery", result.AisleOrderUsed[4]);
    }

    [Fact]
    public void GetSupportedPortionSizes_ReturnsExpectedValues()
    {
        var options = _service.GetSupportedPortionSizes();

        Assert.Equal(["Small", "Medium", "Large"], options);
    }

    [Fact]
    public void BuildPlan_LargerPortionSize_IncreasesMealAndShoppingVolumes()
    {
        var baseRequest = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            DislikesOrAllergens = "lentils, coconut milk, chickpea, quinoa, black beans, sweet potatoes, mushrooms, risotto rice, spinach",
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            CookDays = 1
        };

        var mediumResult = _service.BuildPlan(new AislePilotRequestModel
        {
            Supermarket = baseRequest.Supermarket,
            WeeklyBudget = baseRequest.WeeklyBudget,
            HouseholdSize = baseRequest.HouseholdSize,
            CookDays = baseRequest.CookDays,
            PortionSize = "Medium",
            DietaryModes = [.. baseRequest.DietaryModes],
            DislikesOrAllergens = baseRequest.DislikesOrAllergens
        });

        var largeResult = _service.BuildPlan(new AislePilotRequestModel
        {
            Supermarket = baseRequest.Supermarket,
            WeeklyBudget = baseRequest.WeeklyBudget,
            HouseholdSize = baseRequest.HouseholdSize,
            CookDays = baseRequest.CookDays,
            PortionSize = "Large",
            DietaryModes = [.. baseRequest.DietaryModes],
            DislikesOrAllergens = baseRequest.DislikesOrAllergens
        });

        Assert.Single(mediumResult.MealPlan);
        Assert.Single(largeResult.MealPlan);
        Assert.Equal(mediumResult.MealPlan[0].MealName, largeResult.MealPlan[0].MealName);
        Assert.Equal("Medium", mediumResult.PortionSize);
        Assert.Equal("Large", largeResult.PortionSize);
        Assert.True(largeResult.EstimatedTotalCost > mediumResult.EstimatedTotalCost);

        Assert.Equal(mediumResult.ShoppingItems.Count, largeResult.ShoppingItems.Count);
        foreach (var mediumItem in mediumResult.ShoppingItems)
        {
            var largeItem = Assert.Single(largeResult.ShoppingItems, item =>
                item.Department.Equals(mediumItem.Department, StringComparison.OrdinalIgnoreCase) &&
                item.Name.Equals(mediumItem.Name, StringComparison.OrdinalIgnoreCase) &&
                item.Unit.Equals(mediumItem.Unit, StringComparison.OrdinalIgnoreCase));

            Assert.True(largeItem.Quantity > mediumItem.Quantity);
            Assert.True(largeItem.EstimatedCost > mediumItem.EstimatedCost);
            Assert.True(largeItem.Quantity <= decimal.Round(mediumItem.Quantity * 1.2m, 2, MidpointRounding.AwayFromZero));
        }
    }

    [Fact]
    public void BuildPlan_SmallerPortionSize_MateriallyReducesMealAndShoppingVolumes()
    {
        var baseRequest = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            DislikesOrAllergens = "lentils, coconut milk, chickpea, quinoa, black beans, sweet potatoes, mushrooms, risotto rice, spinach",
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            CookDays = 1
        };

        var smallResult = _service.BuildPlan(new AislePilotRequestModel
        {
            Supermarket = baseRequest.Supermarket,
            WeeklyBudget = baseRequest.WeeklyBudget,
            HouseholdSize = baseRequest.HouseholdSize,
            CookDays = baseRequest.CookDays,
            PortionSize = "Small",
            DietaryModes = [.. baseRequest.DietaryModes],
            DislikesOrAllergens = baseRequest.DislikesOrAllergens
        });

        var mediumResult = _service.BuildPlan(new AislePilotRequestModel
        {
            Supermarket = baseRequest.Supermarket,
            WeeklyBudget = baseRequest.WeeklyBudget,
            HouseholdSize = baseRequest.HouseholdSize,
            CookDays = baseRequest.CookDays,
            PortionSize = "Medium",
            DietaryModes = [.. baseRequest.DietaryModes],
            DislikesOrAllergens = baseRequest.DislikesOrAllergens
        });

        Assert.Single(smallResult.MealPlan);
        Assert.Single(mediumResult.MealPlan);
        Assert.Equal(smallResult.MealPlan[0].MealName, mediumResult.MealPlan[0].MealName);
        Assert.Equal("Small", smallResult.PortionSize);
        Assert.Equal("Medium", mediumResult.PortionSize);
        Assert.True(smallResult.EstimatedTotalCost < mediumResult.EstimatedTotalCost);
        Assert.True(smallResult.EstimatedTotalCost <= decimal.Round(mediumResult.EstimatedTotalCost * 0.8m, 2, MidpointRounding.AwayFromZero));

        Assert.Equal(mediumResult.ShoppingItems.Count, smallResult.ShoppingItems.Count);
        foreach (var mediumItem in mediumResult.ShoppingItems)
        {
            var smallItem = Assert.Single(smallResult.ShoppingItems, item =>
                item.Department.Equals(mediumItem.Department, StringComparison.OrdinalIgnoreCase) &&
                item.Name.Equals(mediumItem.Name, StringComparison.OrdinalIgnoreCase) &&
                item.Unit.Equals(mediumItem.Unit, StringComparison.OrdinalIgnoreCase));

            Assert.True(smallItem.Quantity < mediumItem.Quantity);
            Assert.True(smallItem.EstimatedCost < mediumItem.EstimatedCost);
            Assert.True(smallItem.Quantity <= decimal.Round(mediumItem.Quantity * 0.8m, 2, MidpointRounding.AwayFromZero));
        }
    }

    [Fact]
    public void BuildPlan_LargerPortionSize_IncreasesMealNutritionPerServing()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            DislikesOrAllergens = "lentils, coconut milk, chickpea, quinoa, black beans, sweet potatoes, mushrooms, risotto rice, spinach",
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            CookDays = 1
        };

        var medium = _service.BuildPlan(new AislePilotRequestModel
        {
            Supermarket = request.Supermarket,
            WeeklyBudget = request.WeeklyBudget,
            HouseholdSize = request.HouseholdSize,
            CookDays = request.CookDays,
            PortionSize = "Medium",
            DietaryModes = [.. request.DietaryModes],
            DislikesOrAllergens = request.DislikesOrAllergens,
            PreferQuickMeals = request.PreferQuickMeals
        });
        var large = _service.BuildPlan(new AislePilotRequestModel
        {
            Supermarket = request.Supermarket,
            WeeklyBudget = request.WeeklyBudget,
            HouseholdSize = request.HouseholdSize,
            CookDays = request.CookDays,
            PortionSize = "Large",
            DietaryModes = [.. request.DietaryModes],
            DislikesOrAllergens = request.DislikesOrAllergens,
            PreferQuickMeals = request.PreferQuickMeals
        });

        Assert.Single(medium.MealPlan);
        Assert.Single(large.MealPlan);
        Assert.Equal(medium.MealPlan[0].MealName, large.MealPlan[0].MealName);
        Assert.True(large.MealPlan[0].CaloriesPerServing > medium.MealPlan[0].CaloriesPerServing);
        Assert.True(large.MealPlan[0].ProteinGramsPerServing > medium.MealPlan[0].ProteinGramsPerServing);
        Assert.True(large.MealPlan[0].CarbsGramsPerServing > medium.MealPlan[0].CarbsGramsPerServing);
        Assert.True(large.MealPlan[0].FatGramsPerServing > medium.MealPlan[0].FatGramsPerServing);
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
    public void BuildPlanWithBudgetRebalance_WhenPlanIsOverBudget_DoesNotIncreaseTotalCost()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            HouseholdSize = 4,
            CookDays = 7
        };

        AislePilotPlanResultViewModel? baseline = null;
        AislePilotRequestModel? overBudgetRequest = null;
        foreach (var budget in Enumerable.Range(20, 61))
        {
            var candidateRequest = new AislePilotRequestModel
            {
                Supermarket = request.Supermarket,
                WeeklyBudget = budget,
                HouseholdSize = request.HouseholdSize,
                CookDays = request.CookDays,
                PortionSize = request.PortionSize,
                DietaryModes = [.. request.DietaryModes],
                PreferQuickMeals = request.PreferQuickMeals
            };

            var candidatePlan = _service.BuildPlan(candidateRequest);
            if (!candidatePlan.IsOverBudget)
            {
                continue;
            }

            overBudgetRequest = candidateRequest;
            baseline = candidatePlan;
            break;
        }

        Assert.NotNull(overBudgetRequest);
        Assert.NotNull(baseline);

        var rebalanced = _service.BuildPlanWithBudgetRebalance(overBudgetRequest!);

        Assert.Equal(overBudgetRequest!.WeeklyBudget, rebalanced.WeeklyBudget);
        Assert.True(rebalanced.EstimatedTotalCost <= baseline!.EstimatedTotalCost);
        Assert.True(rebalanced.BudgetRebalanceAttempted);
        Assert.False(string.IsNullOrWhiteSpace(rebalanced.BudgetRebalanceStatusMessage));
    }

    [Fact]
    public void BuildPlanWithBudgetRebalance_WhenPlanChangesMealMix_TotalCostDrops()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            HouseholdSize = 4,
            CookDays = 7
        };

        AislePilotPlanResultViewModel? baseline = null;
        AislePilotRequestModel? overBudgetRequest = null;
        foreach (var budget in Enumerable.Range(20, 61))
        {
            var candidateRequest = new AislePilotRequestModel
            {
                Supermarket = request.Supermarket,
                WeeklyBudget = budget,
                HouseholdSize = request.HouseholdSize,
                CookDays = request.CookDays,
                PortionSize = request.PortionSize,
                DietaryModes = [.. request.DietaryModes],
                PreferQuickMeals = request.PreferQuickMeals
            };

            var candidatePlan = _service.BuildPlan(candidateRequest);
            if (!candidatePlan.IsOverBudget)
            {
                continue;
            }

            overBudgetRequest = candidateRequest;
            baseline = candidatePlan;
            break;
        }

        Assert.NotNull(overBudgetRequest);
        Assert.NotNull(baseline);

        var baselineMealNames = baseline!.MealPlan.Select(meal => meal.MealName).ToList();
        var rebalanced = _service.BuildPlanWithBudgetRebalance(
            overBudgetRequest!,
            currentPlanMealNames: baselineMealNames);
        var sameMealSequence = rebalanced.MealPlan
            .Select(meal => meal.MealName)
            .SequenceEqual(baselineMealNames, StringComparer.OrdinalIgnoreCase);

        if (!sameMealSequence)
        {
            Assert.True(rebalanced.EstimatedTotalCost < baseline.EstimatedTotalCost);
        }
    }

    [Fact]
    public void BuildPlanWithBudgetRebalance_WhenNoCheaperCompatibleMealsExist_ReturnsNoCheaperMessage()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 12m,
            HouseholdSize = 8,
            CookDays = 7,
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            DislikesOrAllergens = "lentil, paneer, chickpea, black bean, sweet potato"
        };

        var baseline = _service.BuildPlan(request);
        Assert.True(baseline.IsOverBudget);

        var rebalanced = _service.BuildPlanWithBudgetRebalance(
            request,
            currentPlanMealNames: baseline.MealPlan.Select(meal => meal.MealName).ToList());

        Assert.True(rebalanced.BudgetRebalanceAttempted);
        Assert.False(rebalanced.BudgetRebalanceReducedCost);
        Assert.Equal(baseline.EstimatedTotalCost, rebalanced.EstimatedTotalCost);
        Assert.Contains("do not currently have compatible recipes", rebalanced.BudgetRebalanceStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlanWithBudgetRebalance_WithCurrentPlanMealNames_PreservesThatSequenceWhenAlreadyWithinBudget()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 200m,
            HouseholdSize = 2,
            CookDays = 7,
            DietaryModes = ["Balanced"]
        };
        var currentPlanMealNames = new List<string>
        {
            "Chicken stir fry with rice",
            "Salmon, potatoes, and broccoli",
            "Turkey chilli with beans",
            "Veggie lentil curry",
            "Tofu noodle bowls",
            "Greek yogurt chicken wraps",
            "Egg fried rice"
        };

        var result = _service.BuildPlanWithBudgetRebalance(
            request,
            currentPlanMealNames: currentPlanMealNames);
        var resultMealNames = result.MealPlan
            .Select(meal => meal.MealName)
            .ToList();

        Assert.Equal(currentPlanMealNames.Count, resultMealNames.Count);
        Assert.True(resultMealNames.SequenceEqual(currentPlanMealNames, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_WithCurrentPlanMealNames_PreservesThatSequence()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 200m,
            HouseholdSize = 2,
            CookDays = 7,
            DietaryModes = ["Balanced"]
        };
        var currentPlanMealNames = new List<string>
        {
            "Chicken stir fry with rice",
            "Salmon, potatoes, and broccoli",
            "Turkey chilli with beans",
            "Veggie lentil curry",
            "Tofu noodle bowls",
            "Greek yogurt chicken wraps",
            "Egg fried rice"
        };

        var result = await _service.BuildPlanFromCurrentMealsAsync(request, currentPlanMealNames);
        var resultMealNames = result.MealPlan
            .Select(meal => meal.MealName)
            .ToList();

        Assert.Equal(currentPlanMealNames.Count, resultMealNames.Count);
        Assert.True(resultMealNames.SequenceEqual(currentPlanMealNames, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_MacrosRemainPerServingAcrossHouseholdSizes()
    {
        var currentPlanMealNames = new List<string>
        {
            "Chicken stir fry with rice",
            "Salmon, potatoes, and broccoli",
            "Turkey chilli with beans",
            "Veggie lentil curry",
            "Tofu noodle bowls",
            "Greek yogurt chicken wraps",
            "Egg fried rice"
        };
        var onePersonRequest = new AislePilotRequestModel
        {
            WeeklyBudget = 200m,
            HouseholdSize = 1,
            CookDays = 7,
            PortionSize = "Medium",
            DietaryModes = ["Balanced"]
        };
        var fourPeopleRequest = new AislePilotRequestModel
        {
            WeeklyBudget = 200m,
            HouseholdSize = 4,
            CookDays = 7,
            PortionSize = "Medium",
            DietaryModes = ["Balanced"]
        };

        var onePersonPlan = await _service.BuildPlanFromCurrentMealsAsync(onePersonRequest, currentPlanMealNames);
        var fourPeoplePlan = await _service.BuildPlanFromCurrentMealsAsync(fourPeopleRequest, currentPlanMealNames);

        Assert.Equal(onePersonPlan.MealPlan.Count, fourPeoplePlan.MealPlan.Count);
        for (var i = 0; i < onePersonPlan.MealPlan.Count; i++)
        {
            Assert.Equal(onePersonPlan.MealPlan[i].MealName, fourPeoplePlan.MealPlan[i].MealName);
            Assert.Equal(onePersonPlan.MealPlan[i].CaloriesPerServing, fourPeoplePlan.MealPlan[i].CaloriesPerServing);
            Assert.Equal(onePersonPlan.MealPlan[i].ProteinGramsPerServing, fourPeoplePlan.MealPlan[i].ProteinGramsPerServing);
            Assert.Equal(onePersonPlan.MealPlan[i].CarbsGramsPerServing, fourPeoplePlan.MealPlan[i].CarbsGramsPerServing);
            Assert.Equal(onePersonPlan.MealPlan[i].FatGramsPerServing, fourPeoplePlan.MealPlan[i].FatGramsPerServing);
        }

        Assert.True(fourPeoplePlan.EstimatedTotalCost > onePersonPlan.EstimatedTotalCost);
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_WithIgnoredMealSlot_ExcludesCostAndShoppingItemsForThatMeal()
    {
        var currentPlanMealNames = new List<string>
        {
            "Chicken stir fry with rice",
            "Tuna sweetcorn pasta salad",
            "Greek yogurt berry oat pots",
            "Turkey chilli with beans",
            "Mediterranean hummus wraps",
            "Spinach and tomato egg muffins"
        };
        var baselineRequest = new AislePilotRequestModel
        {
            WeeklyBudget = 120m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 3,
            DietaryModes = ["Balanced"]
        };
        var ignoredRequest = new AislePilotRequestModel
        {
            WeeklyBudget = baselineRequest.WeeklyBudget,
            HouseholdSize = baselineRequest.HouseholdSize,
            PlanDays = baselineRequest.PlanDays,
            CookDays = baselineRequest.CookDays,
            MealsPerDay = baselineRequest.MealsPerDay,
            DietaryModes = [.. baselineRequest.DietaryModes],
            IgnoredMealSlotIndexesCsv = "1"
        };

        var baseline = await _service.BuildPlanFromCurrentMealsAsync(baselineRequest, currentPlanMealNames);
        var ignored = await _service.BuildPlanFromCurrentMealsAsync(ignoredRequest, currentPlanMealNames);

        Assert.Equal("Tuna sweetcorn pasta salad", baseline.MealPlan[1].MealName, ignoreCase: true);
        Assert.False(baseline.MealPlan[1].IsIgnored);
        Assert.True(baseline.MealPlan[1].EstimatedCost > 0m);
        Assert.Contains(
            baseline.ShoppingItems,
            item => item.Name.Equals("Tuna chunks", StringComparison.OrdinalIgnoreCase));

        Assert.True(ignored.MealPlan[1].IsIgnored);
        Assert.Equal(0m, ignored.MealPlan[1].EstimatedCost);
        Assert.DoesNotContain(
            ignored.ShoppingItems,
            item => item.Name.Equals("Tuna chunks", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            ignored.ShoppingItems,
            item => item.Name.Equals("Sweetcorn", StringComparison.OrdinalIgnoreCase));
        Assert.True(ignored.EstimatedTotalCost < baseline.EstimatedTotalCost);
    }

    [Fact]
    public void BuildPlan_WithSpecialTreatMealEnabled_UsesAiIndulgentDinnerThatMatchesDietaryModes()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();
        var payloadContent = """
{
  "meals": [
    {
      "name": "Special treat creamy mushroom pasta bake",
      "baseCostForTwo": 9.20,
      "isQuick": false,
      "tags": ["Vegetarian", "Gluten-Free", "Special Treat"],
      "recipeSteps": [
        "Heat the oven to 200C and bring a large pot of water to the boil.",
        "Cook gluten-free pasta for 8 minutes until just tender, then drain.",
        "Saute mushrooms and garlic in olive oil for 6 minutes until golden.",
        "Stir in creme fraiche and spinach, then simmer gently for 3 minutes.",
        "Combine pasta with sauce, top with cheese, and bake for 12 minutes."
      ],
      "ingredients": [
        { "name": "Gluten-free pasta", "department": "Tins & Dry Goods", "quantityForTwo": 0.3, "unit": "kg", "estimatedCostForTwo": 1.90 },
        { "name": "Chestnut mushrooms", "department": "Produce", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.20 },
        { "name": "Creme fraiche", "department": "Dairy & Eggs", "quantityForTwo": 0.2, "unit": "kg", "estimatedCostForTwo": 1.90 },
        { "name": "Spinach", "department": "Produce", "quantityForTwo": 0.18, "unit": "kg", "estimatedCostForTwo": 1.60 },
        { "name": "Cheddar", "department": "Dairy & Eggs", "quantityForTwo": 0.12, "unit": "kg", "estimatedCostForTwo": 1.60 }
      ]
    }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            DietaryModes = ["Vegetarian", "Gluten-Free"],
            IncludeSpecialTreatMeal = true
        };

        var result = service.BuildPlan(request);

        Assert.Equal(1, handler.CallCount);
        Assert.True(result.UsedAiGeneratedMeals);
        Assert.Single(result.MealPlan);
        Assert.True(result.MealPlan[0].IsSpecialTreat);
        Assert.Contains("special treat", result.MealPlan[0].MealName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            result.BudgetTips,
            tip => tip.Contains("special treat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_WithSpecialTreatCookDaySelection_PlacesTreatOnSelectedDinnerSlot()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();
        var payloadContent = """
{
  "meals": [
    {
      "name": "Special treat creamy chicken pasta bake",
      "baseCostForTwo": 9.10,
      "isQuick": false,
      "tags": ["Balanced", "Special Treat"],
      "recipeSteps": [
        "Heat the oven to 200C and lightly oil a baking dish.",
        "Cook pasta for 8 minutes then drain well.",
        "Brown chicken pieces in a hot pan for 6 minutes.",
        "Stir in cream, spinach, and seasoning and simmer for 3 minutes.",
        "Combine pasta and sauce, top with cheese, and bake for 12 minutes."
      ],
      "ingredients": [
        { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.80 },
        { "name": "Pasta", "department": "Tins & Dry Goods", "quantityForTwo": 0.25, "unit": "kg", "estimatedCostForTwo": 0.90 },
        { "name": "Cheddar", "department": "Dairy & Eggs", "quantityForTwo": 0.12, "unit": "kg", "estimatedCostForTwo": 1.35 }
      ]
    },
    {
      "name": "Turkey chilli with beans",
      "baseCostForTwo": 7.00,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat oil in a deep pan and soften onion for 4 minutes.",
        "Add turkey mince and cook until browned.",
        "Stir in spices, tomatoes, and beans.",
        "Simmer for 20 minutes until thickened.",
        "Taste, season, and serve."
      ],
      "ingredients": [
        { "name": "Turkey mince", "department": "Meat & Fish", "quantityForTwo": 0.4, "unit": "kg", "estimatedCostForTwo": 3.10 },
        { "name": "Kidney beans", "department": "Tins & Dry Goods", "quantityForTwo": 1, "unit": "tin", "estimatedCostForTwo": 0.75 },
        { "name": "Chopped tomatoes", "department": "Tins & Dry Goods", "quantityForTwo": 1, "unit": "tin", "estimatedCostForTwo": 0.70 }
      ]
    }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 95m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"],
            IncludeSpecialTreatMeal = true,
            SelectedSpecialTreatCookDayIndex = 1
        };

        var result = service.BuildPlan(request);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(2, result.MealPlan.Count);
        Assert.Equal("Dinner", result.MealPlan[1].MealType, ignoreCase: true);
        Assert.True(result.MealPlan[1].IsSpecialTreat);
    }

    [Fact]
    public void BuildPlan_WithOutOfRangeSpecialTreatCookDaySelection_FallsBackToAnyDinner()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();
        var payloadContent = """
{
  "meals": [
    {
      "name": "Special treat creamy chicken pasta bake",
      "baseCostForTwo": 9.10,
      "isQuick": false,
      "tags": ["Balanced", "Special Treat"],
      "recipeSteps": [
        "Heat the oven to 200C and lightly oil a baking dish.",
        "Cook pasta for 8 minutes then drain well.",
        "Brown chicken pieces in a hot pan for 6 minutes.",
        "Stir in cream, spinach, and seasoning and simmer for 3 minutes.",
        "Combine pasta and sauce, top with cheese, and bake for 12 minutes."
      ],
      "ingredients": [
        { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.80 },
        { "name": "Pasta", "department": "Tins & Dry Goods", "quantityForTwo": 0.25, "unit": "kg", "estimatedCostForTwo": 0.90 },
        { "name": "Cheddar", "department": "Dairy & Eggs", "quantityForTwo": 0.12, "unit": "kg", "estimatedCostForTwo": 1.35 }
      ]
    },
    {
      "name": "Turkey chilli with beans",
      "baseCostForTwo": 7.00,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat oil in a deep pan and soften onion for 4 minutes.",
        "Add turkey mince and cook until browned.",
        "Stir in spices, tomatoes, and beans.",
        "Simmer for 20 minutes until thickened.",
        "Taste, season, and serve."
      ],
      "ingredients": [
        { "name": "Turkey mince", "department": "Meat & Fish", "quantityForTwo": 0.4, "unit": "kg", "estimatedCostForTwo": 3.10 },
        { "name": "Kidney beans", "department": "Tins & Dry Goods", "quantityForTwo": 1, "unit": "tin", "estimatedCostForTwo": 0.75 },
        { "name": "Chopped tomatoes", "department": "Tins & Dry Goods", "quantityForTwo": 1, "unit": "tin", "estimatedCostForTwo": 0.70 }
      ]
    }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 95m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"],
            IncludeSpecialTreatMeal = true,
            SelectedSpecialTreatCookDayIndex = 42
        };

        var result = service.BuildPlan(request);

        Assert.Equal(1, handler.CallCount);
        Assert.Contains(
            result.MealPlan,
            meal => meal.MealType.Equals("Dinner", StringComparison.OrdinalIgnoreCase) && meal.IsSpecialTreat);
    }

    [Fact]
    public void BuildPlan_WithSpecialTreatMealEnabled_WhenBatchLacksTreat_UsesDedicatedAiTreatGeneration()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();

        var basePlanPayloadContent = """
{
  "meals": [
    {
      "name": "Chicken and peppers skillet",
      "baseCostForTwo": 7.10,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat a pan over medium heat for two minutes.",
        "Cook sliced chicken for 6 minutes until lightly browned.",
        "Add peppers and onions and cook for 5 more minutes.",
        "Stir in seasoning and a splash of water and simmer for 3 minutes.",
        "Serve immediately with a simple side."
      ],
      "ingredients": [
        { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.70 },
        { "name": "Bell peppers", "department": "Produce", "quantityForTwo": 2, "unit": "pcs", "estimatedCostForTwo": 1.40 },
        { "name": "Onions", "department": "Produce", "quantityForTwo": 0.3, "unit": "kg", "estimatedCostForTwo": 0.70 }
      ]
    }
  ]
}
""";
        var dedicatedTreatPayloadContent = """
{
  "name": "Creamy special treat chicken gratin",
  "baseCostForTwo": 9.40,
  "isQuick": false,
  "tags": ["Balanced", "Special Treat"],
  "recipeSteps": [
    "Heat oven to 200C and butter a medium baking dish.",
    "Brown chicken pieces in a pan for 6 minutes, then set aside.",
    "Cook leeks and mushrooms in butter for 5 minutes until softened.",
    "Stir in cream and mustard, then simmer gently for 3 minutes.",
    "Layer chicken and sauce in dish, top with cheese, and bake for 18 minutes."
  ],
  "ingredients": [
    { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.45, "unit": "kg", "estimatedCostForTwo": 3.40 },
    { "name": "Leeks", "department": "Produce", "quantityForTwo": 2, "unit": "pcs", "estimatedCostForTwo": 1.20 },
    { "name": "Chestnut mushrooms", "department": "Produce", "quantityForTwo": 0.3, "unit": "kg", "estimatedCostForTwo": 1.30 },
    { "name": "Double cream", "department": "Dairy & Eggs", "quantityForTwo": 0.2, "unit": "kg", "estimatedCostForTwo": 1.40 },
    { "name": "Cheddar", "department": "Dairy & Eggs", "quantityForTwo": 0.1, "unit": "kg", "estimatedCostForTwo": 1.10 }
  ]
}
""";

        var firstResponseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = basePlanPayloadContent
                    }
                }
            }
        });
        var secondResponseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = dedicatedTreatPayloadContent
                    }
                }
            }
        });

        using var handler = new SequentialResponseHandler(
            (HttpStatusCode.OK, firstResponseBody),
            (HttpStatusCode.OK, secondResponseBody));
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 75m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            DietaryModes = ["Balanced"],
            IncludeSpecialTreatMeal = true
        };

        var result = service.BuildPlan(request);

        Assert.Equal(2, handler.CallCount);
        Assert.True(result.UsedAiGeneratedMeals);
        Assert.Single(result.MealPlan);
        Assert.True(result.MealPlan[0].IsSpecialTreat);
        Assert.Contains("special treat", result.MealPlan[0].MealName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlan_WithLargeMealCountAndSpecialTreat_StillGeneratesDedicatedAiTreat()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();

        var dedicatedTreatPayloadContent = """
{
  "name": "Special treat creamy chicken gratin",
  "baseCostForTwo": 9.40,
  "isQuick": false,
  "tags": ["Balanced", "Special Treat"],
  "recipeSteps": [
    "Heat oven to 200C and butter a medium baking dish.",
    "Brown chicken pieces in a pan for 6 minutes, then set aside.",
    "Cook leeks and mushrooms in butter for 5 minutes until softened.",
    "Stir in cream and mustard, then simmer gently for 3 minutes.",
    "Layer chicken and sauce in dish, top with cheese, and bake for 18 minutes."
  ],
  "ingredients": [
    { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.45, "unit": "kg", "estimatedCostForTwo": 3.40 },
    { "name": "Leeks", "department": "Produce", "quantityForTwo": 2, "unit": "pcs", "estimatedCostForTwo": 1.20 },
    { "name": "Chestnut mushrooms", "department": "Produce", "quantityForTwo": 0.3, "unit": "kg", "estimatedCostForTwo": 1.30 },
    { "name": "Double cream", "department": "Dairy & Eggs", "quantityForTwo": 0.2, "unit": "kg", "estimatedCostForTwo": 1.40 },
    { "name": "Cheddar", "department": "Dairy & Eggs", "quantityForTwo": 0.1, "unit": "kg", "estimatedCostForTwo": 1.10 }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = dedicatedTreatPayloadContent
                    }
                }
            }
        });
        using var handler = new SequentialResponseHandler((HttpStatusCode.OK, responseBody));
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 120m,
            HouseholdSize = 2,
            PlanDays = 5,
            CookDays = 5,
            MealsPerDay = 2,
            SelectedMealTypes = ["Lunch", "Dinner"],
            DietaryModes = ["Balanced"],
            IncludeSpecialTreatMeal = true
        };

        var result = service.BuildPlan(request);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(10, result.MealPlan.Count);
        Assert.Contains(result.MealPlan, meal => meal.IsSpecialTreat);
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_WithSpecialTreatMealEnabled_MarksOnlyOneMealCardAsSpecialTreat()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 120m,
            HouseholdSize = 2,
            PlanDays = 5,
            CookDays = 5,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"],
            IncludeSpecialTreatMeal = true,
            SelectedSpecialTreatCookDayIndex = 2
        };
        var currentPlanMealNames = new List<string>
        {
            "Salmon, potatoes, and broccoli",
            "Chicken and leek cream pie",
            "Mushroom spinach risotto",
            "Creamy chicken and mushroom pasta bake",
            "Halloumi and harissa roast veg tray bake"
        };

        var result = await _service.BuildPlanFromCurrentMealsAsync(request, currentPlanMealNames);
        var flaggedTreatMeals = result.MealPlan
            .Where(meal => meal.IsSpecialTreat)
            .ToList();

        Assert.Single(flaggedTreatMeals);
        Assert.Equal("Dinner", flaggedTreatMeals[0].MealType, ignoreCase: true);
        Assert.Equal("Wednesday", flaggedTreatMeals[0].Day, ignoreCase: true);
    }

    [Fact]
    public void BuildPlan_WithSpecialTreatMealEnabled_WhenAiReturnsNoTreat_StillReturnsSpecialTreatMeal()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();
        var payloadContent = """
{
  "meals": [
    {
      "name": "Chicken and peppers skillet",
      "baseCostForTwo": 7.10,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat a pan over medium heat for two minutes.",
        "Cook sliced chicken for 6 minutes until lightly browned.",
        "Add peppers and onions and cook for 5 more minutes.",
        "Stir in seasoning and a splash of water and simmer for 3 minutes.",
        "Serve immediately with a simple side."
      ],
      "ingredients": [
        { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.70 },
        { "name": "Bell peppers", "department": "Produce", "quantityForTwo": 2, "unit": "pcs", "estimatedCostForTwo": 1.40 },
        { "name": "Onions", "department": "Produce", "quantityForTwo": 0.3, "unit": "kg", "estimatedCostForTwo": 0.70 }
      ]
    }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });
        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 75m,
            HouseholdSize = 2,
            PlanDays = 1,
            CookDays = 1,
            DietaryModes = ["Balanced"],
            IncludeSpecialTreatMeal = true
        };

        var result = service.BuildPlan(request);

        Assert.Single(result.MealPlan);
        Assert.Contains(result.MealPlan, meal => meal.IsSpecialTreat);
        Assert.True(result.IncludeSpecialTreatMeal);
        Assert.DoesNotContain(
            result.BudgetTips,
            tip => tip.Contains("background", StringComparison.OrdinalIgnoreCase));
        Assert.True(handler.CallCount >= 2);
    }

    [Fact]
    public void BuildPlan_WithDessertAddOn_AddsDessertIngredientsAndCost()
    {
        var baselineRequest = new AislePilotRequestModel
        {
            WeeklyBudget = 90m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            DietaryModes = ["Balanced"]
        };
        var dessertRequest = new AislePilotRequestModel
        {
            WeeklyBudget = baselineRequest.WeeklyBudget,
            HouseholdSize = baselineRequest.HouseholdSize,
            PlanDays = baselineRequest.PlanDays,
            CookDays = baselineRequest.CookDays,
            DietaryModes = [.. baselineRequest.DietaryModes],
            IncludeDessertAddOn = true
        };

        var baseline = _service.BuildPlan(baselineRequest);
        var withDessert = _service.BuildPlan(dessertRequest);

        Assert.Equal(
            baseline.MealPlan.Select(meal => meal.MealName),
            withDessert.MealPlan.Select(meal => meal.MealName));
        Assert.True(withDessert.IncludeDessertAddOn);
        Assert.True(withDessert.DessertAddOnEstimatedCost > 0m);
        Assert.Equal("Chocolate sponge tray bake", withDessert.DessertAddOnName, ignoreCase: true);
        Assert.NotEmpty(withDessert.DessertAddOnIngredientLines);
        Assert.True(withDessert.EstimatedTotalCost > baseline.EstimatedTotalCost);
        Assert.Contains(
            withDessert.ShoppingItems,
            item => item.Name.Equals("Self-raising flour", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            withDessert.BudgetTips,
            tip => tip.Contains("dessert", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_WithDessertAddOnSelection_UsesRequestedDessert()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 90m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            DietaryModes = ["Balanced"],
            IncludeDessertAddOn = true,
            SelectedDessertAddOnName = "Lemon drizzle loaf cake"
        };

        var result = _service.BuildPlan(request);

        Assert.True(result.IncludeDessertAddOn);
        Assert.Equal("Lemon drizzle loaf cake", result.DessertAddOnName, ignoreCase: true);
        Assert.Contains(
            result.ShoppingItems,
            item => item.Name.Equals("Lemons", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveNextDessertAddOnName_WithKnownCurrentDessert_RotatesToDifferentDessert()
    {
        var firstDessert = _service.ResolveNextDessertAddOnName(null);
        var nextDessert = _service.ResolveNextDessertAddOnName(firstDessert);

        Assert.False(string.IsNullOrWhiteSpace(firstDessert));
        Assert.False(string.IsNullOrWhiteSpace(nextDessert));
        Assert.NotEqual(firstDessert, nextDessert);
    }

    [Fact]
    public void ResolveNextDessertAddOnName_WithPersistedDessertPool_UsesPersistedDessertsInRotation()
    {
        var dessertPool = GetRequiredStaticField("DessertAddOnPool");
        var dessertTemplates = GetRequiredStaticField("DessertAddOnTemplates") as IEnumerable;
        Assert.NotNull(dessertTemplates);
        var seedDessertTemplate = dessertTemplates!.Cast<object>().First();
        var dessertTemplateType = seedDessertTemplate.GetType();
        var ingredientsProperty = dessertTemplateType.GetProperty("Ingredients", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(ingredientsProperty);
        var ingredients = ingredientsProperty!.GetValue(seedDessertTemplate)
            ?? throw new InvalidOperationException("Dessert add-on template ingredients were missing.");
        var constructor = dessertTemplateType.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 2 && parameters[0].ParameterType == typeof(string);
            });
        Assert.NotNull(constructor);

        var persistedDessertName = $"Test persisted dessert {Guid.NewGuid():N}";
        var persistedDessertTemplate = constructor!.Invoke([persistedDessertName, ingredients]);
        var indexer = dessertPool.GetType().GetProperty("Item");
        Assert.NotNull(indexer);

        try
        {
            indexer!.SetValue(dessertPool, persistedDessertTemplate, [persistedDessertName]);
            var nextDessert = _service.ResolveNextDessertAddOnName("Apple crumble pots");
            Assert.Equal(persistedDessertName, nextDessert, ignoreCase: true);
        }
        finally
        {
            RemoveFromConcurrentDictionary(dessertPool, persistedDessertName);
        }
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_WithSelectedDessertAddOnName_UsesThatDessert()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 90m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            DietaryModes = ["Balanced"],
            IncludeDessertAddOn = true
        };

        var initial = _service.BuildPlan(request);
        var currentPlanMealNames = initial.MealPlan.Select(meal => meal.MealName).ToList();
        request.SelectedDessertAddOnName = _service.ResolveNextDessertAddOnName(initial.DessertAddOnName);

        var swappedDessertResult = await _service.BuildPlanFromCurrentMealsAsync(request, currentPlanMealNames);

        Assert.False(
            initial.DessertAddOnName.Equals(swappedDessertResult.DessertAddOnName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateAndMapAiMeal_NormalizesInflatedBaseCostAgainstIngredientTotals()
    {
        var payloadJson = """
{
  "name": "Egg and mushroom omelette",
  "baseCostForTwo": 7.00,
  "isQuick": true,
  "tags": ["Balanced", "Vegetarian"],
  "recipeSteps": [
    "Crack eggs into a bowl and whisk with a pinch of salt.",
    "Slice mushrooms and pan-fry for 4 minutes until softened.",
    "Add butter, pour in eggs, and cook gently for 2-3 minutes.",
    "Fold the omelette and toast bread while it finishes cooking.",
    "Serve immediately with the mushrooms and toast."
  ],
  "ingredients": [
    { "name": "Eggs", "department": "Dairy & Eggs", "quantityForTwo": 4, "unit": "pcs", "estimatedCostForTwo": 1.20 },
    { "name": "Mushrooms", "department": "Produce", "quantityForTwo": 0.25, "unit": "kg", "estimatedCostForTwo": 1.00 },
    { "name": "Bread", "department": "Bakery", "quantityForTwo": 0.20, "unit": "pack", "estimatedCostForTwo": 0.40 }
  ]
}
""";
        var payloadType = typeof(AislePilotService).GetNestedType(
            "AislePilotAiMealPayload",
            BindingFlags.NonPublic);
        Assert.NotNull(payloadType);

        var payload = JsonSerializer.Deserialize(
            payloadJson,
            payloadType!,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        Assert.NotNull(payload);

        var validateMethod = typeof(AislePilotService).GetMethod(
            "ValidateAndMapAiMeal",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types:
            [
                payloadType!,
                typeof(IReadOnlyList<string>),
                typeof(bool),
                typeof(string).MakeByRefType()
            ],
            modifiers: null);
        Assert.NotNull(validateMethod);

        var args = new object?[] { payload, Array.Empty<string>(), true, null };
        var mapped = validateMethod!.Invoke(null, args);
        Assert.NotNull(mapped);

        var baseCostProperty = mapped!.GetType().GetProperty("BaseCostForTwo");
        Assert.NotNull(baseCostProperty);

        var normalizedBaseCostForTwo = (decimal)(baseCostProperty!.GetValue(mapped) ?? 0m);
        Assert.True(normalizedBaseCostForTwo < 4.00m);
    }

    [Fact]
    public void BuildPlan_AssignsMealImageUrl_ForEveryMeal()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 65m,
            HouseholdSize = 2,
            CookDays = 5,
            DietaryModes = ["Balanced"]
        };

        var result = _service.BuildPlan(request);

        Assert.Equal(5, result.MealPlan.Count);
        Assert.All(result.MealPlan, meal =>
        {
            Assert.False(string.IsNullOrWhiteSpace(meal.MealImageUrl));
            Assert.StartsWith("/images/", meal.MealImageUrl, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("vegan-mushroom-stroganoff.png", "/images/aislepilot-meals/vegan-mushroom-stroganoff.png")]
    [InlineData("aislepilot-meals/chickpea-and-spinach-curry.png", "/images/aislepilot-meals/chickpea-and-spinach-curry.png")]
    [InlineData("/projects/aisle-pilot/images/aislepilot-meals/quinoa-roasted-vegetable-salad.png", "/images/aislepilot-meals/quinoa-roasted-vegetable-salad.png")]
    public void NormalizeImageUrl_NormalizesRelativeAndProxyImagePaths(string input, string expected)
    {
        var normalized = InvokeNormalizeImageUrl(input);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void IsMealImageUrlUsable_RelativeFilename_ReturnsFalse()
    {
        var method = typeof(AislePilotService).GetMethod("IsMealImageUrlUsable", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var isUsable = method!.Invoke(_service, ["vegan-mushroom-stroganoff.png"]);

        Assert.NotNull(isUsable);
        Assert.False((bool)isUsable!);
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
    public void BuildPlan_WhenAiReturnsRateLimit_FallsBackToTemplatePlan()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        using var handler = new StaticResponseHandler(HttpStatusCode.TooManyRequests, """{"error":"rate limit"}""");
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegan", "Gluten-Free"],
            CookDays = 7,
            WeeklyBudget = 70m,
            HouseholdSize = 2
        };

        var result = service.BuildPlan(request);

        Assert.Equal(2, handler.CallCount);
        Assert.False(result.UsedAiGeneratedMeals);
        Assert.Equal("Template fallback", result.PlanSourceLabel);
        Assert.Equal(7, result.MealPlan.Count);
    }

    [Fact]
    public void BuildPlan_WithSpecialTreatMealEnabledAndNoAiAvailable_ReturnsTemplateFallbackWithoutThrowing()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, "{}");
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 1,
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            IncludeSpecialTreatMeal = true
        };

        var result = service.BuildPlan(request);

        Assert.NotEmpty(result.MealPlan);
        Assert.True(result.IncludeSpecialTreatMeal);
        Assert.Contains(result.MealPlan, meal => meal.IsSpecialTreat);
        Assert.DoesNotContain(
            result.BudgetTips,
            tip => tip.Contains("background", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_WhenAiRepeatsLunchTooOften_FallsBackAndPreservesLunchVariety()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        var recipeSteps = new[]
        {
            "Heat a large non-stick pan over medium heat for two minutes.",
            "Add oil and onions, then cook for five minutes until softened.",
            "Stir in the main ingredients and cook until hot throughout.",
            "Season, simmer briefly, and adjust texture if needed.",
            "Serve immediately with a simple side."
        };

        object[] BuildIngredients(string primaryIngredient) =>
        [
            new { name = primaryIngredient, department = "Produce", quantityForTwo = 0.4m, unit = "kg", estimatedCostForTwo = 1.8m },
            new { name = "Olive oil", department = "Spices & Sauces", quantityForTwo = 0.05m, unit = "bottle", estimatedCostForTwo = 0.3m },
            new { name = "Chopped tomatoes", department = "Tins & Dry Goods", quantityForTwo = 1m, unit = "tin", estimatedCostForTwo = 0.8m }
        ];

        var aiMeals = new List<object>();
        for (var day = 1; day <= 4; day++)
        {
            aiMeals.Add(new
            {
                name = $"Roasted chicken tray bake day {day}",
                baseCostForTwo = 7.4m,
                isQuick = day % 2 == 0,
                tags = new[] { "Balanced" },
                recipeSteps,
                ingredients = BuildIngredients("Chicken breast")
            });

            aiMeals.Add(new
            {
                name = "Chicken wrap lunch special",
                baseCostForTwo = 5.2m,
                isQuick = true,
                tags = new[] { "Balanced" },
                recipeSteps,
                ingredients = BuildIngredients("Chicken breast")
            });
        }

        var payloadContent = JsonSerializer.Serialize(new { meals = aiMeals });
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            PlanDays = 4,
            CookDays = 4,
            MealsPerDay = 2,
            SelectedMealTypes = ["Lunch", "Dinner"],
            WeeklyBudget = 95m,
            HouseholdSize = 2
        };

        var result = service.BuildPlan(request);
        var lunchMeals = result.MealPlan
            .Where(meal => meal.MealType.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
            .Select(meal => meal.MealName)
            .ToList();
        var maxLunchRepeatCount = lunchMeals
            .GroupBy(mealName => mealName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();

        Assert.Equal(1, handler.CallCount);
        Assert.False(result.UsedAiGeneratedMeals);
        Assert.Equal("Template fallback", result.PlanSourceLabel);
        Assert.Equal(4, lunchMeals.Count);
        Assert.True(
            maxLunchRepeatCount <= 2,
            $"Expected lunch repeats to be capped at two in fallback plan, but found {maxLunchRepeatCount}.");
    }

    [Fact]
    public void BuildPlan_WhenAiReturnsUnrealisticallyCheapIngredientPrices_FallsBackToTemplatePlan()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        var payloadContent = """
{
  "meals": [
    {
      "name": "Budget test meal one",
      "baseCostForTwo": 6.5,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat a large pan on medium heat for two minutes.",
        "Add oil and cook the onions for five minutes until softened.",
        "Stir in the protein and cook until browned and hot through.",
        "Add sauce and simmer for six minutes until slightly reduced.",
        "Serve immediately with your chosen side."
      ],
      "ingredients": [
        { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.6 },
        { "name": "Rice", "department": "Tins & Dry Goods", "quantityForTwo": 0.4, "unit": "kg", "estimatedCostForTwo": 0.9 },
        { "name": "Kidney beans", "department": "Tins & Dry Goods", "quantityForTwo": 2, "unit": "tins", "estimatedCostForTwo": 0.1 }
      ]
    },
    {
      "name": "Budget test meal two",
      "baseCostForTwo": 6.2,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat a large pan on medium heat for two minutes.",
        "Add oil and cook the onions for five minutes until softened.",
        "Stir in the protein and cook until browned and hot through.",
        "Add sauce and simmer for six minutes until slightly reduced.",
        "Serve immediately with your chosen side."
      ],
      "ingredients": [
        { "name": "Turkey mince", "department": "Meat & Fish", "quantityForTwo": 0.4, "unit": "kg", "estimatedCostForTwo": 2.8 },
        { "name": "Chopped tomatoes", "department": "Tins & Dry Goods", "quantityForTwo": 2, "unit": "tins", "estimatedCostForTwo": 1.2 },
        { "name": "Chilli seasoning", "department": "Spices & Sauces", "quantityForTwo": 1, "unit": "pack", "estimatedCostForTwo": 0.7 }
      ]
    },
    {
      "name": "Budget test meal three",
      "baseCostForTwo": 5.9,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat a large pan on medium heat for two minutes.",
        "Add oil and cook the onions for five minutes until softened.",
        "Stir in the protein and cook until browned and hot through.",
        "Add sauce and simmer for six minutes until slightly reduced.",
        "Serve immediately with your chosen side."
      ],
      "ingredients": [
        { "name": "Pasta", "department": "Tins & Dry Goods", "quantityForTwo": 0.45, "unit": "kg", "estimatedCostForTwo": 1.1 },
        { "name": "Passata", "department": "Tins & Dry Goods", "quantityForTwo": 0.7, "unit": "bottle", "estimatedCostForTwo": 0.9 },
        { "name": "Parmesan", "department": "Dairy & Eggs", "quantityForTwo": 0.12, "unit": "kg", "estimatedCostForTwo": 1.2 }
      ]
    },
    {
      "name": "Budget test meal four",
      "baseCostForTwo": 6.8,
      "isQuick": false,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat a large pan on medium heat for two minutes.",
        "Add oil and cook the onions for five minutes until softened.",
        "Stir in the protein and cook until browned and hot through.",
        "Add sauce and simmer for six minutes until slightly reduced.",
        "Serve immediately with your chosen side."
      ],
      "ingredients": [
        { "name": "Cod fillets", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 3.6 },
        { "name": "Sweet potatoes", "department": "Produce", "quantityForTwo": 0.9, "unit": "kg", "estimatedCostForTwo": 1.5 },
        { "name": "Green beans", "department": "Produce", "quantityForTwo": 0.3, "unit": "kg", "estimatedCostForTwo": 1.3 }
      ]
    }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 2,
            WeeklyBudget = 65m,
            HouseholdSize = 2
        };

        var result = service.BuildPlan(request);

        Assert.Equal(2, handler.CallCount);
        Assert.False(result.UsedAiGeneratedMeals);
        Assert.Equal("Template fallback", result.PlanSourceLabel);
        Assert.Equal(2, result.MealPlan.Count);
    }

    [Fact]
    public void BuildPlan_WhenAiMealNameImpliesRiceButIngredientListOmitsRice_FallsBackToTemplatePlan()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        var payloadContent = """
{
  "meals": [
    {
      "name": "Chicken stir fry with rice",
      "baseCostForTwo": 6.5,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat a large pan over medium heat for 2 minutes.",
        "Cook chicken strips for 6 minutes until lightly browned.",
        "Add peppers and carrots and cook for 4 minutes.",
        "Stir in soy sauce and a splash of water and simmer briefly.",
        "Serve immediately while hot."
      ],
      "ingredients": [
        { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.6 },
        { "name": "Bell peppers", "department": "Produce", "quantityForTwo": 2, "unit": "pcs", "estimatedCostForTwo": 1.1 },
        { "name": "Carrots", "department": "Produce", "quantityForTwo": 3, "unit": "pcs", "estimatedCostForTwo": 0.8 }
      ]
    }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 1,
            WeeklyBudget = 65m,
            HouseholdSize = 2
        };

        var result = service.BuildPlan(request);

        Assert.Equal(2, handler.CallCount);
        Assert.False(result.UsedAiGeneratedMeals);
        Assert.Equal("Template fallback", result.PlanSourceLabel);
    }

    [Fact]
    public void BuildPlan_WhenAiRecipeMethodIsVague_ReplacesItWithMoreConcreteFallbackSteps()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        var payloadContent = """
{
  "meals": [
    {
      "name": "Chicken skillet supper",
      "baseCostForTwo": 6.9,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Prepare ingredients in advance.",
        "Cook until done and stir now and then.",
        "Season to taste as desired.",
        "Add everything together and heat through.",
        "Serve and enjoy."
      ],
      "ingredients": [
        { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.6 },
        { "name": "Bell peppers", "department": "Produce", "quantityForTwo": 2, "unit": "pcs", "estimatedCostForTwo": 1.2 },
        { "name": "Rice", "department": "Tins & Dry Goods", "quantityForTwo": 0.4, "unit": "kg", "estimatedCostForTwo": 0.9 }
      ]
    }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 1,
            WeeklyBudget = 65m,
            HouseholdSize = 2
        };

        var result = service.BuildPlan(request);

        Assert.Equal(1, handler.CallCount);
        Assert.Single(result.MealPlan);
        var recipeSteps = result.MealPlan[0].RecipeSteps;
        Assert.True(recipeSteps.Count >= 5);
        Assert.DoesNotContain(recipeSteps, step => step.Contains("serve and enjoy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            recipeSteps,
            step => step.Contains("minute", StringComparison.OrdinalIgnoreCase) ||
                    step.Contains("200C", StringComparison.OrdinalIgnoreCase) ||
                    step.Contains("medium-high", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_WhenAiNutritionLooksExtreme_UsesIngredientBasedNutritionGuardrails()
    {
        ClearAiPool();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        var payloadContent = """
{
  "meals": [
    {
      "name": "Hybrid nutrition test meal",
      "baseCostForTwo": 6.8,
      "isQuick": true,
      "tags": ["Balanced"],
      "recipeSteps": [
        "Heat a large pan on medium heat for two minutes.",
        "Cook onions until softened and lightly golden.",
        "Add protein and cook until fully done.",
        "Stir through sauce and simmer briefly.",
        "Serve hot with your prepared side."
      ],
      "nutritionPerServing": {
        "calories": 1400,
        "proteinGrams": 30,
        "carbsGrams": 40,
        "fatGrams": 120
      },
      "ingredients": [
        { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.7 },
        { "name": "Rice", "department": "Tins & Dry Goods", "quantityForTwo": 0.4, "unit": "kg", "estimatedCostForTwo": 0.95 },
        { "name": "Bell peppers", "department": "Produce", "quantityForTwo": 2, "unit": "pcs", "estimatedCostForTwo": 1.2 }
      ]
    }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 1,
            WeeklyBudget = 65m,
            HouseholdSize = 2
        };

        var result = service.BuildPlan(request);

        Assert.Equal(1, handler.CallCount);
        Assert.True(result.UsedAiGeneratedMeals);
        Assert.Single(result.MealPlan);
        var meal = result.MealPlan[0];
        Assert.InRange(meal.CaloriesPerServing, 220, 1200);
        Assert.InRange(meal.ProteinGramsPerServing, 10m, 90m);
        Assert.InRange(meal.CarbsGramsPerServing, 15m, 180m);
        Assert.InRange(meal.FatGramsPerServing, 5m, 80m);
    }

    [Fact]
    public void BuildPlan_EggFriedRiceCalories_AreWithinReasonableServingRange()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Vegetarian"],
            CookDays = 1,
            WeeklyBudget = 60m,
            HouseholdSize = 2,
            DislikesOrAllergens =
                "lentils, coconut milk, tofu, noodles, paneer, tikka, chickpeas, quinoa, black beans, sweet potatoes, halloumi, couscous, courgettes, mushrooms, risotto, pesto, mozzarella, cod, salmon, turkey, beef, prawns, chicken"
        };

        var result = _service.BuildPlan(request);

        Assert.Single(result.MealPlan);
        Assert.Equal("Egg fried rice", result.MealPlan[0].MealName, ignoreCase: true);
        Assert.InRange(result.MealPlan[0].CaloriesPerServing, 420, 900);
    }

    [Fact]
    public void AddMealsToAiPool_WhenEntryCountExceedsCap_EvictsOldestEntries()
    {
        ClearAiPool();

        var maxEntries = GetPrivateStaticInt("MaxAiMealPoolEntries");
        var seedMealTemplate = GetMealTemplateSeed();
        var olderMealNames = Enumerable.Range(0, 40)
            .Select(index => $"Pool old {index}-{Guid.NewGuid():N}")
            .ToList();
        var newerMealNames = Enumerable.Range(0, maxEntries)
            .Select(index => $"Pool new {index}-{Guid.NewGuid():N}")
            .ToList();

        InvokeAddMealsToAiPool(CreateMealTemplatesFromSeed(seedMealTemplate, olderMealNames));
        Thread.Sleep(20);
        InvokeAddMealsToAiPool(CreateMealTemplatesFromSeed(seedMealTemplate, newerMealNames));

        Assert.True(GetAiPoolCount() <= maxEntries);
        Assert.All(olderMealNames, mealName => Assert.False(AiPoolContains(mealName)));
        Assert.True(AiPoolContains(newerMealNames[^1]));
    }

    [Fact]
    public void PruneAiMealPool_WhenEntryIsPastTtl_RemovesStaleEntry()
    {
        ClearAiPool();

        var seedMealTemplate = GetMealTemplateSeed();
        var staleMealName = $"Pool stale-{Guid.NewGuid():N}";
        var freshMealName = $"Pool fresh-{Guid.NewGuid():N}";
        var ttl = GetPrivateStaticTimeSpan("AiMealPoolEntryTtl");
        var nowUtc = DateTime.UtcNow;

        InvokeAddMealsToAiPool(
            CreateMealTemplatesFromSeed(seedMealTemplate, [staleMealName, freshMealName]));
        SetAiPoolTouchedUtc(staleMealName, nowUtc - ttl - TimeSpan.FromMinutes(1));
        SetAiPoolTouchedUtc(freshMealName, nowUtc);
        InvokePruneAiMealPool(nowUtc);

        Assert.False(AiPoolContains(staleMealName));
        Assert.True(AiPoolContains(freshMealName));
    }

    [Fact]
    public async Task WarmupAiMealPoolAsync_GeneratesAtMostConfiguredMeals()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "false"
            })
            .Build();

        var mealName = $"Warmup meal {Guid.NewGuid():N}";
        var payloadContent = $$"""
{
  "name": "{{mealName}}",
  "baseCostForTwo": 6.4,
  "isQuick": true,
  "tags": ["High-Protein", "Vegetarian", "Vegan", "Pescatarian", "Gluten-Free"],
  "recipeSteps": [
    "Heat a large non-stick pan over medium heat for 2 minutes.",
    "Add oil, onions, and peppers; cook for 6 minutes until softened.",
    "Stir in spices and tomatoes; simmer for 4 minutes.",
    "Add chickpeas and spinach; cook for 5 minutes until hot and wilted.",
    "Season and serve immediately with lemon and chopped herbs."
  ],
  "ingredients": [
    { "name": "Chickpeas", "department": "Tins & Dry Goods", "quantityForTwo": 2, "unit": "tins", "estimatedCostForTwo": 1.4 },
    { "name": "Spinach", "department": "Produce", "quantityForTwo": 0.25, "unit": "kg", "estimatedCostForTwo": 1.2 },
    { "name": "Chopped tomatoes", "department": "Tins & Dry Goods", "quantityForTwo": 1, "unit": "tin", "estimatedCostForTwo": 0.7 }
  ]
}
""";
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = payloadContent
                    }
                }
            }
        });

        using var handler = new StaticResponseHandler(HttpStatusCode.OK, responseBody);
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        try
        {
            var warmup = await service.WarmupAiMealPoolAsync(
                minPerSingleMode: 0,
                minPerKeyPair: 500,
                maxMealsToGenerate: 1);

            Assert.Equal(1, handler.CallCount);
            Assert.Equal(1, warmup.GeneratedCount);
            Assert.Contains(mealName, warmup.GeneratedMealNames, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            RemoveAiPoolMeal(mealName);
        }
    }

    private static void RemoveAiPoolMeal(string mealName)
    {
        RemoveFromConcurrentDictionary(GetRequiredStaticField("AiMealPool"), mealName);
        RemoveFromConcurrentDictionary(GetRequiredStaticField("AiMealPoolLastTouchedUtc"), mealName);
    }

    private static void ClearAiPool()
    {
        ClearConcurrentDictionary(GetRequiredStaticField("AiMealPool"));
        ClearConcurrentDictionary(GetRequiredStaticField("AiMealPoolLastTouchedUtc"));
    }

    private static object GetMealTemplateSeed()
    {
        var mealTemplates = GetRequiredStaticField("MealTemplates") as IEnumerable;
        Assert.NotNull(mealTemplates);

        foreach (var template in mealTemplates!)
        {
            if (template is not null)
            {
                return template;
            }
        }

        throw new InvalidOperationException("AislePilot MealTemplates is empty.");
    }

    private static IReadOnlyList<object> CreateMealTemplatesFromSeed(object seedMealTemplate, IReadOnlyList<string> mealNames)
    {
        var mealType = seedMealTemplate.GetType();
        var constructor = mealType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 5 &&
                       parameters[0].ParameterType == typeof(string);
            });
        Assert.NotNull(constructor);
        var baseCostForTwo = mealType.GetProperty("BaseCostForTwo")?.GetValue(seedMealTemplate)
            ?? throw new InvalidOperationException("Missing BaseCostForTwo property.");
        var isQuick = mealType.GetProperty("IsQuick")?.GetValue(seedMealTemplate)
            ?? throw new InvalidOperationException("Missing IsQuick property.");
        var tags = mealType.GetProperty("Tags")?.GetValue(seedMealTemplate)
            ?? throw new InvalidOperationException("Missing Tags property.");
        var ingredients = mealType.GetProperty("Ingredients")?.GetValue(seedMealTemplate)
            ?? throw new InvalidOperationException("Missing Ingredients property.");

        var templates = new List<object>(mealNames.Count);
        foreach (var mealName in mealNames)
        {
            var template = constructor!.Invoke([mealName, baseCostForTwo, isQuick, tags, ingredients]);
            templates.Add(template);
        }

        return templates;
    }

    private static void InvokeAddMealsToAiPool(IReadOnlyList<object> meals)
    {
        Assert.NotEmpty(meals);

        var method = typeof(AislePilotService).GetMethod("AddMealsToAiPool", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var mealType = meals[0].GetType();
        var mealArray = Array.CreateInstance(mealType, meals.Count);
        for (var index = 0; index < meals.Count; index++)
        {
            mealArray.SetValue(meals[index], index);
        }

        method!.Invoke(null, [mealArray]);
    }

    private static void InvokePruneAiMealPool(DateTime nowUtc)
    {
        var method = typeof(AislePilotService).GetMethod("PruneAiMealPool", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method!.Invoke(null, [nowUtc]);
    }

    private static IReadOnlyList<AislePilotPantrySuggestionViewModel> InvokeOrderPantrySuggestionsByMatch(
        IReadOnlyList<object> mealTemplates,
        IReadOnlyList<AislePilotPantrySuggestionViewModel> suggestions)
    {
        Assert.Equal(mealTemplates.Count, suggestions.Count);

        var method = typeof(AislePilotService).GetMethod("OrderPantrySuggestionsByMatch", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var parameterType = method!.GetParameters()[0].ParameterType;
        var tupleType = parameterType.GetGenericArguments()[0];
        var tupleArray = Array.CreateInstance(tupleType, mealTemplates.Count);

        for (var index = 0; index < mealTemplates.Count; index++)
        {
            var entry = Activator.CreateInstance(tupleType, mealTemplates[index], suggestions[index]);
            Assert.NotNull(entry);
            tupleArray.SetValue(entry, index);
        }

        var ordered = method.Invoke(null, [tupleArray]) as IEnumerable;
        Assert.NotNull(ordered);

        var orderedSuggestions = new List<AislePilotPantrySuggestionViewModel>();
        foreach (var entry in ordered!)
        {
            Assert.NotNull(entry);
            var suggestionField = entry!.GetType().GetField("Item2", BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(suggestionField);
            var suggestion = suggestionField!.GetValue(entry) as AislePilotPantrySuggestionViewModel;
            Assert.NotNull(suggestion);
            orderedSuggestions.Add(suggestion!);
        }

        return orderedSuggestions;
    }

    private static int GetPrivateStaticInt(string fieldName)
    {
        var field = typeof(AislePilotService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.NotNull(value);
        return (int)value!;
    }

    private static TimeSpan GetPrivateStaticTimeSpan(string fieldName)
    {
        var field = typeof(AislePilotService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.NotNull(value);
        return (TimeSpan)value!;
    }

    private static string InvokeNormalizeImageUrl(string? imageUrl)
    {
        var method = typeof(AislePilotService).GetMethod("NormalizeImageUrl", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, [imageUrl]);
        Assert.NotNull(result);
        return (string)result!;
    }

    private static void SetAiPoolTouchedUtc(string mealName, DateTime touchedUtc)
    {
        var touchedMap = GetRequiredStaticField("AiMealPoolLastTouchedUtc");
        var indexer = touchedMap.GetType().GetProperty("Item");
        Assert.NotNull(indexer);
        indexer!.SetValue(touchedMap, touchedUtc, [mealName]);
    }

    private static bool AiPoolContains(string mealName)
    {
        var pool = GetRequiredStaticField("AiMealPool");
        var containsKey = pool.GetType().GetMethod("ContainsKey", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(containsKey);
        var result = containsKey!.Invoke(pool, [mealName]);
        Assert.NotNull(result);
        return (bool)result!;
    }

    private static int GetAiPoolCount()
    {
        var pool = GetRequiredStaticField("AiMealPool");
        var countProperty = pool.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(countProperty);
        var count = countProperty!.GetValue(pool);
        Assert.NotNull(count);
        return (int)count!;
    }

    private static object GetRequiredStaticField(string fieldName)
    {
        var field = typeof(AislePilotService).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var value = field!.GetValue(null);
        Assert.NotNull(value);
        return value!;
    }

    private static void ClearConcurrentDictionary(object dictionary)
    {
        var clear = dictionary.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(clear);
        clear!.Invoke(dictionary, null);
    }

    private static void RemoveFromConcurrentDictionary(object dictionary, string key)
    {
        var tryRemove = dictionary.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                method.Name == "TryRemove" &&
                method.GetParameters().Length == 2);
        Assert.NotNull(tryRemove);

        var valueParameterType = tryRemove!.GetParameters()[1].ParameterType.GetElementType();
        var removedValue = valueParameterType is null || !valueParameterType.IsValueType
            ? null
            : Activator.CreateInstance(valueParameterType);
        var args = new[] { (object?)key, removedValue };
        _ = tryRemove.Invoke(dictionary, args);
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode = statusCode;
        private readonly string _responseBody = responseBody;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody)
            });
        }
    }

    private sealed class SequentialResponseHandler(params (HttpStatusCode StatusCode, string ResponseBody)[] responses) : HttpMessageHandler
    {
        private readonly (HttpStatusCode StatusCode, string ResponseBody)[] _responses =
            responses is { Length: > 0 } ? responses : [(HttpStatusCode.OK, "{}")];
        private int _responseIndex;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var index = Math.Clamp(_responseIndex, 0, _responses.Length - 1);
            var response = _responses[index];
            if (_responseIndex < _responses.Length - 1)
            {
                _responseIndex++;
            }

            return Task.FromResult(new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.ResponseBody)
            });
        }
    }
}
