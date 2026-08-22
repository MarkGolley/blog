using System.Collections;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    [Fact]
    public async Task FastModePoolReplenishment_ConcurrentRequests_CoalesceAndRecoverFromTransientOpenAiFailure()
    {
        ClearAiPool();
        ClearConcurrentDictionary(GetRequiredStaticField("PlanPoolReplenishmentInFlight"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:EnableInteractiveAiGeneration"] = "false",
                ["AislePilot:AllowTemplateFallback"] = "true",
                ["AislePilot:BackgroundQueueCapacity"] = "8",
                ["AislePilot:BackgroundQueueConcurrency"] = "2",
                ["AislePilot:BackgroundQueueMaxAttempts"] = "2",
                ["AislePilot:BackgroundQueueRetryBaseDelayMs"] = "10"
            })
            .Build();
        var payloadContent = """
        {
          "meals": [
            {
              "name": "Concurrent replenishment recovery bowl",
              "baseCostForTwo": 5.8,
              "isQuick": true,
              "tags": ["Balanced"],
              "mealTypes": ["Dinner"],
              "recipeSteps": [
                "Heat a non-stick pan over medium heat for two minutes.",
                "Cook the onions gently for five minutes until softened.",
                "Add the chicken and cook thoroughly until no pink remains.",
                "Fold through the rice and vegetables until piping hot.",
                "Serve straight away in warm bowls."
              ],
              "ingredients": [
                { "name": "Chicken breast", "department": "Meat & Fish", "quantityForTwo": 0.35, "unit": "kg", "estimatedCostForTwo": 2.7 },
                { "name": "Rice", "department": "Tins & Dry Goods", "quantityForTwo": 0.4, "unit": "kg", "estimatedCostForTwo": 0.95 },
                { "name": "Bell peppers", "department": "Produce", "quantityForTwo": 2, "unit": "pcs", "estimatedCostForTwo": 1.2 }
              ]
            }
          ]
        }
        """;
        var successResponse = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = payloadContent } } }
        });
        using var handler = new SequentialResponseHandler(
            (HttpStatusCode.TooManyRequests, "{\"error\":\"transient rate limit\"}"),
            (HttpStatusCode.OK, successResponse));
        using var httpClient = new HttpClient(handler);
        using var queue = new AislePilotBackgroundTaskQueue(
            configuration,
            NullLogger<AislePilotBackgroundTaskQueue>.Instance);
        var service = new AislePilotService(
            httpClient,
            configuration,
            backgroundTaskQueue: queue);
        var request = new AislePilotRequestModel
        {
            Supermarket = "Tesco",
            DietaryModes = ["Balanced"],
            PlanDays = 1,
            CookDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            WeeklyBudget = 65m,
            HouseholdSize = 2
        };

        await queue.StartAsync(CancellationToken.None);
        try
        {
            var interactivePlans = await Task.WhenAll(
                Enumerable.Range(0, 12).Select(_ => service.BuildPlanAsync(request)));

            Assert.All(interactivePlans, plan =>
            {
                Assert.Single(plan.MealPlan);
                Assert.Contains(
                    plan.PlanSourceLabel,
                    new[] { "AislePilot recipe plan", "Personalised meal plan" });
            });
            Assert.Contains(interactivePlans, plan =>
                plan.PlanSourceLabel.Equals("AislePilot recipe plan", StringComparison.Ordinal));

            var inFlight = Assert.IsAssignableFrom<ICollection>(
                GetRequiredStaticField("PlanPoolReplenishmentInFlight"));
            await WaitForConditionAsync(
                () => handler.CallCount >= 2 && inFlight.Count == 0,
                TimeSpan.FromSeconds(5));

            var snapshot = queue.GetSnapshot();
            Assert.True(snapshot.AcceptedCount >= 1);
            Assert.Equal(0, snapshot.RejectedCount);
            Assert.True(snapshot.CompletedCount >= 1);
            Assert.Equal(0, snapshot.FaultedCount);
            Assert.Equal(2, handler.CallCount);

            var pooledPlan = await service.BuildPlanAsync(request);
            Assert.Equal("Personalised meal plan", pooledPlan.PlanSourceLabel);
            Assert.True(pooledPlan.UsedAiGeneratedMeals);
            Assert.Single(pooledPlan.MealPlan);

            Assert.Empty(inFlight.Cast<object>());
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
            ClearAiPool();
            ClearConcurrentDictionary(GetRequiredStaticField("PlanPoolReplenishmentInFlight"));
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(20, timeoutCts.Token);
        }
    }
}
