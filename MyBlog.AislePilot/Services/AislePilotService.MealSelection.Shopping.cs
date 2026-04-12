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
                    return $"{QuantityDisplayFormatter.FormatForRecipe(quantity, ingredient.Unit)} {NormalizeIngredientDisplayName(ingredient.Name)}";
                })
                .ToList();
            var recipeSteps = _nutritionRecipeFallbackEngine.BuildRecipeSteps(template);
            var estimatedPrepMinutes = EstimateMealPrepMinutes(template, recipeSteps, leftoverDaysCovered);
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
                RecipeSteps = recipeSteps
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
        var aggregated = new List<MutableShoppingItem>();
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
                var aggregatedQuantity = ingredient.QuantityForTwo * householdFactor * mealPortionMultiplier;
                var (canonicalQuantity, canonicalUnit) = NormalizeShoppingListQuantityAndUnit(aggregatedQuantity, ingredient.Unit);
                var existing = FindMatchingShoppingItem(aggregated, ingredient.Name, canonicalUnit);
                if (existing is null)
                {
                    existing = new MutableShoppingItem
                    {
                        Department = ingredient.Department,
                        Name = ResolvePreferredShoppingListName(ingredient.Name),
                        Unit = canonicalUnit
                    };
                    aggregated.Add(existing);
                }
                else
                {
                    existing.Department = ResolvePreferredShoppingListDepartment(existing.Department, ingredient.Department);
                    existing.Name = ResolvePreferredShoppingListName(existing.Name, ingredient.Name);
                }

                existing.Quantity += canonicalQuantity;
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

        return aggregated
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

    private static readonly HashSet<string> ShoppingIngredientVariantQualifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "can",
        "canned",
        "tin",
        "tinned",
        "drained"
    };

    private static MutableShoppingItem? FindMatchingShoppingItem(
        IEnumerable<MutableShoppingItem> aggregated,
        string ingredientName,
        string unit)
    {
        var normalizedIngredientName = NormalizeShoppingListIngredientName(ingredientName);
        var normalizedUnit = NormalizeShoppingListUnit(unit);

        foreach (var item in aggregated)
        {
            if (!NormalizeShoppingListUnit(item.Unit).Equals(normalizedUnit, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var existingIngredientName = NormalizeShoppingListIngredientName(item.Name);
            if (AreEquivalentShoppingIngredientNames(existingIngredientName, normalizedIngredientName))
            {
                return item;
            }
        }

        return null;
    }

    private static string NormalizeShoppingListDepartment(string? department)
    {
        var normalized = NormalizePantryText(department ?? string.Empty);
        return string.IsNullOrWhiteSpace(normalized) ? "other" : normalized;
    }

    private static string NormalizeShoppingListIngredientName(string? ingredientName)
    {
        var normalized = NormalizePantryText(ingredientName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Equals("oil", StringComparison.OrdinalIgnoreCase))
        {
            return "olive oil";
        }

        foreach (var aliasEntry in IngredientAliases)
        {
            var canonicalName = NormalizePantryText(aliasEntry.Key);
            if (normalized.Equals(canonicalName, StringComparison.OrdinalIgnoreCase))
            {
                return canonicalName;
            }

            foreach (var alias in aliasEntry.Value)
            {
                var normalizedAlias = NormalizePantryText(alias);
                if (normalized.Equals(normalizedAlias, StringComparison.OrdinalIgnoreCase))
                {
                    return canonicalName;
                }
            }
        }

        return normalized;
    }

    private static (decimal Quantity, string Unit) NormalizeShoppingListQuantityAndUnit(decimal quantity, string? unit)
    {
        var normalizedUnit = NormalizeShoppingListUnit(unit);
        if (QuantityDisplayFormatter.TryConvertToMillilitres(quantity, normalizedUnit, out var totalMillilitres))
        {
            return (totalMillilitres, "ml");
        }

        return (quantity, normalizedUnit);
    }

    private static string NormalizeShoppingListUnit(string? unit)
    {
        var normalized = NormalizeAiUnitForPricing(unit);
        return normalized switch
        {
            "can" or "cans" or "tin" or "tins" => "tin",
            "gram" or "grams" or "g" => "g",
            "kilogram" or "kilograms" or "kg" or "kgs" => "kg",
            "milliliter" or "milliliters" or "millilitre" or "millilitres" or "ml" => "ml",
            "liter" or "liters" or "litre" or "litres" or "l" => "l",
            "piece" or "pieces" or "pc" or "pcs" => "pcs",
            _ => normalized
        };
    }

    private static bool AreEquivalentShoppingIngredientNames(string leftNormalizedName, string rightNormalizedName)
    {
        if (leftNormalizedName.Equals(rightNormalizedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(leftNormalizedName) || string.IsNullOrWhiteSpace(rightNormalizedName))
        {
            return false;
        }

        var leftWords = leftNormalizedName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightWords = rightNormalizedName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var leftExtras = leftWords
            .Where(word => !rightWords.Contains(word))
            .ToList();
        var rightExtras = rightWords
            .Where(word => !leftWords.Contains(word))
            .ToList();

        var leftExtraWordsAreQualifiers = leftExtras.Count == 0 ||
                                          leftExtras.All(word => ShoppingIngredientVariantQualifiers.Contains(word));
        var rightExtraWordsAreQualifiers = rightExtras.Count == 0 ||
                                           rightExtras.All(word => ShoppingIngredientVariantQualifiers.Contains(word));

        return leftExtraWordsAreQualifiers &&
               rightExtraWordsAreQualifiers &&
               leftWords.Overlaps(rightWords);
    }

    private static void AddDessertAddOnShoppingItems(
        IList<MutableShoppingItem> aggregated,
        decimal householdFactor,
        DessertAddOnTemplate dessertAddOnTemplate)
    {
        var scale = Math.Clamp(householdFactor, 0.5m, 4m);
        foreach (var ingredient in dessertAddOnTemplate.Ingredients)
        {
            var aggregatedQuantity = ingredient.QuantityForTwo * scale;
            var (canonicalQuantity, canonicalUnit) = NormalizeShoppingListQuantityAndUnit(aggregatedQuantity, ingredient.Unit);
            var existing = FindMatchingShoppingItem(aggregated, ingredient.Name, canonicalUnit);
            if (existing is null)
            {
                existing = new MutableShoppingItem
                {
                    Department = ingredient.Department,
                    Name = ResolvePreferredShoppingListName(ingredient.Name),
                    Unit = canonicalUnit
                };
                aggregated.Add(existing);
            }
            else
            {
                existing.Department = ResolvePreferredShoppingListDepartment(existing.Department, ingredient.Department);
                existing.Name = ResolvePreferredShoppingListName(existing.Name, ingredient.Name);
            }

            existing.Quantity += canonicalQuantity;
            existing.EstimatedCost += ingredient.EstimatedCostForTwo * scale;
        }
    }

    private static string ResolvePreferredShoppingListDepartment(string currentDepartment, string candidateDepartment)
    {
        var currentNormalized = NormalizeShoppingListDepartment(currentDepartment);
        var candidateNormalized = NormalizeShoppingListDepartment(candidateDepartment);
        if (currentNormalized.Equals(candidateNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return currentDepartment;
        }

        if (currentNormalized.Equals("other", StringComparison.OrdinalIgnoreCase) &&
            !candidateNormalized.Equals("other", StringComparison.OrdinalIgnoreCase))
        {
            return candidateDepartment;
        }

        return currentDepartment;
    }

    private static string ResolvePreferredShoppingListName(string ingredientName, string? candidateName = null)
    {
        var currentName = NormalizeIngredientDisplayName(ingredientName);
        var incomingName = NormalizeIngredientDisplayName(candidateName);
        if (string.IsNullOrWhiteSpace(incomingName))
        {
            return currentName;
        }

        var currentNormalized = NormalizeShoppingListIngredientName(currentName);
        var incomingNormalized = NormalizeShoppingListIngredientName(incomingName);
        if (!currentNormalized.Equals(incomingNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return currentName;
        }

        if (currentNormalized.Equals("olive oil", StringComparison.OrdinalIgnoreCase))
        {
            return "Olive oil";
        }

        if (currentName.Length >= incomingName.Length)
        {
            return currentName;
        }

        return incomingName;
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
                return $"{QuantityDisplayFormatter.FormatForRecipe(scaledQuantity, ingredient.Unit)} {NormalizeIngredientDisplayName(ingredient.Name)}";
            })
            .ToList();
    }

    private static string NormalizeIngredientDisplayName(string? ingredientName)
    {
        if (string.IsNullOrWhiteSpace(ingredientName))
        {
            return string.Empty;
        }

        var trimmed = Regex.Replace(ingredientName.Trim(), "\\s+", " ");
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var lowered = trimmed.ToLowerInvariant();
        var chars = lowered.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetter(chars[i]))
            {
                continue;
            }

            chars[i] = char.ToUpperInvariant(chars[i]);
            break;
        }

        return new string(chars);
    }
}
