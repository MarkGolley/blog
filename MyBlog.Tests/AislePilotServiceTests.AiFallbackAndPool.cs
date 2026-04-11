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
            step => step.Contains("chicken", StringComparison.OrdinalIgnoreCase) ||
                    step.Contains("pepper", StringComparison.OrdinalIgnoreCase) ||
                    step.Contains("rice", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            recipeSteps,
            step => step.Contains("minute", StringComparison.OrdinalIgnoreCase) ||
                    step.Contains("200C", StringComparison.OrdinalIgnoreCase) ||
                    step.Contains("medium-high", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_WhenColdBreakfastMethodContainsDinnerStyleCooking_FallsBackToColdBreakfastSteps()
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
      "name": "Greek yogurt berry oat pots",
      "baseCostForTwo": 4.1,
      "isQuick": true,
      "tags": ["Balanced", "Vegetarian", "High-Protein"],
      "recipeSteps": [
        "Heat a saucepan over medium heat with a drizzle of oil.",
        "Cook the stock on the hob for 4 minutes until simmering.",
        "Stir in Greek yogurt and oats until smooth.",
        "Add frozen berries and honey, then cook for 2 minutes.",
        "Divide between bowls and serve warm."
      ],
      "ingredients": [
        { "name": "Greek yogurt", "department": "Dairy & Eggs", "quantityForTwo": 0.40, "unit": "kg", "estimatedCostForTwo": 1.50 },
        { "name": "Oats", "department": "Tins & Dry Goods", "quantityForTwo": 0.22, "unit": "kg", "estimatedCostForTwo": 0.40 },
        { "name": "Frozen berries", "department": "Frozen", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 1.50 },
        { "name": "Honey", "department": "Spices & Sauces", "quantityForTwo": 0.10, "unit": "jar", "estimatedCostForTwo": 0.70 }
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
            PlanDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Breakfast"],
            WeeklyBudget = 65m,
            HouseholdSize = 2
        };

        var result = service.BuildPlan(request);

        Assert.Equal(1, handler.CallCount);
        Assert.Single(result.MealPlan);
        var meal = result.MealPlan[0];
        Assert.Equal("Breakfast", meal.MealType);

        var recipeSteps = meal.RecipeSteps;
        Assert.True(recipeSteps.Count >= 5);
        Assert.DoesNotContain(recipeSteps, step => step.Contains("stock", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recipeSteps, step => step.Contains("hob", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(recipeSteps, step => step.Contains("saucepan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recipeSteps, step => step.Contains("yogurt", StringComparison.OrdinalIgnoreCase) || step.Contains("yoghurt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recipeSteps, step => step.Contains("oat", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(recipeSteps, step => step.Contains("chill", StringComparison.OrdinalIgnoreCase) || step.Contains("bowl", StringComparison.OrdinalIgnoreCase));
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
}
