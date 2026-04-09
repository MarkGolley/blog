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
    internal static MealTemplate? SelectSwapCandidate(
        IReadOnlyList<MealTemplate> allCandidates,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        string currentMealName,
        decimal weeklyBudget,
        decimal householdFactor,
        bool preferQuickMeals,
        bool preferHighProtein,
        string mealType,
        int dayMultiplier,
        int mealsPerDay)
    {
        if (allCandidates.Count == 0)
        {
            return null;
        }

        var usedNames = selectedMeals
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dedupedCandidates = allCandidates
            .Select(meal => EnsureMealTypeSuitability(meal))
            .ToList();
        var preferredPool = dedupedCandidates
            .Where(meal =>
                !meal.Name.Equals(currentMealName, StringComparison.OrdinalIgnoreCase) &&
                !usedNames.Contains(meal.Name))
            .ToList();

        if (preferredPool.Count == 0)
        {
            return null;
        }

        var normalizedDayMultiplier = Math.Max(1, dayMultiplier);
        var safeMealsPerDay = NormalizeMealsPerDay(mealsPerDay);
        var targetMealCost = (weeklyBudget / (7m * safeMealsPerDay)) * normalizedDayMultiplier;
        var previousName = dayIndex > 0 ? selectedMeals[dayIndex - 1].Name : null;
        var nextName = dayIndex < selectedMeals.Count - 1 ? selectedMeals[dayIndex + 1].Name : null;
        var slotCompatiblePool = preferredPool
            .Where(meal => SupportsMealType(meal, mealType))
            .ToList();
        if (slotCompatiblePool.Count == 0)
        {
            return null;
        }

        return slotCompatiblePool
            .Select(template => new
            {
                template,
                score = BuildMealSelectionScore(
                    template,
                    targetMealCost,
                    householdFactor,
                    preferQuickMeals,
                    preferHighProtein,
                    normalizedDayMultiplier,
                    previousName,
                    nextName)
            })
            .OrderBy(item => item.score)
            .ThenBy(item => item.template.Name, StringComparer.OrdinalIgnoreCase)
            .First()
            .template;
    }

    private static bool HasUniqueMealNames(
        IReadOnlyList<MealTemplate> meals,
        int expectedMeals,
        IReadOnlyList<string>? mealTypeSlots = null)
    {
        if (meals.Count < expectedMeals)
        {
            return false;
        }

        if (mealTypeSlots is { Count: > 0 })
        {
            var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
            var slotTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < expectedMeals; i++)
            {
                var slotMealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
                slotTypeCounts[slotMealType] = slotTypeCounts.GetValueOrDefault(slotMealType, 0) + 1;
            }

            var slotTypeMealNameCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < expectedMeals; i++)
            {
                var meal = meals[i];
                var slotMealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
                if (!SupportsMealType(meal, slotMealType))
                {
                    return false;
                }

                if (!slotTypeMealNameCounts.TryGetValue(slotMealType, out var mealNameCounts))
                {
                    mealNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    slotTypeMealNameCounts[slotMealType] = mealNameCounts;
                }

                var nextCount = mealNameCounts.GetValueOrDefault(meal.Name, 0) + 1;
                mealNameCounts[meal.Name] = nextCount;

                var slotCount = slotTypeCounts.GetValueOrDefault(slotMealType, 0);
                var maxRepeats = ResolveMaxMealRepeatsForSlotType(slotMealType, slotCount);
                if (nextCount > maxRepeats)
                {
                    return false;
                }
            }

            return true;
        }

        var uniqueCount = meals
            .Take(expectedMeals)
            .Select(meal => meal.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return uniqueCount == expectedMeals;
    }

    private static decimal BuildMealSelectionScore(
        MealTemplate template,
        decimal targetMealCost,
        decimal householdFactor,
        bool preferQuickMeals,
        bool preferHighProtein,
        int dayMultiplier = 1,
        string? previousName = null,
        string? nextName = null)
    {
        var normalizedDayMultiplier = Math.Max(1, dayMultiplier);
        var scaledCost = template.BaseCostForTwo * householdFactor * normalizedDayMultiplier;
        var budgetDistance = Math.Abs(scaledCost - targetMealCost);
        var quickPenalty = preferQuickMeals && !template.IsQuick ? 0.8m : 0m;
        var highProteinPenalty =
            preferHighProtein &&
            !template.Tags.Contains("High-Protein", StringComparer.OrdinalIgnoreCase)
                ? 0.45m
                : 0m;
        var adjacencyPenalty =
            (previousName is not null && template.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase)) ||
            (nextName is not null && template.Name.Equals(nextName, StringComparison.OrdinalIgnoreCase))
                ? 1.2m
                : 0m;

        return budgetDistance + quickPenalty + highProteinPenalty + adjacencyPenalty;
    }

}
