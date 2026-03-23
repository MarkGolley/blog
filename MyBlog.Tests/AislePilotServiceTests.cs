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
    [InlineData(0.45, "bottle", "0.45 bottle")]
    [InlineData(0.06, "jar", "0.06 jar")]
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
            "Mushroom spinach risotto"
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
        Assert.Equal(5, distinctMealCount);
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
            BindingFlags.NonPublic | BindingFlags.Static);
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
            "Mushroom spinach risotto"
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

        Assert.Equal(1, handler.CallCount);
        Assert.False(result.UsedAiGeneratedMeals);
        Assert.Equal("Template fallback", result.PlanSourceLabel);
        Assert.Equal(7, result.MealPlan.Count);
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
}
