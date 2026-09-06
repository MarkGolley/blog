using MyBlog.Models;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    private static void AssertValidCorePlan(
        AislePilotPlanResultViewModel plan,
        int expectedMealCount)
    {
        Assert.Equal(expectedMealCount, plan.MealPlan.Count);
        Assert.All(plan.MealPlan, meal =>
        {
            Assert.False(string.IsNullOrWhiteSpace(meal.MealName));
            Assert.Contains(meal.MealType, RequiredMealSlots);
            Assert.NotEmpty(meal.IngredientLines);
            Assert.NotEmpty(meal.RecipeSteps);
            Assert.False(string.IsNullOrWhiteSpace(meal.MealImageUrl));
            Assert.StartsWith("/images/", meal.MealImageUrl, StringComparison.OrdinalIgnoreCase);
            Assert.True(meal.EstimatedCost > 0m);
        });
        Assert.NotEmpty(plan.ShoppingItems);
        Assert.NotEmpty(plan.AisleOrderUsed);
        Assert.True(plan.EstimatedTotalCost > 0m);

        var expectedBudgetDelta = decimal.Round(
            plan.WeeklyBudget - plan.EstimatedTotalCost,
            2,
            MidpointRounding.AwayFromZero);
        Assert.Equal(expectedBudgetDelta, plan.BudgetDelta);
        Assert.Equal(expectedBudgetDelta < 0m, plan.IsOverBudget);
    }
}
