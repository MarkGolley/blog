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
}
