namespace MyBlog.Services;

public sealed class AislePilotSlotSelectionEngine
{
    internal IReadOnlyList<AislePilotService.MealTemplate> SelectMeals(
        IReadOnlyList<AislePilotService.MealTemplate> mealSource,
        IReadOnlyList<string> dietaryModes,
        decimal weeklyBudget,
        decimal householdFactor,
        decimal priceFactor,
        bool preferQuickMeals,
        string dislikesOrAllergens,
        int requestedMealCount,
        IReadOnlyList<string>? mealTypeSlots = null,
        bool includeSpecialTreatMeal = false,
        int? selectedSpecialTreatCookDayIndex = null,
        IReadOnlySet<string>? savedEnjoyedMealNames = null,
        bool enableSavedMealRepeats = false,
        int savedMealRepeatRatePercent = 35,
        IReadOnlyList<string>? excludedMealNames = null)
    {
        var normalizedExcludedMealNames = (excludedMealNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolvedMealTypeSlots = AislePilotService.NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        var normalizedMealCount = AislePilotService.NormalizeRequestedMealCount(requestedMealCount);
        var allCompatibleCandidates = AislePilotService.FilterMeals(dietaryModes, dislikesOrAllergens, mealSource)
            .Select(meal => AislePilotService.EnsureMealTypeSuitability(meal))
            .ToList();
        if (allCompatibleCandidates.Count == 0)
        {
            throw new InvalidOperationException(
                "No meals match the selected dietary modes and dislikes/allergens.");
        }

        var effectiveExcludedMealNames = RelaxExcludedMealNamesForRequiredSlotCoverage(
            allCompatibleCandidates,
            normalizedExcludedMealNames,
            resolvedMealTypeSlots,
            normalizedMealCount);
        var candidates = allCompatibleCandidates
            .Where(meal => !effectiveExcludedMealNames.Contains(meal.Name))
            .ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "Could not find a fully refreshed week with the current settings. Try loosening filters or swapping individual meals.");
        }

        var safeMealsPerDay = resolvedMealTypeSlots.Count;
        var targetMealCost = weeklyBudget / (7m * safeMealsPerDay);
        var preferHighProtein = AislePilotService.IsHighProteinPreferred(dietaryModes);
        var scoredCandidates = candidates
            .Select(template =>
            {
                var scaledCost = template.BaseCostForTwo * householdFactor *
                                 AislePilotService.NormalizeSupermarketPriceFactor(priceFactor);
                var budgetDistance = Math.Abs(scaledCost - targetMealCost);
                var quickPenalty = preferQuickMeals && !template.IsQuick ? 0.8m : 0m;
                var highProteinPenalty =
                    preferHighProtein &&
                    !template.Tags.Contains("High-Protein", StringComparer.OrdinalIgnoreCase)
                        ? 0.45m
                        : 0m;
                return new { template, score = budgetDistance + quickPenalty + highProteinPenalty };
            })
            .OrderBy(item => item.score)
            .ThenBy(item => item.template.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.template)
            .ToList();

        var selected = new List<AislePilotService.MealTemplate>(normalizedMealCount);
        var daySeed = DateOnly.FromDateTime(DateTime.UtcNow).DayNumber;
        var budgetSeed = (long)decimal.Truncate(Math.Abs(weeklyBudget) * 100m);
        var quickSeed = preferQuickMeals ? 17L : 0L;
        var rotationSeed = Math.Abs((long)daySeed + budgetSeed + quickSeed + normalizedMealCount);
        var normalizedSavedMealRepeatRatePercent = Math.Clamp(savedMealRepeatRatePercent, 10, 100);
        var normalizedSavedMealNames = savedEnjoyedMealNames is null || savedEnjoyedMealNames.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(savedEnjoyedMealNames, StringComparer.OrdinalIgnoreCase);
        var startIndex = (int)(rotationSeed % scoredCandidates.Count);
        var rotatedCandidates = scoredCandidates
            .Skip(startIndex)
            .Concat(scoredCandidates.Take(startIndex))
            .ToList();

