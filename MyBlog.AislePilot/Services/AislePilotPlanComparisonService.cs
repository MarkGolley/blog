using MyBlog.Models;

namespace MyBlog.Services;

public sealed class AislePilotPlanComparisonService : IAislePilotPlanComparisonService
{
    public bool HasSameMealSequence(
        AislePilotPlanResultViewModel plan,
        IReadOnlyList<string> expectedMealNames)
    {
        if (plan.MealPlan.Count != expectedMealNames.Count)
        {
            return false;
        }

        for (var i = 0; i < expectedMealNames.Count; i++)
        {
            if (!plan.MealPlan[i].MealName.Equals(expectedMealNames[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public int CountChangedMealDays(
        AislePilotPlanResultViewModel plan,
        IReadOnlyList<string> baselineMealNames)
    {
        var comparedCount = Math.Min(plan.MealPlan.Count, baselineMealNames.Count);
        var changedCount = 0;
        for (var i = 0; i < comparedCount; i++)
        {
            if (!plan.MealPlan[i].MealName.Equals(baselineMealNames[i], StringComparison.OrdinalIgnoreCase))
            {
                changedCount++;
            }
        }

        return Math.Max(1, changedCount);
    }
}
