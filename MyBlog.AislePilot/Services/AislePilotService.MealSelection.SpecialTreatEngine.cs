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
    private static bool IsSpecialTreatMealCandidate(MealTemplate meal)
    {
        if (meal.Tags.Contains("Special Treat", StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return SpecialTreatNameKeywords.Any(keyword =>
            meal.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryApplySpecialTreatMeal(
        IList<MealTemplate> selectedMeals,
        IReadOnlyList<MealTemplate> candidateMeals,
        IReadOnlyList<string> resolvedMealTypeSlots,
        decimal householdFactor,
        int? selectedSpecialTreatCookDayIndex)
    {
        if (selectedMeals.Count == 0 || candidateMeals.Count == 0 || resolvedMealTypeSlots.Count == 0)
        {
            return false;
        }

        var dinnerSlotIndexes = Enumerable.Range(0, selectedMeals.Count)
            .Where(index =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count]
                    .Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dinnerSlotIndexes.Count == 0)
        {
            return false;
        }

        var targetDinnerIndex = ResolveTargetDinnerIndex(
            dinnerSlotIndexes,
            selectedMeals,
            resolvedMealTypeSlots,
            householdFactor,
            selectedSpecialTreatCookDayIndex);
        var currentDinnerMeal = selectedMeals[targetDinnerIndex];
        if (IsSpecialTreatMealCandidate(currentDinnerMeal))
        {
            selectedMeals[targetDinnerIndex] = MarkMealAsSpecialTreat(currentDinnerMeal);
            return true;
        }

        var existingTreatDinnerIndex = dinnerSlotIndexes
            .Where(index => index != targetDinnerIndex && IsSpecialTreatMealCandidate(selectedMeals[index]))
            .Cast<int?>()
            .FirstOrDefault();
        if (existingTreatDinnerIndex.HasValue)
        {
            var sourceTreatDinnerIndex = existingTreatDinnerIndex.Value;
            var targetMeal = selectedMeals[targetDinnerIndex];
            selectedMeals[targetDinnerIndex] = MarkMealAsSpecialTreat(selectedMeals[sourceTreatDinnerIndex]);
            selectedMeals[sourceTreatDinnerIndex] = targetMeal;
            return true;
        }

        var usedMealNames = selectedMeals
            .Where((_, index) => index != targetDinnerIndex)
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dinnerCosts = dinnerSlotIndexes
            .Select(index => CalculateScaledMealCost(selectedMeals[index], householdFactor, dayMultiplier: 1))
            .ToList();
        var averageDinnerCost = dinnerCosts.Count == 0
            ? 0m
            : dinnerCosts.Average();

        var rankedTreatCandidates = candidateMeals
            .Select(meal => EnsureMealTypeSuitability(meal))
            .Where(meal => SupportsMealType(meal, "Dinner"))
            .Where(IsSpecialTreatMealCandidate)
            .Where(meal => !usedMealNames.Contains(meal.Name))
            .Select(meal => new
            {
                Meal = meal,
                Cost = CalculateScaledMealCost(meal, householdFactor, dayMultiplier: 1)
            })
            .OrderByDescending(entry => entry.Cost)
            .ThenBy(entry => entry.Meal.IsQuick ? 1 : 0)
            .ThenBy(entry => entry.Meal.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var treatCandidates = rankedTreatCandidates
            .Where(entry => IsSpecialTreatMealCandidate(entry.Meal))
            .ToList();
        var premiumThreshold = decimal.Round(averageDinnerCost * 1.12m, 2, MidpointRounding.AwayFromZero);
        var replacement = treatCandidates
            .Where(entry => entry.Cost >= premiumThreshold)
            .Select(entry => entry.Meal)
            .FirstOrDefault(meal => !meal.Name.Equals(currentDinnerMeal.Name, StringComparison.OrdinalIgnoreCase));
        replacement ??= treatCandidates
            .Select(entry => entry.Meal)
            .FirstOrDefault(meal => !meal.Name.Equals(currentDinnerMeal.Name, StringComparison.OrdinalIgnoreCase));
        replacement ??= treatCandidates
            .Where(entry => entry.Cost >= premiumThreshold)
            .Select(entry => entry.Meal)
            .FirstOrDefault();
        replacement ??= treatCandidates
            .Select(entry => entry.Meal)
            .FirstOrDefault();

        if (replacement is null)
        {
            return false;
        }

        selectedMeals[targetDinnerIndex] = MarkMealAsSpecialTreat(replacement);
        return true;
    }

    private static int ResolveTargetDinnerIndex(
        IReadOnlyList<int> dinnerSlotIndexes,
        IList<MealTemplate> selectedMeals,
        IReadOnlyList<string> resolvedMealTypeSlots,
        decimal householdFactor,
        int? selectedSpecialTreatCookDayIndex)
    {
        var preferredDinnerIndex = ResolvePreferredSpecialTreatDinnerIndex(
            dinnerSlotIndexes,
            resolvedMealTypeSlots.Count,
            selectedSpecialTreatCookDayIndex);
        if (preferredDinnerIndex.HasValue)
        {
            return preferredDinnerIndex.Value;
        }

        return dinnerSlotIndexes
            .OrderBy(index => CalculateScaledMealCost(selectedMeals[index], householdFactor, dayMultiplier: 1))
            .ThenBy(index => index)
            .First();
    }

    private static int? ResolvePreferredSpecialTreatDinnerIndex(
        IReadOnlyList<int> dinnerSlotIndexes,
        int mealsPerDay,
        int? selectedSpecialTreatCookDayIndex)
    {
        if (!selectedSpecialTreatCookDayIndex.HasValue || mealsPerDay <= 0)
        {
            return null;
        }

        var preferredCookDayIndex = selectedSpecialTreatCookDayIndex.Value;
        if (preferredCookDayIndex < 0)
        {
            return null;
        }

        foreach (var dinnerSlotIndex in dinnerSlotIndexes)
        {
            var cookDayIndex = dinnerSlotIndex / mealsPerDay;
            if (cookDayIndex == preferredCookDayIndex)
            {
                return dinnerSlotIndex;
            }
        }

        return null;
    }

    internal static bool HasSpecialTreatDinner(
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<string> mealTypeSlots)
    {
        if (selectedMeals.Count == 0 || mealTypeSlots.Count == 0)
        {
            return false;
        }

        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        return Enumerable.Range(0, selectedMeals.Count)
            .Where(index =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count]
                    .Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .Any(index => IsSpecialTreatMealCandidate(selectedMeals[index]));
    }

    private static int? ResolveSpecialTreatDisplayMealIndex(
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<string> mealTypeSlots,
        int? selectedSpecialTreatCookDayIndex)
    {
        if (selectedMeals.Count == 0 || mealTypeSlots.Count == 0)
        {
            return null;
        }

        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        var dinnerSlotIndexes = Enumerable.Range(0, selectedMeals.Count)
            .Where(index =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count]
                    .Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dinnerSlotIndexes.Count == 0)
        {
            return null;
        }

        var preferredDinnerIndex = ResolvePreferredSpecialTreatDinnerIndex(
            dinnerSlotIndexes,
            resolvedMealTypeSlots.Count,
            selectedSpecialTreatCookDayIndex);
        if (preferredDinnerIndex.HasValue &&
            IsSpecialTreatMealCandidate(selectedMeals[preferredDinnerIndex.Value]))
        {
            return preferredDinnerIndex.Value;
        }

        var firstTaggedDinnerIndex = dinnerSlotIndexes
            .FirstOrDefault(index =>
                selectedMeals[index].Tags.Contains("Special Treat", StringComparer.OrdinalIgnoreCase));
        if (firstTaggedDinnerIndex >= 0 &&
            firstTaggedDinnerIndex < selectedMeals.Count &&
            selectedMeals[firstTaggedDinnerIndex].Tags.Contains("Special Treat", StringComparer.OrdinalIgnoreCase))
        {
            return firstTaggedDinnerIndex;
        }

        var firstCandidateDinnerIndex = dinnerSlotIndexes
            .Cast<int?>()
            .FirstOrDefault(index => index.HasValue && IsSpecialTreatMealCandidate(selectedMeals[index.Value]));
        return firstCandidateDinnerIndex;
    }

    private static bool ForceReplaceDinnerWithSpecialTreatMeal(
        IList<MealTemplate> selectedMeals,
        MealTemplate specialTreatMeal,
        IReadOnlyList<string> mealTypeSlots,
        decimal householdFactor,
        int? selectedSpecialTreatCookDayIndex)
    {
        if (selectedMeals.Count == 0 || mealTypeSlots.Count == 0)
        {
            return false;
        }

        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        var dinnerSlotIndexes = Enumerable.Range(0, selectedMeals.Count)
            .Where(index =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count]
                    .Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dinnerSlotIndexes.Count == 0)
        {
            return false;
        }

        var targetDinnerIndex = ResolveTargetDinnerIndex(
            dinnerSlotIndexes,
            selectedMeals,
            resolvedMealTypeSlots,
            householdFactor,
            selectedSpecialTreatCookDayIndex);

        selectedMeals[targetDinnerIndex] = MarkMealAsSpecialTreat(EnsureMealTypeSuitability(specialTreatMeal));
        return true;
    }

    private static MealTemplate MarkMealAsSpecialTreat(MealTemplate meal)
    {
        var tags = meal.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!tags.Contains("Special Treat", StringComparer.OrdinalIgnoreCase))
        {
            tags.Add("Special Treat");
        }

        return meal with { Tags = tags };
    }

}
