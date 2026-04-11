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

}
