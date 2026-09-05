using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void RuntimeConfiguration_DisablesInteractiveAislePilotAiGeneration(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "MyBlog",
            fileName));
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        var aislePilot = document.RootElement.GetProperty("AislePilot");

        Assert.False(aislePilot.GetProperty("EnableInteractiveAiGeneration").GetBoolean());
        Assert.True(aislePilot.GetProperty("EnableAiGeneration").GetBoolean());
    }

    [Fact]
    public async Task BuildPlanAsync_WithInteractiveAiDisabled_ReturnsWithoutWaitingForSlowAi()
    {
        ClearAiPool();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:EnableInteractiveAiGeneration"] = "false",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();
        using var handler = new SlowResponseHandler();
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);
        var request = new AislePilotRequestModel
        {
            Supermarket = "Tesco",
            DietaryModes = ["Balanced"],
            CookDays = 3,
            WeeklyBudget = 70m,
            HouseholdSize = 2
        };

        var stopwatch = Stopwatch.StartNew();
        var result = await service.BuildPlanAsync(request);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Fast plan took {stopwatch.Elapsed}.");
        Assert.Equal(0, handler.CallCount);
        Assert.Equal("AislePilot recipe plan", result.PlanSourceLabel);
        Assert.NotEmpty(result.MealPlan);
    }

    [Fact]
    public async Task BuildPlanAsync_WithInteractiveAiDisabled_IncludesDessertWithoutWaitingForExternalWork()
    {
        ClearAiPool();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:EnableInteractiveAiGeneration"] = "false",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();
        using var handler = new SlowResponseHandler();
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var stopwatch = Stopwatch.StartNew();
        var result = await service.BuildPlanAsync(new AislePilotRequestModel
        {
            Supermarket = "Tesco",
            DietaryModes = ["Balanced"],
            CookDays = 3,
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            IncludeDessertAddOn = true
        });

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Fast dessert plan took {stopwatch.Elapsed}.");
        Assert.Equal(0, handler.CallCount);
        Assert.True(result.IncludeDessertAddOn);
        Assert.False(string.IsNullOrWhiteSpace(result.DessertAddOnName));
        AssertValidCorePlan(result, expectedMealCount: 3);
    }

    [Fact]
    public async Task BuildPlanAsync_WithInteractiveAiDisabled_IncludesSpecialTreatWithoutCallingOpenAi()
    {
        ClearAiPool();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:EnableInteractiveAiGeneration"] = "false",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();
        using var handler = new SlowResponseHandler();
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var stopwatch = Stopwatch.StartNew();
        var result = await service.BuildPlanAsync(new AislePilotRequestModel
        {
            Supermarket = "Tesco",
            DietaryModes = ["Balanced"],
            CookDays = 3,
            WeeklyBudget = 70m,
            HouseholdSize = 2,
            IncludeSpecialTreatMeal = true
        });

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Fast special-treat plan took {stopwatch.Elapsed}.");
        Assert.Equal(0, handler.CallCount);
        Assert.True(result.IncludeSpecialTreatMeal);
        Assert.Contains(result.MealPlan, meal => meal.IsSpecialTreat);
    }

    [Fact]
    public async Task BuildPlanAsync_WithInteractiveAiEnabled_DoesNotBlockOnSupermarketLayoutResearch()
    {
        ClearAiPool();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["AislePilot:EnableAiGeneration"] = "true",
                ["AislePilot:EnableInteractiveAiGeneration"] = "true",
                ["AislePilot:AllowTemplateFallback"] = "true"
            })
            .Build();
        using var handler = new CapturingEmptyResponseHandler();
        using var httpClient = new HttpClient(handler);
        var service = new AislePilotService(httpClient, configuration);

        var result = await service.BuildPlanAsync(new AislePilotRequestModel
        {
            Supermarket = "Tesco",
            DietaryModes = ["Balanced"],
            CookDays = 1,
            PlanDays = 1,
            MealsPerDay = 1,
            SelectedMealTypes = ["Dinner"],
            WeeklyBudget = 35m,
            HouseholdSize = 2
        });

        Assert.NotEmpty(result.MealPlan);
        Assert.NotEmpty(handler.RequestBodies);
        Assert.All(handler.RequestBodies, body =>
            Assert.DoesNotContain("web_search", body, StringComparison.Ordinal));
    }

    private sealed class SlowResponseHandler : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class CapturingEmptyResponseHandler : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
