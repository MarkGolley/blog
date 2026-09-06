namespace MyBlog.Tests;

public sealed class AislePilotLoadTestConfigurationTests
{
    [Fact]
    public void LoadProfile_ExercisesAFullPlanAndEnforcesLatencyGates()
    {
        var profile = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "aislepilot-load-test.mjs"));

        Assert.Contains("__RequestVerificationToken", profile, StringComparison.Ordinal);
        Assert.Contains("\"x-forwarded-proto\": \"https\"", profile, StringComparison.Ordinal);
        Assert.Contains("Request.PlanDays\": \"7", profile, StringComparison.Ordinal);
        Assert.Contains("Request.MealsPerDay\": \"3", profile, StringComparison.Ordinal);
        Assert.Contains("form.append(\"Request.SelectedMealTypes\", \"Breakfast\")", profile, StringComparison.Ordinal);
        Assert.Contains("form.append(\"Request.SelectedMealTypes\", \"Lunch\")", profile, StringComparison.Ordinal);
        Assert.Contains("form.append(\"Request.SelectedMealTypes\", \"Dinner\")", profile, StringComparison.Ordinal);
        Assert.Contains("AISLEPILOT_LOAD_WARM_P95_MS ?? \"2000\"", profile, StringComparison.Ordinal);
        Assert.Contains("AISLEPILOT_LOAD_COLD_P95_MS ?? \"5000\"", profile, StringComparison.Ordinal);
        Assert.Contains("percentile(coldDurations, 95)", profile, StringComparison.Ordinal);
        Assert.Contains("percentile(warmDurations, 95)", profile, StringComparison.Ordinal);
    }

    [Fact]
    public void Runner_UsesReleaseProductionFastModeWithoutExternalAi()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "run-aislepilot-load-test.ps1"));

        Assert.Contains("ASPNETCORE_ENVIRONMENT = \"Production\"", runner, StringComparison.Ordinal);
        Assert.Contains("AislePilot__EnableAiGeneration = \"false\"", runner, StringComparison.Ordinal);
        Assert.Contains("AislePilot__EnableInteractiveAiGeneration = \"false\"", runner, StringComparison.Ordinal);
        Assert.Contains("DailyCapsule__EnableAiGeneration = \"false\"", runner, StringComparison.Ordinal);
        Assert.Contains("Firestore__AllowInMemoryFallback = \"true\"", runner, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_URLS = $baseUrl", runner, StringComparison.Ordinal);
        Assert.Contains("PORT = $Port.ToString()", runner, StringComparison.Ordinal);
        Assert.Contains("GOOGLE_CLOUD_PROJECT = \" \"", runner, StringComparison.Ordinal);
        Assert.Contains("\"--configuration\", \"Release\"", runner, StringComparison.Ordinal);
        Assert.Contains("aislepilot-load-test.mjs", runner, StringComparison.Ordinal);
    }

    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
