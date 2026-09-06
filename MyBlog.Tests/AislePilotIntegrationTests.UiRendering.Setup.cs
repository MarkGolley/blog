using System.Text.RegularExpressions;

namespace MyBlog.Tests;

public partial class AislePilotIntegrationTests
{
    [Fact]
    public async Task Index_Get_PresentsFourCorePlannerChoicesBeforeOptionalPersonalisation()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("Choose four essentials", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("How many people?", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex(@"data-plan-basic-item=""meal-types""[^>]*open", RegexOptions.IgnoreCase), html);
        Assert.Matches(new Regex(@"data-plan-basic-item=""plan-days""[^>]*open", RegexOptions.IgnoreCase), html);
        Assert.Matches(new Regex(@"data-plan-basic-item=""budget""[^>]*open", RegexOptions.IgnoreCase), html);
        Assert.Contains("aislepilot-personalise", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Diet, portions and preferences", html, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            html.IndexOf("How many people?", StringComparison.OrdinalIgnoreCase) <
            html.IndexOf("Diet, portions and preferences", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Index_Get_UsesSafeDefaultsForOptionalPlannerChoices()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Matches(new Regex(@"name=""Request\.Supermarket""[^>]*value=""Tesco""[^>]*checked", RegexOptions.IgnoreCase), html);
        Assert.Matches(new Regex(@"name=""Request\.PortionSize""[^>]*value=""Medium""", RegexOptions.IgnoreCase), html);
        Assert.Matches(new Regex(@"name=""Request\.MealsPerDay""[^>]*value=""3""|value=""3""[^>]*name=""Request\.MealsPerDay""", RegexOptions.IgnoreCase), html);
        Assert.Matches(new Regex(@"name=""Request\.PreferQuickMeals""[^>]*checked", RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public async Task Index_Get_GroupsWeeklyOptionsAndPlacesOutcomeImmediatelyBeforePrimaryAction()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");

        Assert.Contains("data-planner-personalise", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Personalise your weekly plan", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Store, layout and extras", html, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Regex.Matches(html, @"data-setup-mode-submit=""planner""", RegexOptions.IgnoreCase).Cast<Match>());
        Assert.Single(Regex.Matches(html, @"data-setup-mode-submit=""generator""", RegexOptions.IgnoreCase).Cast<Match>());
        Assert.Matches(
            new Regex(@"class=""aislepilot-outcome-summary""[^>]*>[\s\S]*?Review and generate[\s\S]*?</div>\s*<button[^>]*data-setup-mode-submit=""planner""", RegexOptions.IgnoreCase),
            html);
        Assert.Matches(
            new Regex(@"class=""aislepilot-outcome-summary""[^>]*>[\s\S]*?Three practical meal ideas[\s\S]*?</div>\s*<button[\s\S]*?data-setup-mode-submit=""generator""", RegexOptions.IgnoreCase),
            html);
        Assert.Equal(2, Regex.Matches(html, @">Review and generate<", RegexOptions.IgnoreCase).Count);
        Assert.Contains("data-household-count", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-plan-basic-mirror=\"meal-types\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aiming for your", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Your selections", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-plan-basic-mirror=\"budget\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-pantry-summary", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_Get_UsesTaskBasedModeLanguageAndExplicitStartingDefaults()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");
        var modeScript = File.ReadAllText(Path.Combine(RepoRoot, "MyBlog", "wwwroot", "js", "aisle-pilot", "setup-mode.js"));

        Assert.Contains("Plan my week", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Use my ingredients", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Starts with 7 days, 3 meals a day, Tesco and a £65 budget", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Starts flexible", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Meal planner", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Generator snapshot", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("storedMode ?? visibleMode ?? defaultMode", modeScript, StringComparison.Ordinal);
        Assert.Contains("forceDefault", modeScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_Get_UsesPlainSetupLabelsAndBlurOnlyClientValidation()
    {
        using var client = CreateClient(allowAutoRedirect: true);

        var html = await client.GetStringAsync("/projects/aisle-pilot");
        var coreScript = File.ReadAllText(Path.Combine(RepoRoot, "MyBlog", "wwwroot", "js", "aisle-pilot", "core.js"));

        Assert.Contains("Number of days", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("How often to reuse saved meals", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Add a treat dinner on", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Repeat strength", html, StringComparison.OrdinalIgnoreCase);
        var optionalExclusionsInput = Regex.Match(html, @"<input[^>]*name=""Request\.DislikesOrAllergens""[^>]*>", RegexOptions.IgnoreCase).Value;
        Assert.False(string.IsNullOrWhiteSpace(optionalExclusionsInput));
        Assert.DoesNotContain("data-val-required", optionalExclusionsInput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("addEventListener(\"blur\"", coreScript, StringComparison.Ordinal);
        Assert.Contains("validator.settings.onkeyup = false", coreScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Index_Post_InvalidCoreValues_RendersLinkedSummaryFocusAndPreservedValues()
    {
        using var client = CreateClient(allowAutoRedirect: true);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/projects/aisle-pilot");
        var form = new List<KeyValuePair<string, string>>
        {
            new("Request.Supermarket", "Tesco"),
            new("Request.WeeklyBudget", "10"),
            new("Request.HouseholdSize", "9"),
            new("Request.PlanDays", "7"),
            new("Request.CookDays", "7"),
            new("Request.MealsPerDay", "3"),
            new("Request.SelectedMealTypes", "Breakfast"),
            new("Request.SelectedMealTypes", "Lunch"),
            new("Request.SelectedMealTypes", "Dinner"),
            new("Request.PortionSize", "Medium"),
            new("Request.DietaryModes", "Balanced"),
            new("Request.PreferQuickMeals", "true"),
            new("__RequestVerificationToken", antiForgeryToken)
        };

        using var response = await client.PostAsync("/projects/aisle-pilot", new FormUrlEncodedContent(form));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("data-validation-summary", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"#Request_WeeklyBudget\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"#aislepilot-household-size\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Weekly budget:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("People:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex(@"id=""Request_WeeklyBudget""[^>]*value=""10""|value=""10""[^>]*id=""Request_WeeklyBudget""", RegexOptions.IgnoreCase), html);
        Assert.Matches(new Regex(@"id=""aislepilot-household-size""[^>]*value=""9""|value=""9""[^>]*id=""aislepilot-household-size""", RegexOptions.IgnoreCase), html);
        Assert.Matches(new Regex(@"<(?:input|textarea)[^>]+autofocus", RegexOptions.IgnoreCase), html);
    }

    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
