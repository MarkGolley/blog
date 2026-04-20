using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBlog.Models;
using MyBlog.Utilities;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private AislePilotPlanResultViewModel BuildLowestCostRebalancePlan(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays)
    {
        EnsureAiMealPoolHydrated();

        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);

        var combinedSource = pooledMeals
            .Concat(templateMeals)
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
            .ToList();
        if (combinedSource.Count == 0)
        {
            throw new InvalidOperationException(
                "No meals match the selected dietary modes and dislikes/allergens.");
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        var selectedMeals = SelectLowestCostMeals(
            combinedSource,
            context.HouseholdFactor,
            context.PriceProfile.RelativeCostFactor,
            request.PreferQuickMeals,
            IsHighProteinPreferred(context.DietaryModes),
            totalMealCount,
            mealTypeSlots,
            request.IncludeSpecialTreatMeal,
            request.SelectedSpecialTreatCookDayIndex,
            savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
            enableSavedMealRepeats: request.EnableSavedMealRepeats,
            savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);

        return BuildPlanFromMeals(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: pooledMeals.Count > 0,
            planSourceLabel: "Budget floor");
    }

    internal async Task<AislePilotPlanResultViewModel> BuildLowestCostRebalancePlanAsync(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        CancellationToken cancellationToken = default)
    {
        await EnsureAiMealPoolHydratedAsync(cancellationToken);

        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);

        var combinedSource = pooledMeals
            .Concat(templateMeals)
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
            .ToList();
        if (combinedSource.Count == 0)
        {
            throw new InvalidOperationException(
                "No meals match the selected dietary modes and dislikes/allergens.");
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        var selectedMeals = SelectLowestCostMeals(
            combinedSource,
            context.HouseholdFactor,
            context.PriceProfile.RelativeCostFactor,
            request.PreferQuickMeals,
            IsHighProteinPreferred(context.DietaryModes),
            totalMealCount,
            mealTypeSlots,
            request.IncludeSpecialTreatMeal,
            request.SelectedSpecialTreatCookDayIndex,
            savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
            enableSavedMealRepeats: request.EnableSavedMealRepeats,
            savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);

        return await BuildPlanFromMealsAsync(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: pooledMeals.Count > 0,
            planSourceLabel: "Budget floor",
            cancellationToken: cancellationToken);
    }

    private static IReadOnlyList<MealTemplate> SelectLowestCostMeals(
        IReadOnlyList<MealTemplate> mealSource,
        decimal householdFactor,
        decimal priceFactor,
        bool preferQuickMeals,
        bool preferHighProtein,
        int requestedMealCount,
        IReadOnlyList<string> mealTypeSlots,
        bool includeSpecialTreatMeal,
        int? selectedSpecialTreatCookDayIndex,
        IReadOnlySet<string>? savedEnjoyedMealNames = null,
        bool enableSavedMealRepeats = false,
        int savedMealRepeatRatePercent = DefaultSavedMealRepeatRatePercent)
    {
        if (mealSource.Count == 0)
        {
            throw new InvalidOperationException(
                "No meals match the selected dietary modes and dislikes/allergens.");
        }

        var normalizedMealCount = NormalizeRequestedMealCount(requestedMealCount);
        var orderedCandidates = mealSource
            .Select(meal => EnsureMealTypeSuitability(meal))
            .OrderBy(meal => CalculateScaledMealCost(meal, householdFactor, dayMultiplier: 1, priceFactor: priceFactor))
            .ThenBy(meal =>
                preferHighProtein &&
                !meal.Tags.Contains("High-Protein", StringComparer.OrdinalIgnoreCase)
                    ? 1
                    : 0)
            .ThenBy(meal => preferQuickMeals && !meal.IsQuick ? 1 : 0)
            .ThenBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = new List<MealTemplate>(normalizedMealCount);
        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        var normalizedSavedMealNames = savedEnjoyedMealNames is null || savedEnjoyedMealNames.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(savedEnjoyedMealNames, StringComparer.OrdinalIgnoreCase);
        var normalizedSavedMealRepeatRatePercent = Math.Clamp(savedMealRepeatRatePercent, 10, 100);
        var rotationSeed = Math.Abs((long)normalizedMealCount + 211L);
        for (var i = 0; i < normalizedMealCount; i++)
        {
            var slotMealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
            var slotCompatibleCandidates = orderedCandidates
                .Where(meal => SupportsMealType(meal, slotMealType))
                .ToList();
            if (slotCompatibleCandidates.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No {slotMealType.ToLowerInvariant()} meals match the selected dietary modes and dislikes/allergens.");
            }

            var shouldPreferSavedMealsForSlot =
                enableSavedMealRepeats &&
                normalizedSavedMealNames.Count > 0 &&
                slotCompatibleCandidates.Any(meal => normalizedSavedMealNames.Contains(meal.Name)) &&
                ShouldPreferSavedMealForSlot(
                    rotationSeed,
                    i,
                    normalizedSavedMealRepeatRatePercent);
            var candidate = SelectCandidateForSlot(
                slotCompatibleCandidates,
                selected,
                resolvedMealTypeSlots,
                i,
                normalizedMealCount,
                preferredMealNames: normalizedSavedMealNames,
                shouldPreferPreferredMeals: shouldPreferSavedMealsForSlot);
            selected.Add(candidate);
        }

        if (includeSpecialTreatMeal)
        {
            var treatApplied = TryApplySpecialTreatMeal(
                selected,
                orderedCandidates,
                resolvedMealTypeSlots,
                householdFactor,
                priceFactor,
                selectedSpecialTreatCookDayIndex);
            if (!treatApplied || !HasSpecialTreatDinner(selected, resolvedMealTypeSlots))
            {
                throw new InvalidOperationException(
                    "No indulgent special treat dinner was available for the current settings.");
            }
        }

        return selected;
    }

    internal AislePilotPlanResultViewModel ApplyBudgetRebalanceStatus(
        AislePilotPlanResultViewModel result,
        AislePilotPlanResultViewModel baselinePlan,
        IReadOnlyList<string> baselineMealNames)
    {
        var ukCulture = CultureInfo.GetCultureInfo("en-GB");
        var costDrop = decimal.Round(
            baselinePlan.EstimatedTotalCost - result.EstimatedTotalCost,
            2,
            MidpointRounding.AwayFromZero);
        var hasMealChanges = !_planComparisonService.HasSameMealSequence(result, baselineMealNames);
        var changedMealCount = _planComparisonService.CountChangedMealDays(result, baselineMealNames);
        var usedTargetedTrim = result.PlanSourceLabel.Contains("Budget trim swaps", StringComparison.OrdinalIgnoreCase);

        result.BudgetRebalanceAttempted = true;
        result.BudgetRebalanceReducedCost = costDrop > 0m;

        if (!result.IsOverBudget)
        {
            result.BudgetRebalanceStatusMessage = costDrop > 0m && usedTargetedTrim
                ? $"Swapped {changedMealCount} higher-cost meal(s) for lower-cost options. Estimated spend reduced by {costDrop.ToString("C", ukCulture)}."
                : costDrop > 0m
                    ? $"Lower-cost mix found. Estimated spend reduced by {costDrop.ToString("C", ukCulture)}."
                : "Plan already sits within your budget.";
            return result;
        }

        if (costDrop > 0m)
        {
            result.BudgetRebalanceStatusMessage = usedTargetedTrim
                ? $"Swapped {changedMealCount} higher-cost meal(s) for lower-cost options and reduced spend by {costDrop.ToString("C", ukCulture)}, but this plan is still {Math.Abs(result.BudgetDelta).ToString("C", ukCulture)} over budget."
                : hasMealChanges
                    ? $"Lowest-cost compatible mix found right now. Estimated spend reduced by {costDrop.ToString("C", ukCulture)}, but this plan is still {Math.Abs(result.BudgetDelta).ToString("C", ukCulture)} over budget."
                : $"Estimated spend reduced by {costDrop.ToString("C", ukCulture)}, but this plan is still {Math.Abs(result.BudgetDelta).ToString("C", ukCulture)} over budget.";
            return result;
        }

        result.BudgetRebalanceStatusMessage =
            "Sorry, we do not currently have compatible recipes that come in cheaper than this right now.";
        return result;
    }

    internal static AislePilotPlanResultViewModel RebasePlanToOriginalBudget(
        AislePilotPlanResultViewModel plan,
        decimal originalBudget)
    {
        var budgetDelta = decimal.Round(originalBudget - plan.EstimatedTotalCost, 2, MidpointRounding.AwayFromZero);
        var isOverBudget = budgetDelta < 0;
        var sourceLabel = string.IsNullOrWhiteSpace(plan.PlanSourceLabel)
            ? "Budget rebalance"
            : $"Budget rebalance ({plan.PlanSourceLabel})";

        plan.WeeklyBudget = originalBudget;
        plan.BudgetDelta = budgetDelta;
        plan.IsOverBudget = isOverBudget;
        plan.BudgetTips = BuildBudgetTips(isOverBudget, budgetDelta, plan.LeftoverDays);
        plan.PlanSourceLabel = sourceLabel;
        return plan;
    }

}
