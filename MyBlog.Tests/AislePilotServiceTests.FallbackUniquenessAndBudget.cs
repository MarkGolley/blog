using MyBlog.Models;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    [Fact]
    public void TemplateFallback_SupportedDietaryCombinations_RespectWeeklyVarietyRules()
    {
        ClearAiPool();
        var failures = new List<string>();

        foreach (var dietaryModes in SupportedDietaryModeCombinations)
        {
            var result = _service.BuildPlan(CreateWeeklyFallbackMatrixRequest(dietaryModes, weeklyBudget: 80m));

            Assert.Equal("AislePilot recipe plan", result.PlanSourceLabel);

            foreach (var mealSlot in RequiredMealSlots)
            {
                var slotMeals = result.MealPlan
                    .Where(meal => meal.MealType.Equals(mealSlot, StringComparison.OrdinalIgnoreCase))
                    .Select(meal => meal.MealName)
                    .ToList();
                var availableTemplateCount = GetCompatibleTemplateMealNamesForSlot(dietaryModes, null, mealSlot).Count;
                var expectedMaximumRepeat = mealSlot.Equals("Dinner", StringComparison.OrdinalIgnoreCase)
                    ? Math.Max(1, (int)Math.Ceiling(slotMeals.Count / (decimal)availableTemplateCount))
                    : availableTemplateCount >= 4
                        ? 2
                        : Math.Max(1, (int)Math.Ceiling(slotMeals.Count / (decimal)availableTemplateCount));
                var actualMaximumRepeat = slotMeals
                    .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Count())
                    .DefaultIfEmpty(0)
                    .Max();

                if (slotMeals.Count != 7 || actualMaximumRepeat > expectedMaximumRepeat)
                {
                    failures.Add(
                        $"{string.Join(" + ", dietaryModes)} / {mealSlot}: " +
                        $"slots={slotMeals.Count}, available={availableTemplateCount}, " +
                        $"maximum repeat={actualMaximumRepeat}, expected <= {expectedMaximumRepeat}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TemplateFallback_SupportedDietaryCombinations_PreserveBudgetAccountingAndNeverRebalanceUpward()
    {
        ClearAiPool();
        var failures = new List<string>();

        foreach (var dietaryModes in SupportedDietaryModeCombinations)
        {
            var request = CreateWeeklyFallbackMatrixRequest(dietaryModes, weeklyBudget: 30m);
            request.HouseholdSize = 4;
            var baseline = _service.BuildPlan(request);
            var rebalanced = _service.BuildPlanWithBudgetRebalance(
                request,
                currentPlanMealNames: baseline.MealPlan.Select(meal => meal.MealName).ToList());
            var expectedDelta = decimal.Round(
                request.WeeklyBudget - rebalanced.EstimatedTotalCost,
                2,
                MidpointRounding.AwayFromZero);

            if (!baseline.PlanSourceLabel.Equals("AislePilot recipe plan", StringComparison.Ordinal) ||
                rebalanced.WeeklyBudget != request.WeeklyBudget ||
                rebalanced.EstimatedTotalCost > baseline.EstimatedTotalCost ||
                rebalanced.BudgetDelta != expectedDelta ||
                rebalanced.IsOverBudget != (expectedDelta < 0) ||
                !rebalanced.BudgetRebalanceAttempted ||
                string.IsNullOrWhiteSpace(rebalanced.BudgetRebalanceStatusMessage))
            {
                failures.Add(
                    $"{string.Join(" + ", dietaryModes)}: budget={rebalanced.WeeklyBudget:0.00}, " +
                    $"baseline={baseline.EstimatedTotalCost:0.00}, rebalanced={rebalanced.EstimatedTotalCost:0.00}, " +
                    $"delta={rebalanced.BudgetDelta:0.00}, over={rebalanced.IsOverBudget}, " +
                    $"attempted={rebalanced.BudgetRebalanceAttempted}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static AislePilotRequestModel CreateWeeklyFallbackMatrixRequest(
        IReadOnlyList<string> dietaryModes,
        decimal weeklyBudget)
    {
        return new AislePilotRequestModel
        {
            DietaryModes = dietaryModes.ToList(),
            WeeklyBudget = weeklyBudget,
            HouseholdSize = 2,
            PlanDays = 7,
            CookDays = 7,
            MealsPerDay = 3,
            SelectedMealTypes = RequiredMealSlots.ToList()
        };
    }
}
