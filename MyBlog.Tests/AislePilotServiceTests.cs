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
    private readonly AislePilotService _service = new();

    [Theory]
    [InlineData(0.32, "kg", "320 g")]
    [InlineData(1.00, "kg", "1000 g")]
    [InlineData(1.24, "kg", "1.2 kg")]
    [InlineData(2.06, "kg", "2.1 kg")]
    [InlineData(0.12, "bottle", "12 tsp")]
    [InlineData(1.2, "bottle", "1 bottle + 20 tsp")]
    [InlineData(0.06, "jar", "2 1/2 tsp")]
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
    [InlineData(0.38, "tin", "half a tin")]
    [InlineData(0.19, "", "1/4")]
    [InlineData(5.63, "ml", "1 tsp")]
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
    public async Task BuildPlanFromCurrentMealsAsync_ShoppingList_ConvertsFractionalBottleUnitsToSpoonMeasures()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"]
        };
        var currentPlanMealNames = new List<string>
        {
            "Chicken stir fry with rice",
            "Egg fried rice"
        };

        var result = await _service.BuildPlanFromCurrentMealsAsync(request, currentPlanMealNames);
        var soySauce = Assert.Single(
            result.ShoppingItems,
            item => item.Name.Equals("Soy sauce", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("7 1/4 tbsp", soySauce.QuantityDisplay);
        Assert.DoesNotContain("bottle", soySauce.QuantityDisplay, StringComparison.OrdinalIgnoreCase);
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
    public void GetDietarySelectionRules_CapsSelectionsAndSeparatesCoreStylesFromOverlays()
    {
        var rules = _service.GetDietarySelectionRules();

        Assert.Equal(2, rules.MaxSelections);
        Assert.Contains("Balanced", rules.CoreModes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Vegan", rules.CoreModes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("High-Protein", rules.OverlayModes, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Gluten-Free", rules.OverlayModes, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("High-Protein", rules.CoreModes, StringComparer.OrdinalIgnoreCase);
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
    public async Task BuildPlanFromCurrentMealsAsync_UsesRecipeSpecificMealTimesInsteadOfFlattenedFallbacks()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 200m,
            HouseholdSize = 2,
            PlanDays = 3,
            CookDays = 3,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"]
        };

        var result = await _service.BuildPlanFromCurrentMealsAsync(
            request,
            ["Egg fried rice", "Turkey chilli with beans", "Salmon, potatoes, and broccoli"]);

        Assert.Equal(3, result.MealPlan.Count);
        Assert.Equal(
            ["Egg fried rice", "Turkey chilli with beans", "Salmon, potatoes, and broccoli"],
            result.MealPlan.Select(meal => meal.MealName).ToArray());

        var eggFriedRice = Assert.Single(result.MealPlan, meal => meal.MealName.Equals("Egg fried rice", StringComparison.OrdinalIgnoreCase));
        var turkeyChilli = Assert.Single(result.MealPlan, meal => meal.MealName.Equals("Turkey chilli with beans", StringComparison.OrdinalIgnoreCase));
        var salmon = Assert.Single(result.MealPlan, meal => meal.MealName.Equals("Salmon, potatoes, and broccoli", StringComparison.OrdinalIgnoreCase));

        Assert.All(result.MealPlan, meal => Assert.Equal(0, meal.EstimatedPrepMinutes % 5));
        Assert.True(eggFriedRice.EstimatedPrepMinutes < turkeyChilli.EstimatedPrepMinutes);
        Assert.True(eggFriedRice.EstimatedPrepMinutes < salmon.EstimatedPrepMinutes);
        Assert.Equal(3, result.MealPlan.Select(meal => meal.EstimatedPrepMinutes).Distinct().Count());
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
    public void SelectCandidateForSlot_WhenBreakfastCapIsExhausted_DoesNotCollapseToSingleMeal()
    {
        var seedMealTemplate = GetMealTemplateSeed();
        var templates = CreateMealTemplatesFromSeed(
            seedMealTemplate,
            ["Breakfast option alpha", "Breakfast option beta", "Lunch option steady", "Dinner option steady"]);
        var breakfastAlpha = templates[0];
        var breakfastBeta = templates[1];
        var lunchSteady = templates[2];
        var dinnerSteady = templates[3];

        var selectMethod = typeof(AislePilotService).GetMethod("SelectCandidateForSlot", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(selectMethod);

        var mealType = seedMealTemplate.GetType();
        var mealListType = typeof(List<>).MakeGenericType(mealType);
        var selectedMeals = Activator.CreateInstance(mealListType) as IList;
        Assert.NotNull(selectedMeals);

        var slotPattern = new List<string> { "Breakfast", "Lunch", "Dinner" };
        const int totalSlotCount = 21;
        for (var slotIndex = 0; slotIndex < totalSlotCount; slotIndex++)
        {
            var slotMealType = slotPattern[slotIndex % slotPattern.Count];
            var slotCompatibleCandidates = Activator.CreateInstance(mealListType) as IList;
            Assert.NotNull(slotCompatibleCandidates);

            if (slotMealType.Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
            {
                slotCompatibleCandidates!.Add(breakfastAlpha);
                slotCompatibleCandidates.Add(breakfastBeta);
            }
            else if (slotMealType.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
            {
                slotCompatibleCandidates!.Add(lunchSteady);
            }
            else
            {
                slotCompatibleCandidates!.Add(dinnerSteady);
            }

            var chosen = selectMethod!.Invoke(
                null,
                [slotCompatibleCandidates, selectedMeals, slotPattern, slotIndex, totalSlotCount, null, false]);
            Assert.NotNull(chosen);
            selectedMeals!.Add(chosen!);
        }

        var nameProperty = mealType.GetProperty("Name");
        Assert.NotNull(nameProperty);

        var breakfastMeals = new List<string>();
        for (var index = 0; index < selectedMeals!.Count; index++)
        {
            if (!slotPattern[index % slotPattern.Count].Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var mealName = nameProperty!.GetValue(selectedMeals[index]) as string;
            Assert.False(string.IsNullOrWhiteSpace(mealName));
            breakfastMeals.Add(mealName!);
        }

        var maxBreakfastRepeatCount = breakfastMeals
            .GroupBy(mealName => mealName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Count())
            .DefaultIfEmpty(0)
            .Max();

        Assert.Equal(7, breakfastMeals.Count);
        Assert.True(
            maxBreakfastRepeatCount <= 4,
            $"Expected breakfast repeats to distribute across available options, but found a repeat count of {maxBreakfastRepeatCount}.");
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
    public void HasCompatibleMeals_WithMoreThanTwoDietaryModes_ReturnsFalse()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["High-Protein", "Pescatarian", "Gluten-Free"],
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"]
        };

        var hasCompatibleMeals = _service.HasCompatibleMeals(request);

        Assert.False(hasCompatibleMeals);
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
        var breakfastSlotIndex = initialPlan.MealPlan
            .Select((meal, index) => new { meal.MealType, index })
            .Where(entry => entry.MealType.Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        Assert.True(breakfastSlotIndex >= 0, "Expected at least one breakfast slot in a three-meals-per-day plan.");
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
    public void BuildPlan_WithRequestedDistinctLeftoverSourceDays_PreservesRequestedDistributionWhenFeasible()
    {
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            CookDays = 5,
            LeftoverCookDayIndexesCsv = "0,2"
        };

        var result = _service.BuildPlan(request);
        var leftoverSourceDayIndexes = ExtractLeftoverSourceDayIndexes(result.MealPlan);

        Assert.Equal([0, 2], leftoverSourceDayIndexes);
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
    public async Task BuildPlanFromCurrentMealsAsync_MergesEquivalentShoppingItemsAcrossIngredientVariants()
    {
        ClearAiPool();

        var mealOneName = $"Smoky bean quinoa bowl {Guid.NewGuid():N}";
        var mealTwoName = $"Zesty quinoa chilli {Guid.NewGuid():N}";
        var mealOne = CreateMealTemplateWithIngredients(
            mealOneName,
            [
                ("Black Beans, Canned", "Tins & Dry Goods", 1m, "tin", 0.90m),
                ("Quinoa", "Tins & Dry Goods", 180m, "grams", 1.20m),
                ("Lime", "Produce", 1m, "pcs", 0.40m)
            ]);
        var mealTwo = CreateMealTemplateWithIngredients(
            mealTwoName,
            [
                ("Black Beans", "Tins & Dry Goods", 1m, "can", 0.80m),
                ("Quinoa", "Tins & Dry Goods", 120m, "g", 0.80m),
                ("Onion", "Produce", 1m, "pcs", 0.30m)
            ]);

        InvokeAddMealsToAiPool([mealOne, mealTwo]);

        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 1,
            DietaryModes = ["Balanced"]
        };

        try
        {
            var result = await _service.BuildPlanFromCurrentMealsAsync(request, [mealOneName, mealTwoName]);

            var blackBeanItems = result.ShoppingItems
                .Where(item => item.Name.Contains("black bean", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var quinoaItems = result.ShoppingItems
                .Where(item => item.Name.Contains("quinoa", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Single(blackBeanItems);
            Assert.Single(quinoaItems);
            Assert.Equal(2m, blackBeanItems[0].Quantity);
            Assert.Equal(300m, quinoaItems[0].Quantity);
        }
        finally
        {
            ClearAiPool();
        }
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_MergesProduceVariantsAndRoundsShoppingQuantitiesForDisplay()
    {
        ClearAiPool();

        var mealOneName = $"Greens lunch {Guid.NewGuid():N}";
        var mealTwoName = $"Salad lunch {Guid.NewGuid():N}";
        var mealThreeName = $"Soup lunch {Guid.NewGuid():N}";
        var mealOne = CreateMealTemplateWithIngredients(
            mealOneName,
            [
                ("Fresh spinach", "Produce", 150m, "g", 0.75m),
                ("Mixed leaves", "Produce", 18.75m, "g", 0.15m),
                ("Tomatoes", "Produce", 20m, "g", 0.14m)
            ]);
        var mealTwo = CreateMealTemplateWithIngredients(
            mealTwoName,
            [
                ("Spinach", "Produce", 75m, "g", 0.45m),
                ("Mixed salad leaves", "Produce", 28.13m, "g", 0.56m),
                ("Tomatoes", "Produce", 1m, "pc", 0.25m)
            ]);
        var mealThree = CreateMealTemplateWithIngredients(
            mealThreeName,
            [
                ("Spinach(fresh)", "Produce", 93.75m, "g", 0.75m)
            ]);

        InvokeAddMealsToAiPool([mealOne, mealTwo, mealThree]);

        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 3,
            CookDays = 3,
            MealsPerDay = 1,
            DietaryModes = ["Balanced"]
        };

        try
        {
            var result = await _service.BuildPlanFromCurrentMealsAsync(request, [mealOneName, mealTwoName, mealThreeName]);

            var spinachItems = result.ShoppingItems
                .Where(item => item.Name.Contains("spinach", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var mixedLeafItems = result.ShoppingItems
                .Where(item => item.Name.Contains("mixed", StringComparison.OrdinalIgnoreCase) ||
                               item.Name.Contains("salad", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var tomatoItems = result.ShoppingItems
                .Where(item => item.Name.Contains("tomato", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var spinach = Assert.Single(spinachItems);
            var mixedLeaves = Assert.Single(mixedLeafItems);
            var tomatoes = Assert.Single(tomatoItems);

            Assert.Equal("Spinach", spinach.Name);
            Assert.Equal("g", spinach.Unit);
            Assert.Equal("320 g", spinach.QuantityDisplay);

            Assert.Equal("Mixed leaves", mixedLeaves.Name);
            Assert.Equal("g", mixedLeaves.Unit);
            Assert.Equal("50 g", mixedLeaves.QuantityDisplay);

            Assert.Equal("Tomatoes", tomatoes.Name);
            Assert.Equal("pcs", tomatoes.Unit);
            Assert.Equal("2 pcs", tomatoes.QuantityDisplay);
        }
        finally
        {
            ClearAiPool();
        }
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_NormalizesIngredientDisplayCapitalizationAcrossMealAndShoppingViews()
    {
        ClearAiPool();

        var mealOneName = $"Creamy greens pasta {Guid.NewGuid():N}";
        var mealTwoName = $"Warm greens tray {Guid.NewGuid():N}";
        var mealOne = CreateMealTemplateWithIngredients(
            mealOneName,
            [
                ("spINNACH", "Produce", 1m, "pcs", 0.60m),
                ("garLIC", "Produce", 2m, "pcs", 0.20m)
            ]);
        var mealTwo = CreateMealTemplateWithIngredients(
            mealTwoName,
            [
                ("spinnach", "Produce", 2m, "pcs", 1.10m),
                ("LEMON zest", "Produce", 1m, "pcs", 0.35m)
            ]);

        InvokeAddMealsToAiPool([mealOne, mealTwo]);

        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"]
        };

        try
        {
            var result = await _service.BuildPlanFromCurrentMealsAsync(request, [mealOneName, mealTwoName]);

            var normalizedIngredientLines = result.MealPlan
                .SelectMany(meal => meal.IngredientLines)
                .Where(line => line.Contains("spinnach", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("garlic", StringComparison.OrdinalIgnoreCase) ||
                               line.Contains("lemon zest", StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.Contains(normalizedIngredientLines, line => line.Contains("Spinnach", StringComparison.Ordinal));
            Assert.Contains(normalizedIngredientLines, line => line.Contains("Garlic", StringComparison.Ordinal));
            Assert.Contains(normalizedIngredientLines, line => line.Contains("Lemon zest", StringComparison.Ordinal));
            Assert.DoesNotContain(normalizedIngredientLines, line => line.Contains("spINNACH", StringComparison.Ordinal));
            Assert.DoesNotContain(normalizedIngredientLines, line => line.Contains("spinnach", StringComparison.Ordinal));
            Assert.DoesNotContain(normalizedIngredientLines, line => line.Contains("garLIC", StringComparison.Ordinal));
            Assert.DoesNotContain(normalizedIngredientLines, line => line.Contains("LEMON zest", StringComparison.Ordinal));

            var spinnachItem = Assert.Single(result.ShoppingItems, item =>
                item.Name.Contains("spinnach", StringComparison.OrdinalIgnoreCase));
            var garlicItem = Assert.Single(result.ShoppingItems, item =>
                item.Name.Contains("garlic", StringComparison.OrdinalIgnoreCase));
            var lemonItem = Assert.Single(result.ShoppingItems, item =>
                item.Name.Contains("lemon zest", StringComparison.OrdinalIgnoreCase));

            Assert.Equal("Spinnach", spinnachItem.Name);
            Assert.Equal("Garlic", garlicItem.Name);
            Assert.Equal("Lemon zest", lemonItem.Name);
        }
        finally
        {
            ClearAiPool();
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
    public async Task BuildPlanFromCurrentMealsAsync_MergesEquivalentShoppingLiquidsAcrossUnitsAndDepartments()
    {
        ClearAiPool();

        var mealOneName = $"Olive oil pasta {Guid.NewGuid():N}";
        var mealTwoName = $"Olive oil salad {Guid.NewGuid():N}";
        var mealThreeName = $"Olive oil roast veg {Guid.NewGuid():N}";
        var mealOne = CreateMealTemplateWithIngredients(
            mealOneName,
            [
                ("Olive oil", "Spices & Sauces", 0.08m, "bottle", 0.55m),
                ("Pasta", "Tins & Dry Goods", 200m, "g", 0.70m),
                ("Garlic", "Produce", 2m, "pcs", 0.10m)
            ]);
        var mealTwo = CreateMealTemplateWithIngredients(
            mealTwoName,
            [
                ("olive oil", "Other", 1.25m, "tsp", 0.04m),
                ("Lettuce", "Produce", 1m, "pcs", 0.65m),
                ("Cherry tomatoes", "Produce", 0.25m, "kg", 0.90m)
            ]);
        var mealThree = CreateMealTemplateWithIngredients(
            mealThreeName,
            [
                ("Olive oil", "Spices & Sauces", 0.02m, "l", 0.14m),
                ("Courgettes", "Produce", 2m, "pcs", 0.70m),
                ("Peppers", "Produce", 2m, "pcs", 0.80m)
            ]);

        InvokeAddMealsToAiPool([mealOne, mealTwo, mealThree]);

        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 3,
            CookDays = 3,
            MealsPerDay = 1,
            DietaryModes = ["Balanced"]
        };

        try
        {
            var result = await _service.BuildPlanFromCurrentMealsAsync(request, [mealOneName, mealTwoName, mealThreeName]);

            var oliveOilItems = result.ShoppingItems
                .Where(item => item.Name.Equals("Olive oil", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var oliveOil = Assert.Single(oliveOilItems);
            Assert.Equal("Spices & Sauces", oliveOil.Department);
            Assert.Equal("ml", oliveOil.Unit);
            Assert.Equal(66.25m, oliveOil.Quantity);
            Assert.Equal("4 1/2 tbsp", oliveOil.QuantityDisplay);
        }
        finally
        {
            ClearAiPool();
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
    public async Task BuildPlanFromCurrentMealsAsync_WithExtraCurrentPlanMealNames_TrimsToExpectedMealCount()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 120m,
            HouseholdSize = 2,
            PlanDays = 3,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"]
        };
        var currentPlanMealNames = new List<string>
        {
            "Turkey chilli with beans",
            "Chicken stir fry with rice"
        };

        var result = await _service.BuildPlanFromCurrentMealsAsync(request, currentPlanMealNames);
        var resultMealNames = result.MealPlan
            .Select(meal => meal.MealName)
            .ToList();

        Assert.Single(resultMealNames);
        Assert.Equal("Turkey chilli with beans", resultMealNames[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_WithFewerCurrentPlanMealNames_TopsUpAndPreservesPrefix()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 120m,
            HouseholdSize = 2,
            PlanDays = 3,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"]
        };
        var currentPlanMealNames = new List<string>
        {
            "Turkey chilli with beans"
        };

        var result = await _service.BuildPlanFromCurrentMealsAsync(request, currentPlanMealNames);
        var resultMealNames = result.MealPlan
            .Select(meal => meal.MealName)
            .ToList();

        Assert.Equal(2, resultMealNames.Count);
        Assert.Equal("Turkey chilli with beans", resultMealNames[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_WithUnresolvableCurrentPlanMealNames_FallsBackToTemplatesWithoutThrowing()
    {
        var request = new AislePilotRequestModel
        {
            WeeklyBudget = 120m,
            HouseholdSize = 2,
            PlanDays = 3,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"]
        };
        var currentPlanMealNames = new List<string>
        {
            "Meal that does not exist anywhere"
        };

        var result = await _service.BuildPlanFromCurrentMealsAsync(request, currentPlanMealNames);
        var resultMealNames = result.MealPlan
            .Select(meal => meal.MealName)
            .ToList();

        Assert.Equal(2, resultMealNames.Count);
        Assert.DoesNotContain("Meal that does not exist anywhere", resultMealNames, StringComparer.OrdinalIgnoreCase);
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

    private static void RemoveAiPoolMeal(string mealName)
    {
        RemoveFromConcurrentDictionary(GetRequiredStaticField("AiMealPool"), mealName);
        RemoveFromConcurrentDictionary(GetRequiredStaticField("AiMealPoolLastTouchedUtc"), mealName);
    }

    private static IReadOnlyList<string> GetCompatibleTemplateMealNamesForSlot(
        IReadOnlyList<string> dietaryModes,
        string? dislikesOrAllergens,
        string mealType)
    {
        var filterMethod = typeof(AislePilotService).GetMethod("FilterMeals", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(filterMethod);
        var supportsMealTypeMethod = typeof(AislePilotService).GetMethod("SupportsMealType", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(supportsMealTypeMethod);

        var filteredTemplates = filterMethod!.Invoke(null, [dietaryModes, dislikesOrAllergens ?? string.Empty, null]) as IEnumerable;
        Assert.NotNull(filteredTemplates);

        var mealNames = new List<string>();
        foreach (var template in filteredTemplates!)
        {
            if (template is null)
            {
                continue;
            }

            var supportsSlot = supportsMealTypeMethod!.Invoke(null, [template, mealType]);
            Assert.NotNull(supportsSlot);
            if (!(bool)supportsSlot!)
            {
                continue;
            }

            var name = template.GetType().GetProperty("Name")?.GetValue(template) as string;
            if (!string.IsNullOrWhiteSpace(name))
            {
                mealNames.Add(name.Trim());
            }
        }

        return mealNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static object CreateMealTemplateWithIngredients(
        string mealName,
        IReadOnlyList<(string Name, string Department, decimal QuantityForTwo, string Unit, decimal EstimatedCostForTwo)> ingredients)
    {
        var mealTemplateType = typeof(AislePilotService).GetNestedType("MealTemplate", BindingFlags.NonPublic);
        Assert.NotNull(mealTemplateType);
        var ingredientTemplateType = typeof(AislePilotService).GetNestedType("IngredientTemplate", BindingFlags.NonPublic);
        Assert.NotNull(ingredientTemplateType);

        var ingredientConstructor = ingredientTemplateType!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 5 && parameters[0].ParameterType == typeof(string);
            });
        Assert.NotNull(ingredientConstructor);

        var typedIngredientList = Activator.CreateInstance(typeof(List<>).MakeGenericType(ingredientTemplateType)) as IList;
        Assert.NotNull(typedIngredientList);

        foreach (var ingredient in ingredients)
        {
            var ingredientTemplate = ingredientConstructor!.Invoke(
            [
                ingredient.Name,
                ingredient.Department,
                ingredient.QuantityForTwo,
                ingredient.Unit,
                ingredient.EstimatedCostForTwo
            ]);
            typedIngredientList!.Add(ingredientTemplate);
        }

        var mealConstructor = mealTemplateType!.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 5 && parameters[0].ParameterType == typeof(string);
            });
        Assert.NotNull(mealConstructor);

        return mealConstructor!.Invoke(
        [
            mealName,
            6m,
            true,
            new List<string> { "Balanced" },
            typedIngredientList!
        ]);
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

    private static IReadOnlyList<int> ExtractLeftoverSourceDayIndexes(IReadOnlyList<AislePilotMealDayViewModel> mealPlan)
    {
        var weekDayLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Monday"] = 0,
            ["Tuesday"] = 1,
            ["Wednesday"] = 2,
            ["Thursday"] = 3,
            ["Friday"] = 4,
            ["Saturday"] = 5,
            ["Sunday"] = 6
        };
        var sourceDayIndexes = new List<int>();
        foreach (var meal in mealPlan)
        {
            if (meal.LeftoverDaysCovered <= 0)
            {
                continue;
            }

            if (!weekDayLookup.TryGetValue(meal.Day ?? string.Empty, out var dayIndex))
            {
                continue;
            }

            for (var i = 0; i < meal.LeftoverDaysCovered; i++)
            {
                sourceDayIndexes.Add(dayIndex);
            }
        }

        return sourceDayIndexes;
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
