using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Theory]
    [InlineData("/projects/aisle-pilot/export/plan-pack", "Plan-pack export hit a temporary issue. Please retry.")]
    [InlineData("/projects/aisle-pilot/export/checklist", "Checklist export hit a temporary issue. Please retry.")]
    public async Task Export_WhenFormatterFails_ReturnsControlledProblemResponse(
        string exportPath,
        string expectedDetail)
    {
        using var failureFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAislePilotExportService>();
                services.AddSingleton<IAislePilotExportService, ThrowingAislePilotExportService>();
            });
        });
        using var client = failureFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");

        using var response = await client.PostAsync(exportPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Request.Supermarket"] = "Tesco",
            ["Request.WeeklyBudget"] = "65",
            ["Request.HouseholdSize"] = "2",
            ["Request.PlanDays"] = "1",
            ["Request.CookDays"] = "1",
            ["Request.MealsPerDay"] = "1",
            ["Request.SelectedMealTypes"] = "Dinner",
            ["Request.CustomAisleOrder"] = string.Empty,
            ["Request.DislikesOrAllergens"] = string.Empty,
            ["Request.PreferQuickMeals"] = "true",
            ["Request.DietaryModes"] = "Balanced",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Export failed", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedDetail, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Simulated formatter failure", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingAislePilotExportService : IAislePilotExportService
    {
        public byte[] BuildPlanPackPdf(
            AislePilotRequestModel request,
            AislePilotPlanResultViewModel result,
            bool useDarkTheme)
        {
            throw new IOException("Simulated formatter failure.");
        }

        public string BuildChecklistText(AislePilotPlanResultViewModel result)
        {
            throw new IOException("Simulated formatter failure.");
        }
    }
}
