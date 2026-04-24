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
    private static IReadOnlyList<MealTemplate>? ValidateAndMapAiMeals(
        AislePilotAiPlanPayload? payload,
        IReadOnlyList<string> dietaryModes,
        int cookDays,
        int requestedMealCount,
        int mealsPerDay,
        IReadOnlyList<string>? mealTypeSlots,
        bool requireSpecialTreatDinner,
        out string? validationReason)
    {
        validationReason = null;
        var rawMeals = payload?.Meals;
        if (rawMeals is null || rawMeals.Count < cookDays || rawMeals.Count > requestedMealCount)
        {
            validationReason = $"meal_count_out_of_range(count={rawMeals?.Count ?? 0},min={cookDays},max={requestedMealCount})";
            return null;
        }

        var strictModes = ResolveHardDietaryModes(dietaryModes);
        var meals = new List<MealTemplate>(cookDays);
        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, mealsPerDay);
        var slotTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rawMeals.Count; i++)
        {
            var slotMealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
            slotTypeCounts[slotMealType] = slotTypeCounts.GetValueOrDefault(slotMealType, 0) + 1;
        }

        var slotTypeMealNameCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < rawMeals.Count; i++)
        {
            var rawMeal = rawMeals[i];
            var mealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
            var meal = ValidateAndMapAiMeal(
                rawMeal,
                strictModes,
                requireRecipeSteps: true,
                suitableMealTypes: [mealType],
                out var mealReason);
            if (meal is null)
            {
                validationReason = $"invalid_meal_at_index_{i}:{mealReason ?? "unknown"}";
                return null;
            }

            if (!IsMealNameAppropriateForSlot(meal.Name, mealType))
            {
                validationReason = $"invalid_meal_at_index_{i}:slot_name_mismatch(expected={mealType})";
                return null;
            }

            if (!slotTypeMealNameCounts.TryGetValue(mealType, out var mealNameCounts))
            {
                mealNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                slotTypeMealNameCounts[mealType] = mealNameCounts;
            }

            var nextCount = mealNameCounts.GetValueOrDefault(meal.Name, 0) + 1;
            mealNameCounts[meal.Name] = nextCount;

            var slotCount = slotTypeCounts.GetValueOrDefault(mealType, 0);
            var maxRepeats = ResolveMaxMealRepeatsForSlotType(mealType, slotCount);
            if (nextCount > maxRepeats)
            {
                validationReason = $"repeat_cap_exceeded(meal={meal.Name},slot={mealType},count={nextCount},max={maxRepeats})";
                return null;
            }

            meals.Add(meal);
        }

        if (requireSpecialTreatDinner)
        {
            var hasDinnerSlot = resolvedMealTypeSlots
                .Any(slot => slot.Equals("Dinner", StringComparison.OrdinalIgnoreCase));
            if (!hasDinnerSlot)
            {
                validationReason = "special_treat_requires_dinner_slot";
                return null;
            }

            if (!HasSpecialTreatDinner(meals, resolvedMealTypeSlots))
            {
                validationReason = "special_treat_not_found";
                return null;
            }
        }

        return meals;
    }

    private static int GetRequestedAiMealCount(int requestedMealCount)
    {
        var normalizedMealCount = Math.Clamp(requestedMealCount, 1, MaxFreshAiPlanMeals);
        return normalizedMealCount switch
        {
            >= 7 => normalizedMealCount,
            >= 5 => Math.Min(MaxFreshAiPlanMeals, normalizedMealCount + 1),
            _ => Math.Min(MaxFreshAiPlanMeals, normalizedMealCount + 2)
        };
    }

    private static bool ShouldRetryWithCompactPayload(int requestedMealCount)
    {
        var normalizedMealCount = Math.Clamp(requestedMealCount, 1, MaxFreshAiPlanMeals);
        return normalizedMealCount <= 5;
    }

    private static IReadOnlyList<IngredientUnitPriceReference> BuildIngredientUnitPriceReferences()
    {
        var references = new List<IngredientUnitPriceReference>();
        foreach (var meal in MealTemplates)
        {
            foreach (var ingredient in meal.Ingredients)
            {
                if (ingredient.QuantityForTwo <= 0m || ingredient.EstimatedCostForTwo <= 0m)
                {
                    continue;
                }

                var normalizedName = NormalizePantryText(ingredient.Name);
                var normalizedUnit = NormalizeAiUnitForPricing(ingredient.Unit);
                if (string.IsNullOrWhiteSpace(normalizedName) || string.IsNullOrWhiteSpace(normalizedUnit))
                {
                    continue;
                }

                var unitPrice = ingredient.EstimatedCostForTwo / ingredient.QuantityForTwo;
                references.Add(new IngredientUnitPriceReference(
                    normalizedName,
                    normalizedUnit,
                    decimal.Round(unitPrice, 4, MidpointRounding.AwayFromZero)));
            }
        }

        return references;
    }

    private static decimal NormalizeAiIngredientEstimatedCost(
        string ingredientName,
        string unit,
        decimal quantityForTwo,
        decimal estimatedCostForTwo)
    {
        if (quantityForTwo <= 0m || estimatedCostForTwo <= 0m)
        {
            return decimal.Round(estimatedCostForTwo, 2, MidpointRounding.AwayFromZero);
        }

        var normalizedUnit = NormalizeAiUnitForPricing(unit);
        var adjustedUnitPrice = estimatedCostForTwo / quantityForTwo;
        var genericMaxUnitPrice = ResolveGenericAiIngredientMaxUnitPrice(normalizedUnit);
        if (adjustedUnitPrice > genericMaxUnitPrice)
        {
            adjustedUnitPrice = genericMaxUnitPrice;
        }

        if (TryGetTemplateIngredientUnitPriceBounds(ingredientName, normalizedUnit, out var minKnownUnitPrice, out var maxKnownUnitPrice))
        {
            var lowerBound = minKnownUnitPrice * AiIngredientKnownUnitPriceMinFactor;
            var upperBound = maxKnownUnitPrice * AiIngredientKnownUnitPriceMaxFactor;
            adjustedUnitPrice = Math.Clamp(adjustedUnitPrice, lowerBound, upperBound);
        }

        var adjustedCost = adjustedUnitPrice * quantityForTwo;
        return decimal.Round(adjustedCost, 2, MidpointRounding.AwayFromZero);
    }

    private static bool IsAiLowVolumeSeasoningIngredient(
        string ingredientName,
        string department,
        string unit,
        decimal quantityForTwo)
    {
        if (!department.Equals("Spices & Sauces", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedUnit = NormalizeAiUnitForPricing(unit);
        if (normalizedUnit.Equals("g", StringComparison.OrdinalIgnoreCase) && quantityForTwo > 0m && quantityForTwo <= 8m)
        {
            return true;
        }

        return IsMinorPantryAssumptionIngredient(ingredientName);
    }

    private static decimal NormalizeAiLowVolumeSeasoningEstimatedCost(
        string unit,
        decimal quantityForTwo,
        decimal estimatedCostForTwo)
    {
        var normalizedUnit = NormalizeAiUnitForPricing(unit);
        if (normalizedUnit.Equals("g", StringComparison.OrdinalIgnoreCase) && quantityForTwo > 0m)
        {
            var maxReasonableCost = Math.Max(0.01m, quantityForTwo * ResolveGenericAiIngredientMaxUnitPrice(normalizedUnit));
            var minReasonableCost = 0.01m;
            var seedCost = estimatedCostForTwo > 0m ? estimatedCostForTwo : minReasonableCost;
            return Math.Min(maxReasonableCost, Math.Max(minReasonableCost, seedCost));
        }

        return Math.Max(0.01m, estimatedCostForTwo);
    }

    private static bool TryGetTemplateIngredientUnitPriceBounds(
        string ingredientName,
        string normalizedUnit,
        out decimal minKnownUnitPrice,
        out decimal maxKnownUnitPrice)
    {
        minKnownUnitPrice = 0m;
        maxKnownUnitPrice = 0m;
        if (string.IsNullOrWhiteSpace(ingredientName) || string.IsNullOrWhiteSpace(normalizedUnit))
        {
            return false;
        }

        var normalizedIngredientName = NormalizePantryText(ingredientName);
        if (string.IsNullOrWhiteSpace(normalizedIngredientName))
        {
            return false;
        }

        var matchedReferences = IngredientUnitPriceReferences
            .Where(reference =>
                reference.UnitNormalized.Equals(normalizedUnit, StringComparison.OrdinalIgnoreCase) &&
                (reference.IngredientNameNormalized.Contains(normalizedIngredientName, StringComparison.OrdinalIgnoreCase) ||
                 normalizedIngredientName.Contains(reference.IngredientNameNormalized, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (matchedReferences.Count == 0)
        {
            return false;
        }

        minKnownUnitPrice = matchedReferences.Min(reference => reference.UnitPrice);
        maxKnownUnitPrice = matchedReferences.Max(reference => reference.UnitPrice);
        return true;
    }

    private static decimal NormalizeAiMealBaseCost(decimal baseCostForTwo, decimal ingredientCostForTwo)
    {
        if (ingredientCostForTwo <= 0m)
        {
            return decimal.Round(baseCostForTwo, 2, MidpointRounding.AwayFromZero);
        }

        var lowerBound = ingredientCostForTwo * AiMealBaseCostMinToIngredientFactor;
        var upperBound = ingredientCostForTwo * AiMealBaseCostMaxToIngredientFactor;
        return decimal.Round(
            Math.Clamp(baseCostForTwo, lowerBound, upperBound),
            2,
            MidpointRounding.AwayFromZero);
    }

    private static string NormalizeAiUnitForPricing(string? unit)
    {
        return (unit ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static decimal ResolveGenericAiIngredientMaxUnitPrice(string normalizedUnit)
    {
        return normalizedUnit switch
        {
            "kg" => 24m,
            "g" => 0.024m,
            "l" => 8m,
            "ml" => 0.008m,
            "pcs" => 1.20m,
            "slice" or "slices" => 0.50m,
            "tin" or "tins" => 2.80m,
            "pack" or "packs" => 4m,
            "jar" or "jars" => 4.50m,
            "bottle" or "bottles" => 4.50m,
            "head" => 2.50m,
            "fillets" => 5.50m,
            "balls" => 2.20m,
            _ => 6m
        };
    }

    private static MealTemplate? ValidateAndMapAiMeal(
        AislePilotAiMealPayload? payload,
        IReadOnlyList<string> strictModes,
        bool requireRecipeSteps,
        out string? validationReason)
    {
        return ValidateAndMapAiMeal(payload, strictModes, requireRecipeSteps, suitableMealTypes: null, out validationReason);
    }

    private static MealTemplate? ValidateAndMapAiMeal(
        AislePilotAiMealPayload? payload,
        IReadOnlyList<string> strictModes,
        bool requireRecipeSteps,
        IReadOnlyList<string>? suitableMealTypes,
        out string? validationReason)
    {
        validationReason = null;
        if (payload is null)
        {
            validationReason = "meal_payload_null";
            return null;
        }

        var name = ClampAndNormalize(payload.Name, MaxAiMealNameLength);
        if (string.IsNullOrWhiteSpace(name))
        {
            validationReason = "meal_name_missing";
            return null;
        }

        var baseCostForTwo = payload.BaseCostForTwo ?? 0m;
        if (baseCostForTwo <= 0m || baseCostForTwo > 30m)
        {
            validationReason = $"meal_cost_invalid:{baseCostForTwo.ToString(CultureInfo.InvariantCulture)}";
            return null;
        }

        var ingredients = payload.Ingredients?
            .Select((ingredient, index) =>
            {
                var mapped = ValidateAndMapAiIngredient(ingredient, out var ingredientReason);
                return new
                {
                    mapped,
                    ingredientReason,
                    index
                };
            })
            .ToList();

        if (ingredients is null || ingredients.Count < 3 || ingredients.Count > 7)
        {
            validationReason = $"ingredient_count_invalid:{ingredients?.Count ?? 0}";
            return null;
        }

        var invalidIngredient = ingredients.FirstOrDefault(item => item.mapped is null);
        if (invalidIngredient is not null)
        {
            validationReason = $"invalid_ingredient_at_index_{invalidIngredient.index}:{invalidIngredient.ingredientReason ?? "unknown"}";
            return null;
        }

        var ingredientCostForTwo = decimal.Round(
            ingredients.Sum(item => item.mapped!.EstimatedCostForTwo),
            2,
            MidpointRounding.AwayFromZero);
        var normalizedBaseCostForTwo = NormalizeAiMealBaseCost(baseCostForTwo, ingredientCostForTwo);
        if (!IsAiMealCostProfileReasonable(normalizedBaseCostForTwo, ingredientCostForTwo, out var mealCostReason))
        {
            validationReason = $"meal_cost_profile_invalid:{mealCostReason ?? "unknown"}";
            return null;
        }

        var tags = payload.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => SupportedAiMealTags.FirstOrDefault(mode => mode.Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList() ?? [];

        if (tags.Count == 0)
        {
            tags.Add("Balanced");
        }

        if (!strictModes.All(mode => tags.Contains(mode, StringComparer.OrdinalIgnoreCase)))
        {
            validationReason = "strict_modes_not_satisfied";
            return null;
        }

        var recipeSteps = CleanAiRecipeSteps(payload.RecipeSteps);
        if (requireRecipeSteps && recipeSteps.Count < 5)
        {
            validationReason = $"recipe_steps_invalid:{recipeSteps.Count}";
            return null;
        }

        var mappedIngredients = ingredients.Select(item => item.mapped!).ToList();
        if (HasMealNameIngredientAnchorMismatch(name, mappedIngredients, out var ingredientAnchorReason))
        {
            validationReason = ingredientAnchorReason;
            return null;
        }

        AiMealNutritionEstimate? aiNutritionPerServingMedium = null;
        if (payload.NutritionPerServing is not null)
        {
            TryValidateAiNutritionPerServing(
                payload.NutritionPerServing,
                out aiNutritionPerServingMedium,
                out _);
        }

        return new MealTemplate(
            name,
            normalizedBaseCostForTwo,
            payload.IsQuick ?? false,
            tags,
            mappedIngredients)
        {
            AiRecipeSteps = recipeSteps.Count == 0 ? null : recipeSteps,
            AiNutritionPerServingMedium = aiNutritionPerServingMedium,
            ImageUrl = NormalizeImageUrl(payload.ImageUrl),
            SuitableMealTypes = NormalizeMealTypes(suitableMealTypes)
        };
    }

    private static bool HasMealNameIngredientAnchorMismatch(
        string mealName,
        IReadOnlyList<IngredientTemplate> ingredients,
        out string? validationReason)
    {
        validationReason = null;
        if (string.IsNullOrWhiteSpace(mealName) || ingredients.Count == 0)
        {
            return false;
        }

        var ingredientTerms = ingredients
            .SelectMany(ingredient => BuildIngredientSearchTerms(ingredient.Name))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var anchor in MealNameRequiredIngredientAnchors)
        {
            if (!ContainsWholeWord(mealName, anchor.Key))
            {
                continue;
            }

            var hasMatchingIngredient = ingredientTerms.Any(term =>
                anchor.Value.Any(expected => ContainsWholeWord(term, expected)));
            if (hasMatchingIngredient)
            {
                continue;
            }

            validationReason = $"name_ingredient_anchor_missing(anchor={anchor.Key})";
            return true;
        }

        return false;
    }

    private static bool ContainsWholeWord(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            $@"\b{Regex.Escape(token)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IngredientTemplate? ValidateAndMapAiIngredient(
        AislePilotAiIngredientPayload? payload,
        out string? validationReason)
    {
        validationReason = null;
        if (payload is null)
        {
            validationReason = "ingredient_payload_null";
            return null;
        }

        var name = ClampAndNormalize(payload.Name, MaxAiIngredientNameLength);
        var department = NormalizeAiDepartment(payload.Department);
        var unit = ClampAndNormalize(payload.Unit, MaxAiUnitLength);
        var quantityForTwo = payload.QuantityForTwo ?? 0m;
        var estimatedCostForTwo = payload.EstimatedCostForTwo ?? 0m;
        var normalizedEstimatedCostForTwo = NormalizeAiIngredientEstimatedCost(
            name,
            unit,
            quantityForTwo,
            estimatedCostForTwo);
        if (IsAiLowVolumeSeasoningIngredient(name, department, unit, quantityForTwo))
        {
            normalizedEstimatedCostForTwo = NormalizeAiLowVolumeSeasoningEstimatedCost(
                unit,
                quantityForTwo,
                normalizedEstimatedCostForTwo);
        }

        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(department) ||
            string.IsNullOrWhiteSpace(unit) ||
            quantityForTwo <= 0m ||
            !IsAiIngredientQuantityReasonable(unit, quantityForTwo) ||
            normalizedEstimatedCostForTwo <= 0m ||
            normalizedEstimatedCostForTwo > 20m ||
            !IsAiIngredientPriceReasonable(unit, quantityForTwo, normalizedEstimatedCostForTwo, out _))
        {
            validationReason =
                $"ingredient_fields_invalid(name='{name}',department='{department}',unit='{unit}',qty={quantityForTwo.ToString(CultureInfo.InvariantCulture)},cost={estimatedCostForTwo.ToString(CultureInfo.InvariantCulture)})";
            return null;
        }

        return new IngredientTemplate(
            name,
            department,
            decimal.Round(quantityForTwo, 2, MidpointRounding.AwayFromZero),
            unit,
            decimal.Round(normalizedEstimatedCostForTwo, 2, MidpointRounding.AwayFromZero));
    }

    private static bool IsAiIngredientQuantityReasonable(string unit, decimal quantity)
    {
        var normalizedUnit = NormalizeAiUnitForPricing(unit);
        var max = normalizedUnit switch
        {
            "g" => 5000m,
            "ml" => 5000m,
            "kg" => 15m,
            "l" => 10m,
            "pcs" => 60m,
            "slice" => 40m,
            "slices" => 40m,
            "tins" => 24m,
            "tin" => 24m,
            "pack" => 20m,
            "packs" => 20m,
            "bottle" => 12m,
            "bottles" => 12m,
            "jar" => 12m,
            "jars" => 12m,
            "head" => 12m,
            "fillets" => 20m,
            _ => 100m
        };

        return quantity <= max;
    }

    private static bool IsAiIngredientPriceReasonable(
        string unit,
        decimal quantityForTwo,
        decimal estimatedCostForTwo,
        out string? validationReason)
    {
        validationReason = null;
        if (quantityForTwo <= 0m || estimatedCostForTwo <= 0m)
        {
            validationReason = "non_positive_quantity_or_cost";
            return false;
        }

        var normalizedUnit = NormalizeAiUnitForPricing(unit);
        var unitPrice = estimatedCostForTwo / quantityForTwo;
        var minUnitPrice = normalizedUnit switch
        {
            "kg" => 0.65m,
            "g" => 0.00065m,
            "l" => 0.75m,
            "ml" => 0.00075m,
            "pcs" => 0.05m,
            "slice" or "slices" => 0.04m,
            "tin" or "tins" => 0.40m,
            "pack" or "packs" => 0.50m,
            "jar" or "jars" => 0.65m,
            "bottle" or "bottles" => 0.70m,
            "head" => 0.55m,
            "fillets" => 0.70m,
            _ => 0.03m
        };

        if (unitPrice < minUnitPrice)
        {
            validationReason = $"unit_price_too_low(unit={normalizedUnit},unit_price={unitPrice.ToString("0.####", CultureInfo.InvariantCulture)},min={minUnitPrice.ToString("0.####", CultureInfo.InvariantCulture)})";
            return false;
        }

        var maxUnitPrice = ResolveGenericAiIngredientMaxUnitPrice(normalizedUnit);
        if (unitPrice > maxUnitPrice)
        {
            validationReason = $"unit_price_too_high(unit={normalizedUnit},unit_price={unitPrice.ToString("0.####", CultureInfo.InvariantCulture)},max={maxUnitPrice.ToString("0.####", CultureInfo.InvariantCulture)})";
            return false;
        }

        return true;
    }

    private static bool IsAiMealCostProfileReasonable(
        decimal baseCostForTwo,
        decimal ingredientCostForTwo,
        out string? validationReason)
    {
        validationReason = null;
        if (ingredientCostForTwo <= 0m)
        {
            validationReason = "ingredient_cost_sum_non_positive";
            return false;
        }

        var ratio = baseCostForTwo / ingredientCostForTwo;
        if (ratio < 0.8m || ratio > 2.5m)
        {
            validationReason = $"base_to_ingredient_ratio_out_of_range(ratio={ratio.ToString("0.##", CultureInfo.InvariantCulture)},base={baseCostForTwo.ToString("0.##", CultureInfo.InvariantCulture)},ingredients={ingredientCostForTwo.ToString("0.##", CultureInfo.InvariantCulture)})";
            return false;
        }

        return true;
    }

    private static bool TryValidateAiNutritionPerServing(
        AislePilotAiNutritionPayload payload,
        out AiMealNutritionEstimate? estimate,
        out string? validationReason)
    {
        estimate = null;
        validationReason = null;
        var calories = payload.Calories ?? 0m;
        var protein = payload.ProteinGrams ?? 0m;
        var carbs = payload.CarbsGrams ?? 0m;
        var fat = payload.FatGrams ?? 0m;

        if (calories < 150m || calories > 1500m)
        {
            validationReason = "nutrition_calories_out_of_range";
            return false;
        }

        if (protein <= 0m || carbs <= 0m || fat <= 0m ||
            protein > 120m || carbs > 190m || fat > 110m)
        {
            validationReason = "nutrition_macros_out_of_range";
            return false;
        }

        var caloriesFromMacros = (protein * 4m) + (carbs * 4m) + (fat * 9m);
        if (caloriesFromMacros <= 0m)
        {
            validationReason = "nutrition_macro_calories_invalid";
            return false;
        }

        var consistencyRatio = calories / caloriesFromMacros;
        if (consistencyRatio < 0.75m || consistencyRatio > 1.35m)
        {
            validationReason = "nutrition_calorie_macro_mismatch";
            return false;
        }

        var consistencyScore = 1m - Math.Min(1m, Math.Abs(1m - consistencyRatio) * 2m);
        var confidence = Math.Clamp(0.40m + (consistencyScore * 0.45m), 0.40m, 0.85m);
        estimate = new AiMealNutritionEstimate
        {
            CaloriesPerServingMedium = (int)Math.Round(calories, MidpointRounding.AwayFromZero),
            ProteinGramsPerServingMedium = decimal.Round(protein, 1, MidpointRounding.AwayFromZero),
            CarbsGramsPerServingMedium = decimal.Round(carbs, 1, MidpointRounding.AwayFromZero),
            FatGramsPerServingMedium = decimal.Round(fat, 1, MidpointRounding.AwayFromZero),
            ConfidenceScore = confidence
        };

        return true;
    }

    private static IReadOnlyList<string> CleanAiRecipeSteps(IReadOnlyList<string>? recipeSteps)
    {
        return recipeSteps?
            .Select(step => ClampAndNormalize(step, MaxAiRecipeStepLength))
            .Where(step => !string.IsNullOrWhiteSpace(step) && step.Length >= 12)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList() ?? [];
    }

}
