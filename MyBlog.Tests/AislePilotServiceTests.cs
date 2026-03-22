using System.Globalization;
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
    [InlineData(0.45, "bottle", "1 bottle")]
    [InlineData(1.2, "bottle", "2 bottles")]
    [InlineData(0.06, "jar", "1 jar")]
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
        var currentPlanMealNames = initialPlan.MealPlan.Select(meal => meal.MealName).ToList();

        var swappedPlan = _service.SwapMealForDay(request, 0, currentMealName, currentPlanMealNames, [currentMealName]);

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
            HouseholdSize = 2
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
        var field = typeof(AislePilotService).GetField("AiMealPool", BindingFlags.NonPublic | BindingFlags.Static);
        var pool = field?.GetValue(null);
        if (pool is null)
        {
            return;
        }

        var tryRemove = pool.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method =>
                method.Name == "TryRemove" &&
                method.GetParameters().Length == 2);
        if (tryRemove is null)
        {
            return;
        }

        var args = new object?[] { mealName, null };
        _ = tryRemove.Invoke(pool, args);
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
