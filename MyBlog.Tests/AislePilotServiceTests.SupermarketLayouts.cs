using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    [Fact]
    public void GetSupportedSupermarkets_IncludesExpandedPhysicalStoreChains()
    {
        var supermarkets = _service.GetSupportedSupermarkets();

        Assert.Contains("Tesco", supermarkets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Morrisons", supermarkets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Waitrose", supermarkets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Co-op", supermarkets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Iceland", supermarkets, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("M&S Food", supermarkets, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ocado", supermarkets, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPlan_Aldi_UsesCuratedDefaultLayoutMetadata_AndDoesNotPutBakerySecond()
    {
        var request = new AislePilotRequestModel
        {
            Supermarket = "Aldi",
            DietaryModes = ["Balanced"],
            WeeklyBudget = 70m,
            HouseholdSize = 2
        };

        var result = _service.BuildPlan(request);

        Assert.Equal("Curated chain default", result.LayoutInsight.SourceLabel);
        Assert.True(result.LayoutInsight.NeedsReview);
        Assert.NotEmpty(result.AisleOrderUsed);
        Assert.Equal("Produce", result.AisleOrderUsed[0]);
        Assert.NotEqual("Bakery", result.AisleOrderUsed[1]);
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_AdjustsCostsBySupermarket_WithoutChangingShoppingQuantities()
    {
        var mealNames = new List<string>
        {
            "Chicken stir fry with rice",
            "Egg fried rice"
        };

        var aldiRequest = new AislePilotRequestModel
        {
            Supermarket = "Aldi",
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"]
        };
        var waitroseRequest = new AislePilotRequestModel
        {
            Supermarket = "Waitrose",
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 2,
            CookDays = 2,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            DietaryModes = ["Balanced"]
        };

        var aldi = await _service.BuildPlanFromCurrentMealsAsync(aldiRequest, mealNames);
        var waitrose = await _service.BuildPlanFromCurrentMealsAsync(waitroseRequest, mealNames);

        Assert.Equal("Reviewed public basket benchmark", aldi.PriceInsight.SourceLabel);
        Assert.True(aldi.PriceInsight.IsDirectBasketData);
        Assert.False(aldi.PriceInsight.NeedsReview);
        Assert.Equal(0.85m, aldi.PriceInsight.RelativeCostFactor);
        Assert.Equal(1.17m, waitrose.PriceInsight.RelativeCostFactor);
        Assert.True(aldi.EstimatedTotalCost < waitrose.EstimatedTotalCost);

        var aldiItems = aldi.ShoppingItems
            .OrderBy(item => item.Department, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var waitroseItems = waitrose.ShoppingItems
            .OrderBy(item => item.Department, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(aldiItems.Count, waitroseItems.Count);
        for (var index = 0; index < aldiItems.Count; index++)
        {
            Assert.Equal(aldiItems[index].Department, waitroseItems[index].Department);
            Assert.Equal(aldiItems[index].Name, waitroseItems[index].Name);
            Assert.Equal(aldiItems[index].Unit, waitroseItems[index].Unit);
            Assert.Equal(aldiItems[index].Quantity, waitroseItems[index].Quantity);
        }

        Assert.Contains(
            aldiItems.Zip(waitroseItems),
            pair => pair.First.EstimatedCost < pair.Second.EstimatedCost);
    }

    [Fact]
    public void BuildPlan_CoOp_UsesEstimatedPriceProfileMetadata()
    {
        var request = new AislePilotRequestModel
        {
            Supermarket = "Co-op",
            DietaryModes = ["Balanced"],
            WeeklyBudget = 70m,
            HouseholdSize = 2
        };

        var result = _service.BuildPlan(request);

        Assert.Equal("Reviewed chain positioning estimate", result.PriceInsight.SourceLabel);
        Assert.True(result.PriceInsight.NeedsReview);
        Assert.False(result.PriceInsight.IsDirectBasketData);
        Assert.True(result.PriceInsight.RelativeCostFactor > 1m);
    }

    [Fact]
    public async Task BuildPlanFromCurrentMealsAsync_WithReviewedPriceFileOverride_UsesFileBackedProfile()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "aislepilot-price-profile-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var filePath = Path.Combine(tempDirectory, "reviewed-price-profiles.json");
            await File.WriteAllTextAsync(filePath, """
{
  "version": 1,
  "reviewedAtUtc": "2026-04-20T00:00:00Z",
  "profiles": [
    {
      "supermarket": "Aldi",
      "relativeCostFactor": 0.91,
      "relativeCostBasis": "Temporary reviewed override for test",
      "sourceLabel": "Reviewed override file",
      "confidenceScore": 0.93,
      "confidenceLabel": "High",
      "isDirectBasketData": true,
      "needsReview": false,
      "lastVerifiedUtc": "2026-04-20T00:00:00Z",
      "evidence": [
        {
          "title": "Internal reviewed benchmark",
          "url": "https://example.test/reviewed-benchmark",
          "sourceType": "article"
        }
      ]
    }
  ]
}
""");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AislePilot:SupermarketPriceProfilesPath"] = filePath
                })
                .Build();
            var service = new AislePilotService(configuration: configuration);
            var request = new AislePilotRequestModel
            {
                Supermarket = "Aldi",
                WeeklyBudget = 80m,
                HouseholdSize = 2,
                PlanDays = 2,
                CookDays = 2,
                MealsPerDay = 1,
                SelectedMealTypes = ["Dinner"],
                DietaryModes = ["Balanced"]
            };
            var mealNames = new List<string>
            {
                "Chicken stir fry with rice",
                "Egg fried rice"
            };

            var result = await service.BuildPlanFromCurrentMealsAsync(request, mealNames);

            Assert.Equal("Reviewed override file", result.PriceInsight.SourceLabel);
            Assert.Equal(0.91m, result.PriceInsight.RelativeCostFactor);
            Assert.Equal("Temporary reviewed override for test", result.PriceInsight.RelativeCostBasis);
            Assert.False(result.PriceInsight.NeedsReview);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ParseSupermarketLayoutResearchResponse_WithWeakEvidence_ReturnsNull()
    {
        var payload = JsonSerializer.Serialize(new
        {
            output_text = JsonSerializer.Serialize(new
            {
                aisleOrder = new[]
                {
                    "Produce",
                    "Bakery",
                    "Meat & Fish",
                    "Dairy & Eggs",
                    "Frozen",
                    "Tins & Dry Goods"
                },
                confidenceScore = 0.54,
                confidenceLabel = "low",
                needsReview = true,
                sources = new[]
                {
                    new
                    {
                        title = "Forum post",
                        url = "https://forum.example.com/aldi-layout",
                        sourceType = "forum"
                    }
                }
            })
        });

        var parsed = InvokeParseSupermarketLayoutResearchResponse(payload, "Aldi");

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseSupermarketLayoutResearchResponse_WithStrongEvidence_ReturnsResearchBackedLayout()
    {
        var payload = JsonSerializer.Serialize(new
        {
            output_text = JsonSerializer.Serialize(new
            {
                aisleOrder = new[]
                {
                    "Produce",
                    "Tins & Dry Goods",
                    "Meat & Fish",
                    "Dairy & Eggs",
                    "Frozen",
                    "Bakery"
                },
                confidenceScore = 0.83,
                confidenceLabel = "high",
                needsReview = false,
                sources = new[]
                {
                    new
                    {
                        title = "Aldi typical store layout",
                        url = "https://www.aldi.co.uk/corporate/property/typical-store-layout",
                        sourceType = "official"
                    },
                    new
                    {
                        title = "Aldi specialbuys overview",
                        url = "https://www.aldi.co.uk/aldihub/about-aldi",
                        sourceType = "article"
                    },
                    new
                    {
                        title = "Retail walkthrough",
                        url = "https://retail.example.org/aldi-layout-guide",
                        sourceType = "article"
                    }
                }
            })
        });

        var parsed = InvokeParseSupermarketLayoutResearchResponse(payload, "Aldi");

        Assert.NotNull(parsed);
        Assert.Equal("AI web research", GetProperty<string>(parsed!, "SourceLabel"));
        Assert.Equal("High", GetProperty<string>(parsed!, "ConfidenceLabel"));
        Assert.False(GetProperty<bool>(parsed!, "NeedsReview"));

        var aisleOrder = GetProperty<IReadOnlyList<string>>(parsed!, "AisleOrder");
        Assert.Equal("Produce", aisleOrder[0]);
        Assert.Equal("Tins & Dry Goods", aisleOrder[1]);
    }

    private static object? InvokeParseSupermarketLayoutResearchResponse(string responseContent, string supermarket)
    {
        var method = typeof(AislePilotService).GetMethod(
            "ParseSupermarketLayoutResearchResponse",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method!.Invoke(null, [responseContent, supermarket]);
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        var value = property!.GetValue(target);
        Assert.NotNull(value);
        return (T)value!;
    }
}
