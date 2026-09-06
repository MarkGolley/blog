using MyBlog.Models;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    private static readonly string[][] SupportedDietaryModeCombinations =
    [
        ["Balanced"],
        ["Vegetarian"],
        ["Vegan"],
        ["Pescatarian"],
        ["High-Protein"],
        ["Gluten-Free"],
        ["Balanced", "High-Protein"],
        ["Balanced", "Gluten-Free"],
        ["Vegetarian", "High-Protein"],
        ["Vegetarian", "Gluten-Free"],
        ["Vegan", "High-Protein"],
        ["Vegan", "Gluten-Free"],
        ["Pescatarian", "High-Protein"],
        ["Pescatarian", "Gluten-Free"],
        ["High-Protein", "Gluten-Free"]
    ];

    private static readonly string[] RequiredMealSlots = ["Breakfast", "Lunch", "Dinner"];

    [Fact]
    public void TemplateCatalog_SupportedDietaryCombinations_CoverEveryRequiredMealSlot()
    {
        var missingCoverage = new List<string>();

        foreach (var dietaryModes in SupportedDietaryModeCombinations)
        {
            foreach (var mealSlot in RequiredMealSlots)
            {
                var names = GetCompatibleTemplateMealNamesForSlot(dietaryModes, null, mealSlot);
                if (names.Count == 0)
                {
                    missingCoverage.Add($"{string.Join(" + ", dietaryModes)} / {mealSlot}");
                }
            }
        }

        Assert.True(
            missingCoverage.Count == 0,
            $"Template catalogue has no compatible meal for: {string.Join(", ", missingCoverage)}");
    }

    [Fact]
    public void TemplateFallback_SupportedDietaryCombinations_BuildEveryRequiredMealSlot()
    {
        ClearAiPool();

        foreach (var dietaryModes in SupportedDietaryModeCombinations)
        {
            var request = new AislePilotRequestModel
            {
                DietaryModes = dietaryModes.ToList(),
                WeeklyBudget = 80m,
                HouseholdSize = 2,
                PlanDays = 1,
                CookDays = 1,
                MealsPerDay = 3,
                SelectedMealTypes = RequiredMealSlots.ToList()
            };

            Assert.True(
                _service.HasCompatibleMeals(request),
                $"Compatibility pre-check rejected {string.Join(" + ", dietaryModes)}.");

            var result = _service.BuildPlan(request);

            Assert.Equal("AislePilot recipe plan", result.PlanSourceLabel);
            Assert.Equal(RequiredMealSlots, result.MealPlan.Select(meal => meal.MealType));
        }
    }
}