        for (var i = 0; i < normalizedMealCount; i++)
        {
            var slotMealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
            var slotCompatibleCandidates = rotatedCandidates
                .Where(meal => AislePilotService.SupportsMealType(meal, slotMealType))
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
            var candidate = AislePilotService.SelectCandidateForSlot(
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
            var treatApplied = AislePilotService.TryApplySpecialTreatMeal(
                selected,
                rotatedCandidates,
                resolvedMealTypeSlots,
                householdFactor,
                priceFactor,
                selectedSpecialTreatCookDayIndex);
            if (!treatApplied || !AislePilotService.HasSpecialTreatDinner(selected, resolvedMealTypeSlots))
            {
                throw new InvalidOperationException(
                    "No indulgent special treat dinner was available for the current settings.");
            }
        }

        return selected;
    }

    private static HashSet<string> RelaxExcludedMealNamesForRequiredSlotCoverage(
        IReadOnlyList<AislePilotService.MealTemplate> compatibleCandidates,
        IReadOnlySet<string> excludedMealNames,
        IReadOnlyList<string> resolvedMealTypeSlots,
        int requestedMealCount)
    {
        var effectiveExcludedMealNames = new HashSet<string>(excludedMealNames, StringComparer.OrdinalIgnoreCase);
        if (effectiveExcludedMealNames.Count == 0 ||
            compatibleCandidates.Count == 0 ||
            resolvedMealTypeSlots.Count == 0 ||
            requestedMealCount <= 0)
        {
            return effectiveExcludedMealNames;
        }

        foreach (var slotMealType in resolvedMealTypeSlots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var compatibleMealNamesForSlot = compatibleCandidates
                .Where(meal => AislePilotService.SupportsMealType(meal, slotMealType))
                .Select(meal => meal.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (compatibleMealNamesForSlot.Count == 0)
            {
                continue;
            }

            var slotCount = CountSlotsForMealType(resolvedMealTypeSlots, slotMealType, requestedMealCount);
            var maxRepeatsForSlot = Math.Max(
                1,
                AislePilotService.ResolveMaxMealRepeatsForSlotType(slotMealType, slotCount));
            var preferredUniqueMealCount = Math.Max(
                1,
                (int)Math.Ceiling(slotCount / (decimal)maxRepeatsForSlot));
            var currentlyAvailableCount = compatibleMealNamesForSlot.Count(name =>
                !effectiveExcludedMealNames.Contains(name));
            if (currentlyAvailableCount >= preferredUniqueMealCount)
            {
                continue;
            }

            foreach (var mealName in compatibleMealNamesForSlot.Where(effectiveExcludedMealNames.Contains))
            {
                effectiveExcludedMealNames.Remove(mealName);
                currentlyAvailableCount++;
                if (currentlyAvailableCount >= preferredUniqueMealCount)
                {
                    break;
                }
            }
        }

        return effectiveExcludedMealNames;
    }

    private static int CountSlotsForMealType(
        IReadOnlyList<string> resolvedMealTypeSlots,
        string mealType,
        int requestedMealCount)
    {
        if (resolvedMealTypeSlots.Count == 0 || requestedMealCount <= 0)
        {
            return 0;
        }

        var slotCount = 0;
        for (var slotIndex = 0; slotIndex < requestedMealCount; slotIndex++)
        {
            if (resolvedMealTypeSlots[slotIndex % resolvedMealTypeSlots.Count].Equals(
                    mealType,
                    StringComparison.OrdinalIgnoreCase))
            {
                slotCount++;
            }
        }

        return slotCount;
    }

    private static bool ShouldPreferSavedMealForSlot(
        long rotationSeed,
        int slotIndex,
        int savedMealRepeatRatePercent)
    {
        var normalizedRate = Math.Clamp(savedMealRepeatRatePercent, 10, 100);
        if (normalizedRate >= 100)
        {
            return true;
        }

        var slotSeed = Math.Abs(rotationSeed + ((slotIndex + 1L) * 43L));
        var percentile = (int)(slotSeed % 100L);
        return percentile < normalizedRate;
    }
}
