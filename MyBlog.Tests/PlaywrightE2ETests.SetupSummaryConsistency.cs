using Microsoft.Playwright;

namespace MyBlog.Tests;

public sealed partial class PlaywrightE2ETests
{
    [Fact]
    public async Task Mobile_AislePilotSetupSummariesTrackTheSubmittedPlannerValues()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotSetupAsync(page);

        var form = page.Locator("#aislepilot-setup-form");
        var budget = form.Locator("details[data-plan-basic-item='budget'] input[name='Request.WeeklyBudget']");
        await AssertAllSummaryValuesAsync(form, "budget", $"£{await budget.InputValueAsync()}");

        await form.Locator("details[data-plan-basic-item='budget'], details[data-plan-basic-item='plan-days']")
            .EvaluateAllAsync<int>("details => { details.forEach(item => item.open = true); return details.length; }");
        await budget.EvaluateAsync(
            "input => { input.value = '110'; input.dispatchEvent(new Event('input', { bubbles: true })); input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await form.Locator("input[name='Request.HouseholdSize']").FillAsync("3");
        await form.Locator("details[data-plan-basic-item='plan-days'] input[name='Request.PlanDays']").EvaluateAsync(
            "input => { input.value = '5'; input.dispatchEvent(new Event('input', { bubbles: true })); input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await form.Locator("input[name='Request.SelectedMealTypes'][value='Breakfast']").EvaluateAsync(
            "input => { input.checked = false; input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await form.Locator("input[name='Request.Supermarket'][value='Aldi']:not(:disabled)").EvaluateAsync(
            "input => { input.checked = true; input.dispatchEvent(new Event('change', { bubbles: true })); }");

        var normalizedBudget = await budget.InputValueAsync();
        await AssertAllSummaryValuesAsync(form, "budget", $"£{normalizedBudget}");
        await AssertAllSummaryValuesAsync(form, "serving", "3 people - Medium portions");
        await AssertAllSummaryValuesAsync(form, "store", "Aldi");
        await AssertAllSummaryValuesAsync(form, "meal-types", "Lunch, Dinner");
        await AssertAllSummaryValuesAsync(form, "plan-days", "5 days");
        Assert.Equal("Complete plan for 5 days", await form.Locator("#aislepilot-planner-outcome-title").InnerTextAsync());
        var outcomeCopy = await form.Locator("#aislepilot-planner-outcome-title + p").InnerTextAsync();
        Assert.Contains("Lunch, Dinner for 3 people", outcomeCopy, StringComparison.Ordinal);
        Assert.Contains("Aldi-ordered", outcomeCopy, StringComparison.Ordinal);
        Assert.Contains($"aiming for your £{normalizedBudget} budget", outcomeCopy, StringComparison.Ordinal);

        await form.Locator("[data-setup-mode-toggle='generator']").ClickAsync();
        await form.Locator("textarea[name='Request.PantryItems']").FillAsync("rice, spinach, eggs");
        await form.Locator("input[type='checkbox'][name='Request.RequireCorePantryIngredients']").EvaluateAsync(
            "input => { input.checked = true; input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await form.Locator("input[type='checkbox'][name='Request.PreferQuickMeals']").EvaluateAsync(
            "input => { input.checked = false; input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await AssertAllSummaryValuesAsync(form, "pantry", "rice, spinach, eggs");
        await AssertAllSummaryValuesAsync(form, "generator-core", "Use every listed ingredient");
        await AssertAllSummaryValuesAsync(form, "quick-meals", "Quick meals off");

        await form.Locator("[data-setup-mode-toggle='planner']").ClickAsync();
        await AssertAllSummaryValuesAsync(form, "budget", $"£{normalizedBudget}");
        await WriteAislePilotStateScreenshotAsync(page, "setup-summary-consistency-mobile");
        await page.SetViewportSizeAsync(1440, 900);
        await WriteAislePilotStateScreenshotAsync(page, "setup-summary-consistency-desktop");

        await page.EvaluateAsync(
            """
            () => {
                window.__aislePilotTestSubmissions = [];
                const form = document.querySelector('#aislepilot-setup-form');
                form?.addEventListener('submit', event => {
                    event.preventDefault();
                    const data = new FormData(form);
                    window.__aislePilotTestSubmissions.push({
                        budget: data.get('Request.WeeklyBudget'),
                        people: data.get('Request.HouseholdSize'),
                        days: data.get('Request.PlanDays'),
                        store: data.get('Request.Supermarket')
                    });
                });
            }
            """);
        await form.Locator("[data-setup-mode-submit='planner']").ClickAsync();
        var submissions = await page.EvaluateAsync<PlannerSubmission[]>("() => window.__aislePilotTestSubmissions");
        var submission = Assert.Single(submissions);
        Assert.Equal(normalizedBudget, submission.Budget);
        Assert.Equal("3", submission.People);
        Assert.Equal("5", submission.Days);
        Assert.Equal("Aldi", submission.Store);
    }

    [Fact]
    public async Task Mobile_AislePilotSetupSummariesResynchroniseAfterServerValidation()
    {
        if (!IsE2EEnabled())
        {
            return;
        }

        await using var context = await CreateMobileContextAsync();
        var page = await context.NewPageAsync();
        await GoToAislePilotSetupAsync(page);

        var mealTypes = page.Locator("input[name='Request.SelectedMealTypes']:checked");
        while (await mealTypes.CountAsync() > 0)
        {
            await mealTypes.First.UncheckAsync(new LocatorUncheckOptions { Force = true });
        }

        await page.Locator("[data-mobile-setup-submit='planner']").ClickAsync();
        await page.Locator("[data-validation-summary]").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
        Assert.Single(await page.Locator("[data-validation-summary] a[href='#aislepilot-meal-types']").AllAsync());

        var form = page.Locator("#aislepilot-setup-form");
        var budget = await form.Locator("input[name='Request.WeeklyBudget']").First.InputValueAsync();
        var selectedMealTypes = await form.Locator("input[name='Request.SelectedMealTypes']:checked").EvaluateAllAsync<string[]>(
            "inputs => inputs.map(input => input.value).filter(Boolean)");
        await AssertAllSummaryValuesAsync(form, "budget", $"£{budget}");
        await AssertAllSummaryValuesAsync(form, "meal-types", selectedMealTypes.Length > 0 ? string.Join(", ", selectedMealTypes) : "None selected");
        Assert.Contains($"aiming for your £{budget} budget", await form.Locator("#aislepilot-planner-outcome-title + p").InnerTextAsync(), StringComparison.Ordinal);
    }

    private static async Task AssertAllSummaryValuesAsync(ILocator form, string key, string expected)
    {
        var selector = key switch
        {
            "serving" => "[data-serving-summary]",
            "dietary" => "[data-dietary-summary]",
            "extras" => "[data-special-options-summary]",
            "pantry" => "[data-pantry-summary]",
            "generator-core" => "[data-generator-core-summary]",
            "quick-meals" => "[data-quick-meals-summary]",
            "store" => "[data-plan-basic-mirror='supermarket']",
            _ => $"[data-plan-basic-mirror='{key}']"
        };
        var outputs = form.Locator(selector);
        Assert.True(await outputs.CountAsync() > 0, $"Expected at least one {key} setup summary.");
        foreach (var output in await outputs.AllAsync())
        {
            Assert.Equal(expected, (await output.InnerTextAsync()).Trim());
        }
    }

    private sealed class PlannerSubmission
    {
        public string? Budget { get; init; }
        public string? People { get; init; }
        public string? Days { get; init; }
        public string? Store { get; init; }
    }
}
