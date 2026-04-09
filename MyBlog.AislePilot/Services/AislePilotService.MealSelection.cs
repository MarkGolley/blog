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
    private IReadOnlyList<AislePilotMealDayViewModel> BuildDailyPlans(
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<int> mealPortionMultipliers,
        IReadOnlyList<int> dayPortionMultipliers,
        IReadOnlyList<string> mealTypeSlots,
        IReadOnlySet<int> ignoredMealSlotIndexes,
        IReadOnlyDictionary<string, string> mealImageUrls,
        decimal householdFactor,
        decimal portionSizeFactor,
        IReadOnlyList<string> dietaryModes,
        string dislikesOrAllergens,
        int? specialTreatMealSlotIndex = null)
    {
        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        var safeMealsPerDay = resolvedMealTypeSlots.Count;
        var normalizedMealCount = Math.Min(selectedMeals.Count, mealPortionMultipliers.Count);
        var cookDayNames = BuildCookDayNames(dayPortionMultipliers).Take(dayPortionMultipliers.Count).ToArray();
        var plans = new List<AislePilotMealDayViewModel>(normalizedMealCount);
        for (var i = 0; i < normalizedMealCount; i++)
        {
            var template = selectedMeals[i];
            var dayIndex = dayPortionMultipliers.Count == 0
                ? 0
                : Math.Min(dayPortionMultipliers.Count - 1, i / safeMealsPerDay);
            var cookDayName = cookDayNames.Length == 0
                ? "Monday"
                : cookDayNames[Math.Min(dayIndex, cookDayNames.Length - 1)];
            var mealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
            var mealPortionMultiplier = Math.Max(1, mealPortionMultipliers[i]);
            var isIgnored = ignoredMealSlotIndexes.Contains(i);
            var isSpecialTreat = specialTreatMealSlotIndex.HasValue &&
                                 i == specialTreatMealSlotIndex.Value;
            var estimatedCost = isIgnored
                ? 0m
                : decimal.Round(
                    template.BaseCostForTwo * householdFactor * mealPortionMultiplier,
                    2,
                    MidpointRounding.AwayFromZero);
            var reason = template.IsQuick
                ? "Quick prep for busy days."
                : "Batch-friendly and good for leftovers.";
            if (safeMealsPerDay > 1)
            {
                reason = $"{mealType} option. {reason}";
            }

            if (isSpecialTreat && !isIgnored)
            {
                reason = $"Special treat pick for this week. {reason}";
            }

            if (isIgnored)
            {
                reason = "Ignored in this plan. Cost and shopping list contributions are excluded.";
            }

            if (dietaryModes.Count > 1)
            {
                reason += $" Matches {string.Join(", ", dietaryModes.Where(x => !x.Equals("Balanced", StringComparison.OrdinalIgnoreCase)))}.";
            }

            if (!string.IsNullOrWhiteSpace(dislikesOrAllergens))
            {
                reason += " Built around your allergy/dislike notes.";
            }
            var leftoverDaysCovered = Math.Max(0, mealPortionMultiplier - 1);
            if (leftoverDaysCovered > 0)
            {
                reason += $" Cooks extra portions for {leftoverDaysCovered} leftover day(s).";
            }

            var ingredientLines = template.Ingredients
                .Select(ingredient =>
                {
                    var quantity = decimal.Round(
                        ingredient.QuantityForTwo * householdFactor * mealPortionMultiplier,
                        2,
                        MidpointRounding.AwayFromZero);
                    return $"{QuantityDisplayFormatter.FormatForRecipe(quantity, ingredient.Unit)} {ingredient.Name}";
                })
                .ToList();
            var basePrepMinutes = template.IsQuick ? 25 : 40;
            var estimatedPrepMinutes = RoundToNearestFiveMinutes(basePrepMinutes + (leftoverDaysCovered * 8));
            var nutrition = _nutritionRecipeFallbackEngine.EstimateMealNutritionPerServing(template, portionSizeFactor);

            plans.Add(new AislePilotMealDayViewModel
            {
                Day = cookDayName,
                MealType = mealType,
                IsIgnored = isIgnored,
                MealName = template.Name,
                IsSpecialTreat = isSpecialTreat,
                MealImageUrl = mealImageUrls.GetValueOrDefault(template.Name, GetFallbackMealImageUrl()),
                MealReason = reason,
                LeftoverDaysCovered = leftoverDaysCovered,
                EstimatedCost = estimatedCost,
                EstimatedPrepMinutes = estimatedPrepMinutes,
                CaloriesPerServing = nutrition.CaloriesPerServing,
                ProteinGramsPerServing = nutrition.ProteinGramsPerServing,
                CarbsGramsPerServing = nutrition.CarbsGramsPerServing,
                FatGramsPerServing = nutrition.FatGramsPerServing,
                IngredientLines = ingredientLines,
                RecipeSteps = _nutritionRecipeFallbackEngine.BuildRecipeSteps(template)
            });
        }

        return plans;
    }

    private static IReadOnlyList<string> BuildCookDayNames(IReadOnlyList<int> mealPortionMultipliers)
    {
        var weekDays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        if (mealPortionMultipliers.Count == 0)
        {
            return [];
        }

        var dayNames = new List<string>(mealPortionMultipliers.Count);
        var dayCursor = 0;
        for (var i = 0; i < mealPortionMultipliers.Count; i++)
        {
            var safeDayIndex = Math.Clamp(dayCursor, 0, weekDays.Length - 1);
            dayNames.Add(weekDays[safeDayIndex]);
            dayCursor += Math.Max(1, mealPortionMultipliers[i]);
        }

        return dayNames;
    }

    private static DessertAddOnTemplate ResolveDessertAddOnTemplate(string? selectedDessertAddOnName)
    {
        var availableDessertTemplates = GetAvailableDessertAddOnTemplatesSnapshot();
        if (availableDessertTemplates.Count == 0)
        {
            throw new InvalidOperationException("No dessert add-on templates are configured.");
        }

        if (!string.IsNullOrWhiteSpace(selectedDessertAddOnName))
        {
            var normalizedSelectedDessertName = selectedDessertAddOnName.Trim();
            var selectedTemplate = availableDessertTemplates.FirstOrDefault(template =>
                template.Name.Equals(normalizedSelectedDessertName, StringComparison.OrdinalIgnoreCase));
            if (selectedTemplate is not null)
            {
                return selectedTemplate;
            }
        }

        return availableDessertTemplates[0];
    }

    private static IReadOnlyList<AislePilotShoppingItemViewModel> BuildShoppingList(
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<int> mealPortionMultipliers,
        IReadOnlySet<int> ignoredMealSlotIndexes,
        decimal householdFactor,
        IReadOnlyList<string> aisleOrder,
        DessertAddOnTemplate? dessertAddOnTemplate)
    {
        var aggregated = new Dictionary<string, MutableShoppingItem>(StringComparer.OrdinalIgnoreCase);
        var mealCount = Math.Min(selectedMeals.Count, mealPortionMultipliers.Count);
        for (var i = 0; i < mealCount; i++)
        {
            if (ignoredMealSlotIndexes.Contains(i))
            {
                continue;
            }

            var meal = selectedMeals[i];
            var mealPortionMultiplier = Math.Max(1, mealPortionMultipliers[i]);
            foreach (var ingredient in meal.Ingredients)
            {
                var key = $"{ingredient.Department}|{ingredient.Name}|{ingredient.Unit}";
                if (!aggregated.TryGetValue(key, out var existing))
                {
                    existing = new MutableShoppingItem
                    {
                        Department = ingredient.Department,
                        Name = ingredient.Name,
                        Unit = ingredient.Unit
                    };
                    aggregated[key] = existing;
                }

                existing.Quantity += ingredient.QuantityForTwo * householdFactor * mealPortionMultiplier;
                existing.EstimatedCost += ingredient.EstimatedCostForTwo * householdFactor * mealPortionMultiplier;
            }
        }

        if (dessertAddOnTemplate is not null)
        {
            AddDessertAddOnShoppingItems(aggregated, householdFactor, dessertAddOnTemplate);
        }

        var departmentOrder = aisleOrder
            .Select((department, index) => new { department, index })
            .ToDictionary(x => x.department, x => x.index, StringComparer.OrdinalIgnoreCase);

        return aggregated.Values
            .Select(item => new AislePilotShoppingItemViewModel
            {
                Department = item.Department,
                Name = item.Name,
                Unit = item.Unit,
                Quantity = decimal.Round(item.Quantity, 2, MidpointRounding.AwayFromZero),
                EstimatedCost = decimal.Round(item.EstimatedCost, 2, MidpointRounding.AwayFromZero)
            })
            .OrderBy(item => departmentOrder.GetValueOrDefault(item.Department, int.MaxValue))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddDessertAddOnShoppingItems(
        IDictionary<string, MutableShoppingItem> aggregated,
        decimal householdFactor,
        DessertAddOnTemplate dessertAddOnTemplate)
    {
        var scale = Math.Clamp(householdFactor, 0.5m, 4m);
        foreach (var ingredient in dessertAddOnTemplate.Ingredients)
        {
            var key = $"{ingredient.Department}|{ingredient.Name}|{ingredient.Unit}";
            if (!aggregated.TryGetValue(key, out var existing))
            {
                existing = new MutableShoppingItem
                {
                    Department = ingredient.Department,
                    Name = ingredient.Name,
                    Unit = ingredient.Unit
                };
                aggregated[key] = existing;
            }

            existing.Quantity += ingredient.QuantityForTwo * scale;
            existing.EstimatedCost += ingredient.EstimatedCostForTwo * scale;
        }
    }

    private static decimal CalculateDessertAddOnEstimatedCost(
        decimal householdFactor,
        DessertAddOnTemplate dessertAddOnTemplate)
    {
        var scale = Math.Clamp(householdFactor, 0.5m, 4m);
        var estimatedCost = dessertAddOnTemplate.Ingredients.Sum(ingredient => ingredient.EstimatedCostForTwo * scale);
        return decimal.Round(estimatedCost, 2, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<string> BuildDessertAddOnIngredientLines(
        decimal householdFactor,
        DessertAddOnTemplate dessertAddOnTemplate)
    {
        var scale = Math.Clamp(householdFactor, 0.5m, 4m);
        return dessertAddOnTemplate.Ingredients
            .Select(ingredient =>
            {
                var scaledQuantity = decimal.Round(
                    ingredient.QuantityForTwo * scale,
                    2,
                    MidpointRounding.AwayFromZero);
                return $"{QuantityDisplayFormatter.FormatForRecipe(scaledQuantity, ingredient.Unit)} {ingredient.Name}";
            })
            .ToList();
    }

    private IReadOnlyList<MealTemplate> SelectMeals(
        IReadOnlyList<MealTemplate> mealSource,
        IReadOnlyList<string> dietaryModes,
        decimal weeklyBudget,
        decimal householdFactor,
        bool preferQuickMeals,
        string dislikesOrAllergens,
        int requestedMealCount,
        IReadOnlyList<string>? mealTypeSlots = null,
        bool includeSpecialTreatMeal = false,
        int? selectedSpecialTreatCookDayIndex = null,
        IReadOnlySet<string>? savedEnjoyedMealNames = null,
        bool enableSavedMealRepeats = false,
        int savedMealRepeatRatePercent = DefaultSavedMealRepeatRatePercent)
    {
        return _slotSelectionEngine.SelectMeals(
            mealSource,
            dietaryModes,
            weeklyBudget,
            householdFactor,
            preferQuickMeals,
            dislikesOrAllergens,
            requestedMealCount,
            mealTypeSlots,
            includeSpecialTreatMeal,
            selectedSpecialTreatCookDayIndex,
            savedEnjoyedMealNames,
            enableSavedMealRepeats,
            savedMealRepeatRatePercent);
    }

    internal static bool ShouldPreferSavedMealForSlot(
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

    internal static IReadOnlyList<string> ResolveHardDietaryModes(IReadOnlyList<string> dietaryModes)
    {
        return dietaryModes
            .Where(mode =>
                !mode.Equals("Balanced", StringComparison.OrdinalIgnoreCase) &&
                !mode.Equals("High-Protein", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static bool IsHighProteinPreferred(IReadOnlyList<string> dietaryModes)
    {
        return dietaryModes.Any(mode => mode.Equals("High-Protein", StringComparison.OrdinalIgnoreCase));
    }

    internal static List<MealTemplate> FilterMeals(
        IReadOnlyList<string> dietaryModes,
        string dislikesOrAllergens,
        IReadOnlyList<MealTemplate>? mealSource = null)
    {
        var disallowedTokens = dislikesOrAllergens
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length >= 3)
            .ToList();

        var hardModes = ResolveHardDietaryModes(dietaryModes);
        var preferHighProtein = IsHighProteinPreferred(dietaryModes);

        var source = mealSource ?? MealTemplates;

        var baseFiltered = source
            .Where(meal => disallowedTokens.All(token => !ContainsToken(meal, token)))
            .ToList();

        // Balanced is the default baseline; High-Protein is a preference, not a hard blocker.
        if (hardModes.Count == 0)
        {
            return baseFiltered
                .Where(meal =>
                    meal.Tags.Contains("Balanced", StringComparer.OrdinalIgnoreCase) ||
                    (preferHighProtein && meal.Tags.Contains("High-Protein", StringComparer.OrdinalIgnoreCase)))
                .ToList();
        }

        var strictFiltered = baseFiltered
            .Where(meal => hardModes.All(mode => meal.Tags.Contains(mode, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        return strictFiltered;
    }

    private static IReadOnlySet<string> ParseSavedEnjoyedMealNamesState(string? savedEnjoyedMealNamesState)
    {
        if (string.IsNullOrWhiteSpace(savedEnjoyedMealNamesState))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(savedEnjoyedMealNamesState, JsonOptions);
            if (parsed is null || parsed.Count == 0)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return parsed
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Where(name => name.Length is > 0 and <= MaxSavedEnjoyedMealNameLength)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxSavedEnjoyedMealCount)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static IReadOnlyList<MealTemplate> GetCompatibleAiPoolMeals(
        IReadOnlyList<string> dietaryModes,
        string dislikesOrAllergens)
    {
        PruneAiMealPool(DateTime.UtcNow);
        return FilterMeals(dietaryModes, dislikesOrAllergens, AiMealPool.Values.ToList());
    }

    internal static void AddMealsToAiPool(IReadOnlyList<MealTemplate> meals)
    {
        var nowUtc = DateTime.UtcNow;
        foreach (var meal in meals)
        {
            UpsertAiMealPoolEntry(meal, nowUtc);
        }

        PruneAiMealPool(nowUtc);
    }

    private static void UpsertAiMealPoolEntry(MealTemplate meal, DateTime touchedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(meal.Name))
        {
            return;
        }

        var normalizedMeal = EnsureMealTypeSuitability(meal);
        if (AiMealPool.TryGetValue(normalizedMeal.Name, out var existingMeal) &&
            !HasEquivalentIngredientProfiles(existingMeal, normalizedMeal))
        {
            // Same meal name but different ingredient profile: prevent stale image reuse.
            MealImagePool.TryRemove(normalizedMeal.Name, out _);
            ClearMealImageLookupMiss(normalizedMeal.Name);
            normalizedMeal = normalizedMeal with { ImageUrl = string.Empty };
        }

        AiMealPool[normalizedMeal.Name] = normalizedMeal;
        AiMealPoolLastTouchedUtc[normalizedMeal.Name] = touchedAtUtc;
    }

    private static bool HasEquivalentIngredientProfiles(MealTemplate left, MealTemplate right)
    {
        return BuildIngredientProfileKey(left.Ingredients)
            .Equals(BuildIngredientProfileKey(right.Ingredients), StringComparison.Ordinal);
    }

    private static string BuildIngredientProfileKey(IReadOnlyList<IngredientTemplate> ingredients)
    {
        return string.Join(
            ";",
            ingredients
                .Select(ingredient =>
                    $"{NormalizePantryText(ingredient.Name)}|{NormalizeAiUnitForPricing(ingredient.Unit)}|{decimal.Round(ingredient.QuantityForTwo, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture)}")
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static void RemoveAiMealPoolEntry(string mealName)
    {
        AiMealPool.TryRemove(mealName, out _);
        AiMealPoolLastTouchedUtc.TryRemove(mealName, out _);
    }

    private static void PruneAiMealPool(DateTime nowUtc)
    {
        foreach (var entry in AiMealPoolLastTouchedUtc)
        {
            if (nowUtc - entry.Value > AiMealPoolEntryTtl)
            {
                RemoveAiMealPoolEntry(entry.Key);
            }
        }

        foreach (var mealName in AiMealPool.Keys)
        {
            if (!AiMealPoolLastTouchedUtc.ContainsKey(mealName))
            {
                RemoveAiMealPoolEntry(mealName);
            }
        }

        var overflowCount = AiMealPool.Count - MaxAiMealPoolEntries;
        if (overflowCount <= 0)
        {
            return;
        }

        var evictionCandidates = AiMealPoolLastTouchedUtc
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Take(overflowCount)
            .Select(entry => entry.Key)
            .ToList();

        foreach (var mealName in evictionCandidates)
        {
            RemoveAiMealPoolEntry(mealName);
        }
    }

    private static string ToAiMealDocumentId(string mealName)
    {
        var normalized = NormalizePantryText(mealName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        }

        return normalized.Replace(' ', '-');
    }

    private static FirestoreAislePilotMeal ToFirestoreDocument(MealTemplate meal)
    {
        return new FirestoreAislePilotMeal
        {
            Name = meal.Name,
            BaseCostForTwo = (double)meal.BaseCostForTwo,
            IsQuick = meal.IsQuick,
            Tags = meal.Tags.ToList(),
            Ingredients = meal.Ingredients
                .Select(ingredient => new FirestoreAislePilotIngredient
                {
                    Name = ingredient.Name,
                    Department = ingredient.Department,
                    QuantityForTwo = (double)ingredient.QuantityForTwo,
                    Unit = ingredient.Unit,
                    EstimatedCostForTwo = (double)ingredient.EstimatedCostForTwo
                })
                .ToList(),
            RecipeSteps = meal.AiRecipeSteps?.ToList() ?? [],
            NutritionPerServingMedium = meal.AiNutritionPerServingMedium is null
                ? null
                : new FirestoreAislePilotNutrition
                {
                    Calories = meal.AiNutritionPerServingMedium.CaloriesPerServingMedium,
                    ProteinGrams = (double)meal.AiNutritionPerServingMedium.ProteinGramsPerServingMedium,
                    CarbsGrams = (double)meal.AiNutritionPerServingMedium.CarbsGramsPerServingMedium,
                    FatGrams = (double)meal.AiNutritionPerServingMedium.FatGramsPerServingMedium,
                    ConfidenceScore = (double)meal.AiNutritionPerServingMedium.ConfidenceScore
                },
            ImageUrl = meal.ImageUrl ?? string.Empty,
            SuitableMealTypes = ResolveSuitableMealTypes(meal).ToList(),
            CreatedAtUtc = DateTime.UtcNow,
            Source = "openai"
        };
    }

    private static FirestoreAislePilotDessertAddOn ToFirestoreDessertAddOnDocument(DessertAddOnTemplate dessertAddOnTemplate)
    {
        return new FirestoreAislePilotDessertAddOn
        {
            Name = dessertAddOnTemplate.Name,
            Ingredients = dessertAddOnTemplate.Ingredients
                .Select(ingredient => new FirestoreAislePilotIngredient
                {
                    Name = ingredient.Name,
                    Department = ingredient.Department,
                    QuantityForTwo = (double)ingredient.QuantityForTwo,
                    Unit = ingredient.Unit,
                    EstimatedCostForTwo = (double)ingredient.EstimatedCostForTwo
                })
                .ToList(),
            UpdatedAtUtc = DateTime.UtcNow,
            Source = "app"
        };
    }

    private static DessertAddOnTemplate? FromFirestoreDessertAddOnDocument(FirestoreAislePilotDessertAddOn? doc)
    {
        if (doc is null)
        {
            return null;
        }

        var normalizedName = ClampAndNormalize(doc.Name, MaxAiMealNameLength);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        var normalizedIngredients = (doc.Ingredients ?? [])
            .Select(ingredient => new IngredientTemplate(
                ClampAndNormalize(ingredient.Name, MaxAiIngredientNameLength),
                ClampAndNormalizeDepartmentName(ingredient.Department),
                Math.Max(0m, (decimal)ingredient.QuantityForTwo),
                ClampAndNormalize(ingredient.Unit, MaxAiUnitLength),
                Math.Max(0m, (decimal)ingredient.EstimatedCostForTwo)))
            .Where(ingredient =>
                !string.IsNullOrWhiteSpace(ingredient.Name) &&
                !string.IsNullOrWhiteSpace(ingredient.Department) &&
                !string.IsNullOrWhiteSpace(ingredient.Unit) &&
                ingredient.QuantityForTwo > 0m &&
                ingredient.EstimatedCostForTwo > 0m)
            .ToList();
        if (normalizedIngredients.Count == 0)
        {
            return null;
        }

        return new DessertAddOnTemplate(normalizedName, normalizedIngredients);
    }

    private static MealTemplate? FromFirestoreDocument(FirestoreAislePilotMeal? doc)
    {
        if (doc is null)
        {
            return null;
        }

        var payload = new AislePilotAiMealPayload
        {
            Name = doc.Name,
            BaseCostForTwo = (decimal)doc.BaseCostForTwo,
            IsQuick = doc.IsQuick,
            Tags = doc.Tags,
            Ingredients = doc.Ingredients?
                .Select(ingredient => new AislePilotAiIngredientPayload
                {
                    Name = ingredient.Name,
                    Department = ingredient.Department,
                    QuantityForTwo = (decimal)ingredient.QuantityForTwo,
                    Unit = ingredient.Unit,
                    EstimatedCostForTwo = (decimal)ingredient.EstimatedCostForTwo
                })
                .ToList(),
            RecipeSteps = doc.RecipeSteps,
            NutritionPerServing = doc.NutritionPerServingMedium is null
                ? null
                : new AislePilotAiNutritionPayload
                {
                    Calories = (decimal)doc.NutritionPerServingMedium.Calories,
                    ProteinGrams = (decimal)doc.NutritionPerServingMedium.ProteinGrams,
                    CarbsGrams = (decimal)doc.NutritionPerServingMedium.CarbsGrams,
                    FatGrams = (decimal)doc.NutritionPerServingMedium.FatGrams
                },
            ImageUrl = doc.ImageUrl
        };

        var mapped = ValidateAndMapAiMeal(
            payload,
            doc.Tags?
                .Where(mode => !string.Equals(mode, "Balanced", StringComparison.OrdinalIgnoreCase))
                .ToArray() ?? [],
            requireRecipeSteps: false,
            suitableMealTypes: doc.SuitableMealTypes,
            out _);
        if (mapped is null)
        {
            return null;
        }

        var normalizedImageUrl = NormalizeImageUrl(doc.ImageUrl);
        return string.IsNullOrWhiteSpace(normalizedImageUrl)
            ? mapped
            : mapped with { ImageUrl = normalizedImageUrl };
    }

    private static IReadOnlyList<string> BuildBudgetTips(bool isOverBudget, decimal budgetDelta, int leftoverDays)
    {
        var ukCulture = CultureInfo.GetCultureInfo("en-GB");
        var tips = new List<string>();

        if (leftoverDays > 0)
        {
            tips.Add($"{leftoverDays} day(s) are allocated to leftovers this week.");
        }

        if (isOverBudget)
        {
            var overspend = Math.Abs(budgetDelta);
            tips.Add($"Current plan is about {overspend.ToString("C", ukCulture)} over your target.");
            tips.Add("Swap 1-2 high-cost fish/meat meals for lentil or chickpea meals.");
            tips.Add("Batch-cook one recipe and reuse leftovers for lunch.");
            return tips;
        }

        if (budgetDelta >= 10m)
        {
            tips.Add($"You still have about {budgetDelta.ToString("C", ukCulture)} available.");
            tips.Add("Consider adding breakfast staples or healthy snacks.");
            tips.Add("Use the spare budget for higher-quality proteins or produce.");
            return tips;
        }

        tips.Add("Budget is on target.");
        tips.Add("If prices shift, swap one meal to keep the weekly total stable.");
        return tips;
    }

    internal static int NormalizePlanDays(int planDays)
    {
        return Math.Clamp(planDays, 1, 7);
    }

    internal static int NormalizeCookDays(int cookDays)
    {
        return NormalizeCookDays(cookDays, 7);
    }

    internal static int NormalizeCookDays(int cookDays, int planDays)
    {
        var normalizedPlanDays = NormalizePlanDays(planDays);
        return Math.Clamp(cookDays, 1, normalizedPlanDays);
    }

    private static int NormalizeMealsPerDay(int mealsPerDay)
    {
        return Math.Clamp(mealsPerDay, MinMealsPerDay, MaxMealsPerDay);
    }

    internal static int NormalizeRequestedMealCount(int requestedMealCount)
    {
        return Math.Clamp(requestedMealCount, 1, MaxPlanMealSlots);
    }

    private static IReadOnlySet<int> ParseIgnoredMealSlotIndexes(
        string? ignoredMealSlotIndexesCsv,
        int expectedMealCount)
    {
        if (string.IsNullOrWhiteSpace(ignoredMealSlotIndexesCsv))
        {
            return new HashSet<int>();
        }

        var normalizedExpectedCount = NormalizeRequestedMealCount(expectedMealCount);
        var parsed = ignoredMealSlotIndexesCsv
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                ? index
                : -1)
            .Where(index => index >= 0 && index < normalizedExpectedCount)
            .Distinct()
            .ToHashSet();

        return parsed;
    }

    internal static IReadOnlyList<string> BuildMealTypeSlots(AislePilotRequestModel request)
    {
        return NormalizeMealTypeSlots(request.SelectedMealTypes, request.MealsPerDay);
    }

    private static IReadOnlyList<string> BuildMealTypeSlots(int mealsPerDay)
    {
        var safeMealsPerDay = NormalizeMealsPerDay(mealsPerDay);
        return safeMealsPerDay switch
        {
            1 => ["Dinner"],
            2 => ["Lunch", "Dinner"],
            _ => ["Breakfast", "Lunch", "Dinner"]
        };
    }

    internal static IReadOnlyList<string> NormalizeMealTypeSlots(
        IReadOnlyList<string>? mealTypeSlots,
        int fallbackMealsPerDay)
    {
        if (mealTypeSlots is null || mealTypeSlots.Count == 0)
        {
            return BuildMealTypeSlots(fallbackMealsPerDay);
        }

        var normalized = mealTypeSlots
            .Select(NormalizeSelectedMealTypeSlot)
            .Where(type => type is not null)
            .Select(type => type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type.Equals("Breakfast", StringComparison.OrdinalIgnoreCase)
                ? 0
                : type.Equals("Lunch", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 2)
            .ToList();

        return normalized.Count == 0 ? BuildMealTypeSlots(fallbackMealsPerDay) : normalized;
    }

    private static string? NormalizeSelectedMealTypeSlot(string? mealType)
    {
        var normalized = mealType?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
        {
            return "Breakfast";
        }

        if (normalized.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "Lunch";
        }

        if (normalized.Equals("Dinner", StringComparison.OrdinalIgnoreCase))
        {
            return "Dinner";
        }

        return null;
    }

    private static string NormalizeMealType(string? mealType)
    {
        var normalized = (mealType ?? string.Empty).Trim();
        if (normalized.Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
        {
            return "Breakfast";
        }

        if (normalized.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "Lunch";
        }

        return "Dinner";
    }

    private static IReadOnlyList<string> NormalizeMealTypes(IReadOnlyList<string>? mealTypes)
    {
        if (mealTypes is null || mealTypes.Count == 0)
        {
            return ["Dinner"];
        }

        var normalized = mealTypes
            .Select(NormalizeMealType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? ["Dinner"] : normalized;
    }

    private static IReadOnlyList<string> InferSuitableMealTypesFromMealName(string mealName)
    {
        var normalizedName = mealName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return ["Dinner"];
        }

        var isBreakfastLike = IsBreakfastLikeMealName(normalizedName);
        var isLunchLike = IsLunchLikeMealName(normalizedName);

        var mealTypes = new List<string>(3);
        if (isBreakfastLike)
        {
            mealTypes.Add("Breakfast");
            mealTypes.Add("Lunch");
        }

        if (isLunchLike)
        {
            mealTypes.Add("Lunch");
        }

        if (!isBreakfastLike && !isLunchLike)
        {
            mealTypes.Add("Dinner");
        }

        return mealTypes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ResolveSuitableMealTypes(MealTemplate meal)
    {
        if (meal.SuitableMealTypes is { Count: > 0 })
        {
            return NormalizeMealTypes(meal.SuitableMealTypes);
        }

        return InferSuitableMealTypesFromMealName(meal.Name);
    }

    internal static MealTemplate EnsureMealTypeSuitability(
        MealTemplate meal,
        IReadOnlyList<string>? preferredMealTypes = null)
    {
        var normalizedPreferredMealTypes = preferredMealTypes is { Count: > 0 }
            ? NormalizeMealTypes(preferredMealTypes)
            : ResolveSuitableMealTypes(meal);
        var currentMealTypes = NormalizeMealTypes(meal.SuitableMealTypes);

        return currentMealTypes.SequenceEqual(normalizedPreferredMealTypes, StringComparer.OrdinalIgnoreCase)
            ? meal
            : meal with { SuitableMealTypes = normalizedPreferredMealTypes };
    }

    internal static bool SupportsMealType(MealTemplate meal, string mealType)
    {
        var normalizedMealType = NormalizeMealType(mealType);
        return ResolveSuitableMealTypes(meal).Contains(normalizedMealType, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBreakfastLikeMealName(string mealName)
    {
        var normalizedName = mealName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        return BreakfastNameKeywords.Any(keyword =>
            normalizedName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLunchLikeMealName(string mealName)
    {
        var normalizedName = mealName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        return LunchNameKeywords.Any(keyword =>
            normalizedName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMealNameAppropriateForSlot(string mealName, string mealType)
    {
        var normalizedMealType = NormalizeMealType(mealType);
        var isBreakfastLike = IsBreakfastLikeMealName(mealName);
        var isLunchLike = IsLunchLikeMealName(mealName);

        return normalizedMealType switch
        {
            "Breakfast" => isBreakfastLike,
            "Lunch" => isLunchLike || isBreakfastLike,
            _ => true
        };
    }

    private static IReadOnlyList<int> BuildPerMealPortionMultipliers(
        IReadOnlyList<int> dayPortionMultipliers,
        int mealsPerDay)
    {
        if (dayPortionMultipliers.Count == 0)
        {
            return [];
        }

        var safeMealsPerDay = NormalizeMealsPerDay(mealsPerDay);
        var perMeal = new List<int>(dayPortionMultipliers.Count * safeMealsPerDay);
        foreach (var dayMultiplier in dayPortionMultipliers)
        {
            var normalizedDayMultiplier = Math.Max(1, dayMultiplier);
            for (var i = 0; i < safeMealsPerDay; i++)
            {
                perMeal.Add(normalizedDayMultiplier);
            }
        }

        return perMeal;
    }

    private static IReadOnlyList<MealTemplate> NormalizeSelectedMealsForCount(
        IReadOnlyList<MealTemplate> selectedMeals,
        int expectedMealCount)
    {
        var normalizedExpectedCount = NormalizeRequestedMealCount(expectedMealCount);
        if (selectedMeals.Count == normalizedExpectedCount)
        {
            return selectedMeals;
        }

        if (selectedMeals.Count > normalizedExpectedCount)
        {
            return selectedMeals.Take(normalizedExpectedCount).ToList();
        }

        if (selectedMeals.Count == 0)
        {
            return [];
        }

        var normalized = new List<MealTemplate>(normalizedExpectedCount);
        for (var i = 0; i < normalizedExpectedCount; i++)
        {
            normalized.Add(selectedMeals[i % selectedMeals.Count]);
        }

        return normalized;
    }

    private static int RoundToNearestFiveMinutes(int minutes)
    {
        var safeMinutes = Math.Max(5, minutes);
        return (int)(Math.Round(safeMinutes / 5m, MidpointRounding.AwayFromZero) * 5m);
    }

}
