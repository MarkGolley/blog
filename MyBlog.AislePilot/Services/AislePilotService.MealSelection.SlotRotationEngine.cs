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
    internal static MealTemplate SelectCandidateForSlot(
        IReadOnlyList<MealTemplate> slotCompatibleCandidates,
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<string> resolvedMealTypeSlots,
        int slotIndex,
        int totalSlotCount,
        IReadOnlySet<string>? preferredMealNames = null,
        bool shouldPreferPreferredMeals = false)
    {
        if (slotCompatibleCandidates.Count == 0)
        {
            throw new InvalidOperationException("No slot-compatible meals are available for selection.");
        }

        var slotMealType = resolvedMealTypeSlots[slotIndex % resolvedMealTypeSlots.Count];
        var isDinnerSlot = slotMealType.Equals("Dinner", StringComparison.OrdinalIgnoreCase);
        var slotTypeSlotCount = CountSlotsForMealType(resolvedMealTypeSlots, slotMealType, totalSlotCount);
        var maxRepeatsForSlotType = ResolveMaxMealRepeatsForSlotType(slotMealType, slotTypeSlotCount);
        var usedMealNames = selectedMeals
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedDinnerMealNames = selectedMeals
            .Where((_, index) =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count].Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedSlotMealTypeCounts = selectedMeals
            .Where((_, index) =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count].Equals(slotMealType, StringComparison.OrdinalIgnoreCase))
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        int GetSlotTypeRepeatCount(MealTemplate meal)
        {
            return usedSlotMealTypeCounts.TryGetValue(meal.Name, out var count) ? count : 0;
        }

        string? GetMostRecentSlotMealName()
        {
            for (var selectedIndex = selectedMeals.Count - 1; selectedIndex >= 0; selectedIndex--)
            {
                if (resolvedMealTypeSlots[selectedIndex % resolvedMealTypeSlots.Count].Equals(
                        slotMealType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return selectedMeals[selectedIndex].Name;
                }
            }

            return null;
        }

        if (shouldPreferPreferredMeals && preferredMealNames is { Count: > 0 })
        {
            MealTemplate? FindPreferredCandidate(Func<MealTemplate, bool> predicate)
            {
                return slotCompatibleCandidates.FirstOrDefault(meal =>
                    preferredMealNames.Contains(meal.Name) &&
                    predicate(meal));
            }

            var preferredSavedCandidate = FindPreferredCandidate(meal =>
                GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
                (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)) &&
                (selectedMeals.Count == 0 ||
                 !meal.Name.Equals(selectedMeals[^1].Name, StringComparison.OrdinalIgnoreCase)));
            if (preferredSavedCandidate is not null)
            {
                return preferredSavedCandidate;
            }

            preferredSavedCandidate = FindPreferredCandidate(meal =>
                !usedMealNames.Contains(meal.Name) &&
                GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
                (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)));
            if (preferredSavedCandidate is not null)
            {
                return preferredSavedCandidate;
            }
        }

        var preferredCandidate = slotCompatibleCandidates.FirstOrDefault(meal =>
            !usedMealNames.Contains(meal.Name) &&
            GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
            (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)));
        if (preferredCandidate is not null)
        {
            return preferredCandidate;
        }

        var cappedRepeatCandidate = slotCompatibleCandidates.FirstOrDefault(meal =>
            GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
            (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)));
        if (cappedRepeatCandidate is not null)
        {
            return cappedRepeatCandidate;
        }

        if (!isDinnerSlot)
        {
            var previousName = selectedMeals.Count > 0 ? selectedMeals[^1].Name : null;
            var nonAdjacentCandidate = slotCompatibleCandidates
                .FirstOrDefault(meal =>
                    GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
                    (previousName is null || !meal.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase)));
            if (nonAdjacentCandidate is not null)
            {
                return nonAdjacentCandidate;
            }
        }

        var previousMealName = selectedMeals.Count > 0 ? selectedMeals[^1].Name : null;
        var mostRecentSlotMealName = GetMostRecentSlotMealName();
        var leastRepeatedCandidates = slotCompatibleCandidates
            .OrderBy(GetSlotTypeRepeatCount)
            .ThenBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var balancedFallback = leastRepeatedCandidates.FirstOrDefault(meal =>
            (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)) &&
            (mostRecentSlotMealName is null || !meal.Name.Equals(mostRecentSlotMealName, StringComparison.OrdinalIgnoreCase)) &&
            (previousMealName is null || !meal.Name.Equals(previousMealName, StringComparison.OrdinalIgnoreCase)));
        if (balancedFallback is not null)
        {
            return balancedFallback;
        }

        balancedFallback = leastRepeatedCandidates.FirstOrDefault(meal =>
            (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)) &&
            (mostRecentSlotMealName is null || !meal.Name.Equals(mostRecentSlotMealName, StringComparison.OrdinalIgnoreCase)));
        if (balancedFallback is not null)
        {
            return balancedFallback;
        }

        balancedFallback = leastRepeatedCandidates.FirstOrDefault(meal =>
            !isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name));
        if (balancedFallback is not null)
        {
            return balancedFallback;
        }

        return leastRepeatedCandidates[0];
    }

    private static int CountSlotsForMealType(
        IReadOnlyList<string> resolvedMealTypeSlots,
        string mealType,
        int totalSlotCount)
    {
        if (resolvedMealTypeSlots.Count == 0 || totalSlotCount <= 0)
        {
            return 0;
        }

        var normalizedTotalSlotCount = Math.Max(1, totalSlotCount);
        var count = 0;
        for (var i = 0; i < normalizedTotalSlotCount; i++)
        {
            if (resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count].Equals(mealType, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    internal static int ResolveMaxMealRepeatsForSlotType(string mealType, int slotCount)
    {
        var normalizedMealType = NormalizeMealType(mealType);
        var normalizedSlotCount = Math.Max(0, slotCount);
        if (normalizedMealType.Equals("Dinner", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        // Breakfast/lunch can repeat, but avoid a single meal dominating the week.
        return normalizedSlotCount >= 4 ? 2 : 1;
    }

}
