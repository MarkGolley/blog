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
    internal static IReadOnlyList<decimal> BuildBudgetRebalanceTargets(
        decimal originalBudget,
        decimal baselineEstimatedTotal,
        int maxTargets)
    {
        if (maxTargets <= 0 || originalBudget <= 15m)
        {
            return [];
        }

        var overspend = Math.Max(0m, baselineEstimatedTotal - originalBudget);
        var rawTargets = new[]
        {
            originalBudget - Math.Max(overspend + 1m, originalBudget * 0.08m),
            originalBudget * 0.92m,
            originalBudget * 0.88m,
            originalBudget * 0.84m,
            originalBudget * 0.80m
        };

        var maxTargetBudget = decimal.Round(Math.Max(15m, originalBudget - 1m), 2, MidpointRounding.AwayFromZero);
        var dedupedTargets = new List<decimal>(rawTargets.Length);
        foreach (var rawTarget in rawTargets)
        {
            var normalized = decimal.Round(rawTarget, 2, MidpointRounding.AwayFromZero);
            if (normalized < 15m)
            {
                normalized = 15m;
            }

            if (normalized > maxTargetBudget)
            {
                normalized = maxTargetBudget;
            }

            if (normalized >= originalBudget)
            {
                continue;
            }

            if (!dedupedTargets.Contains(normalized))
            {
                dedupedTargets.Add(normalized);
            }
        }

        return dedupedTargets
            .Take(maxTargets)
            .ToList();
    }

    private AislePilotPlanResultViewModel? TryBuildTargetedLowerCostPlan(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> baselineMeals,
        int cookDays)
    {
        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        if (baselineMeals.Count != totalMealCount)
        {
            return null;
        }

        EnsureAiMealPoolHydrated();
        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);
        var compatiblePool = pooledMeals
            .Concat(templateMeals)
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
            .ToList();
        if (compatiblePool.Count == 0)
        {
            return null;
        }

        var selectedMeals = baselineMeals.ToList();
        var planDays = NormalizePlanDays(request.PlanDays);
        var leftoverDays = Math.Max(0, planDays - cookDays);
        var requestedLeftoverSourceDays = ParseRequestedLeftoverSourceDays(
            request.LeftoverCookDayIndexesCsv,
            cookDays,
            leftoverDays,
            planDays);
        var dayMultipliers = BuildMealPortionMultipliers(
            cookDays,
            leftoverDays,
            requestedLeftoverSourceDays,
            planDays);
        var mealMultipliers = BuildPerMealPortionMultipliers(dayMultipliers, mealsPerDay);

        var currentTotal = CalculatePlanCost(selectedMeals, context.HouseholdFactor, mealMultipliers);
        var maxIterations = Math.Max(4, totalMealCount * 2);
        var hasChanges = false;

        for (var iteration = 0; iteration < maxIterations && currentTotal > request.WeeklyBudget; iteration++)
        {
            var orderedDayIndexes = Enumerable.Range(0, totalMealCount)
                .OrderByDescending(index => CalculateScaledMealCost(
                    selectedMeals[index],
                    context.HouseholdFactor,
                    mealMultipliers[index]))
                .ToList();

            var swappedThisIteration = false;
            foreach (var dayIndex in orderedDayIndexes)
            {
                var replacement = SelectLowerCostSwapCandidateForDay(
                    compatiblePool,
                    selectedMeals,
                    dayIndex,
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex],
                    request.PreferQuickMeals,
                    IsHighProteinPreferred(context.DietaryModes),
                    mealTypeSlots);
                if (replacement is null)
                {
                    continue;
                }

                var currentMealCost = CalculateScaledMealCost(
                    selectedMeals[dayIndex],
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex]);
                var replacementMealCost = CalculateScaledMealCost(
                    replacement,
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex]);

                if (replacementMealCost >= currentMealCost)
                {
                    continue;
                }

                selectedMeals[dayIndex] = replacement;
                currentTotal = decimal.Round(
                    currentTotal - currentMealCost + replacementMealCost,
                    2,
                    MidpointRounding.AwayFromZero);
                hasChanges = true;
                swappedThisIteration = true;
                break;
            }

            if (!swappedThisIteration)
            {
                break;
            }
        }

        if (!hasChanges)
        {
            return null;
        }

        if (request.IncludeSpecialTreatMeal && !HasSpecialTreatDinner(selectedMeals, mealTypeSlots))
        {
            return null;
        }

        AddMealsToAiPool(selectedMeals);
        return BuildPlanFromMeals(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: pooledMeals.Count > 0,
            planSourceLabel: "Budget trim swaps");
    }

    internal async Task<AislePilotPlanResultViewModel?> TryBuildTargetedLowerCostPlanAsync(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> baselineMeals,
        int cookDays,
        CancellationToken cancellationToken = default)
    {
        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        if (baselineMeals.Count != totalMealCount)
        {
            return null;
        }

        await EnsureAiMealPoolHydratedAsync(cancellationToken);
        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);
        var compatiblePool = pooledMeals
            .Concat(templateMeals)
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
            .ToList();
        if (compatiblePool.Count == 0)
        {
            return null;
        }

        var selectedMeals = baselineMeals.ToList();
        var planDays = NormalizePlanDays(request.PlanDays);
        var leftoverDays = Math.Max(0, planDays - cookDays);
        var requestedLeftoverSourceDays = ParseRequestedLeftoverSourceDays(
            request.LeftoverCookDayIndexesCsv,
            cookDays,
            leftoverDays,
            planDays);
        var dayMultipliers = BuildMealPortionMultipliers(
            cookDays,
            leftoverDays,
            requestedLeftoverSourceDays,
            planDays);
        var mealMultipliers = BuildPerMealPortionMultipliers(dayMultipliers, mealsPerDay);

        var currentTotal = CalculatePlanCost(selectedMeals, context.HouseholdFactor, mealMultipliers);
        var maxIterations = Math.Max(4, totalMealCount * 2);
        var hasChanges = false;

        for (var iteration = 0; iteration < maxIterations && currentTotal > request.WeeklyBudget; iteration++)
        {
            var orderedDayIndexes = Enumerable.Range(0, totalMealCount)
                .OrderByDescending(index => CalculateScaledMealCost(
                    selectedMeals[index],
                    context.HouseholdFactor,
                    mealMultipliers[index]))
                .ToList();

            var swappedThisIteration = false;
            foreach (var dayIndex in orderedDayIndexes)
            {
                var replacement = SelectLowerCostSwapCandidateForDay(
                    compatiblePool,
                    selectedMeals,
                    dayIndex,
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex],
                    request.PreferQuickMeals,
                    IsHighProteinPreferred(context.DietaryModes),
                    mealTypeSlots);
                if (replacement is null)
                {
                    continue;
                }

                var currentMealCost = CalculateScaledMealCost(
                    selectedMeals[dayIndex],
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex]);
                var replacementMealCost = CalculateScaledMealCost(
                    replacement,
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex]);

                if (replacementMealCost >= currentMealCost)
                {
                    continue;
                }

                selectedMeals[dayIndex] = replacement;
                currentTotal = decimal.Round(
                    currentTotal - currentMealCost + replacementMealCost,
                    2,
                    MidpointRounding.AwayFromZero);
                hasChanges = true;
                swappedThisIteration = true;
                break;
            }

            if (!swappedThisIteration)
            {
                break;
            }
        }

        if (!hasChanges)
        {
            return null;
        }

        if (request.IncludeSpecialTreatMeal && !HasSpecialTreatDinner(selectedMeals, mealTypeSlots))
        {
            return null;
        }

        AddMealsToAiPool(selectedMeals);
        return await BuildPlanFromMealsAsync(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: pooledMeals.Count > 0,
            planSourceLabel: "Budget trim swaps",
            cancellationToken: cancellationToken);
    }

    private static MealTemplate? SelectLowerCostSwapCandidateForDay(
        IReadOnlyList<MealTemplate> compatiblePool,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        decimal householdFactor,
        int dayMultiplier,
        bool preferQuickMeals,
        bool preferHighProtein,
        IReadOnlyList<string> mealTypeSlots)
    {
        if (compatiblePool.Count == 0 || dayIndex < 0 || dayIndex >= selectedMeals.Count)
        {
            return null;
        }

        var currentMeal = selectedMeals[dayIndex];
        var currentMealCost = CalculateScaledMealCost(currentMeal, householdFactor, dayMultiplier);
        var usedNames = selectedMeals
            .Where((_, index) => index != dayIndex)
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slotMealType = mealTypeSlots[dayIndex % mealTypeSlots.Count];
        var normalizedPool = compatiblePool
            .Select(meal => EnsureMealTypeSuitability(meal))
            .ToList();
        var slotCompatiblePool = normalizedPool
            .Where(meal => SupportsMealType(meal, slotMealType))
            .ToList();
        if (slotCompatiblePool.Count == 0)
        {
            return null;
        }

        return slotCompatiblePool
            .Where(meal =>
                !meal.Name.Equals(currentMeal.Name, StringComparison.OrdinalIgnoreCase) &&
                !usedNames.Contains(meal.Name))
            .Select(meal => new
            {
                Meal = meal,
                Cost = CalculateScaledMealCost(meal, householdFactor, dayMultiplier)
            })
            .Where(x => x.Cost < currentMealCost)
            .OrderBy(x => x.Cost)
            .ThenBy(x =>
                preferHighProtein &&
                !x.Meal.Tags.Contains("High-Protein", StringComparer.OrdinalIgnoreCase)
                    ? 1
                    : 0)
            .ThenBy(x => preferQuickMeals && !x.Meal.IsQuick ? 1 : 0)
            .ThenBy(x => x.Meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Meal)
            .FirstOrDefault();
    }

    private static decimal CalculatePlanCost(
        IReadOnlyList<MealTemplate> meals,
        decimal householdFactor,
        IReadOnlyList<int> dayMultipliers)
    {
        if (meals.Count == 0)
        {
            return 0m;
        }

        var normalizedCount = Math.Min(meals.Count, dayMultipliers.Count);
        var total = 0m;
        for (var i = 0; i < normalizedCount; i++)
        {
            total += CalculateScaledMealCost(meals[i], householdFactor, dayMultipliers[i]);
        }

        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateScaledMealCost(
        MealTemplate meal,
        decimal householdFactor,
        int dayMultiplier)
    {
        var normalizedMultiplier = Math.Max(1, dayMultiplier);
        return decimal.Round(
            meal.BaseCostForTwo * householdFactor * normalizedMultiplier,
            4,
            MidpointRounding.AwayFromZero);
    }

}
