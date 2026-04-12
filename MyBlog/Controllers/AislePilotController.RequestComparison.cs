using MyBlog.Models;

namespace MyBlog.Controllers;

public partial class AislePilotController
{
    private static bool ShouldRecalculateCurrentPlan(
        AislePilotRequestModel previousRequest,
        AislePilotRequestModel nextRequest)
    {
        if (!HasSameMealCompatibilitySettings(previousRequest, nextRequest))
        {
            return false;
        }

        return previousRequest.HouseholdSize != nextRequest.HouseholdSize ||
               previousRequest.WeeklyBudget != nextRequest.WeeklyBudget ||
               !string.Equals(
                   previousRequest.PortionSize,
                   nextRequest.PortionSize,
                   StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(
                   previousRequest.LeftoverCookDayIndexesCsv,
                   nextRequest.LeftoverCookDayIndexesCsv,
                   StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(
                   previousRequest.IgnoredMealSlotIndexesCsv,
                   nextRequest.IgnoredMealSlotIndexesCsv,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSameMealCompatibilitySettings(
        AislePilotRequestModel previousRequest,
        AislePilotRequestModel nextRequest)
    {
        return string.Equals(previousRequest.Supermarket, nextRequest.Supermarket, StringComparison.OrdinalIgnoreCase) &&
               previousRequest.PlanDays == nextRequest.PlanDays &&
               previousRequest.MealsPerDay == nextRequest.MealsPerDay &&
               AreEquivalentSelections(previousRequest.SelectedMealTypes, nextRequest.SelectedMealTypes) &&
               AreEquivalentSelections(previousRequest.DietaryModes, nextRequest.DietaryModes) &&
               string.Equals(
                   previousRequest.DislikesOrAllergens,
                   nextRequest.DislikesOrAllergens,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   previousRequest.CustomAisleOrder,
                   nextRequest.CustomAisleOrder,
                   StringComparison.OrdinalIgnoreCase) &&
               previousRequest.PreferQuickMeals == nextRequest.PreferQuickMeals &&
               previousRequest.IncludeSpecialTreatMeal == nextRequest.IncludeSpecialTreatMeal &&
               previousRequest.SelectedSpecialTreatCookDayIndex == nextRequest.SelectedSpecialTreatCookDayIndex &&
               previousRequest.IncludeDessertAddOn == nextRequest.IncludeDessertAddOn &&
               string.Equals(
                   previousRequest.SelectedDessertAddOnName,
                   nextRequest.SelectedDessertAddOnName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreEquivalentSelections(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right)
    {
        var leftValues = (left ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rightValues = (right ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return leftValues.Count == rightValues.Count &&
               leftValues.All(value => rightValues.Contains(value, StringComparer.OrdinalIgnoreCase));
    }
}
