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
    private static IReadOnlyList<WarmupProfileWithTarget> BuildWarmupProfiles(
        int minPerSingleMode,
        int minPerKeyPair)
    {
        var profiles = new List<WarmupProfileWithTarget>(WarmupProfilesSingleMode.Length + WarmupProfilesKeyPairs.Length);
        if (minPerSingleMode > 0)
        {
            profiles.AddRange(WarmupProfilesSingleMode.Select(profile =>
                new WarmupProfileWithTarget(profile.Name, profile.Modes, minPerSingleMode)));
        }

        if (minPerKeyPair > 0)
        {
            profiles.AddRange(WarmupProfilesKeyPairs.Select(profile =>
                new WarmupProfileWithTarget(profile.Name, profile.Modes, minPerKeyPair)));
        }

        return profiles;
    }

    private static IReadOnlyList<AislePilotWarmupCoverageViewModel> BuildWarmupCoverage(
        IReadOnlyList<WarmupProfileWithTarget> profiles)
    {
        return profiles
            .Select(profile =>
            {
                var count = GetCompatibleAiPoolMeals(profile.Modes, string.Empty).Count;
                return new AislePilotWarmupCoverageViewModel
                {
                    Profile = profile.Name,
                    Modes = profile.Modes.ToArray(),
                    Target = profile.Target,
                    Count = count,
                    Deficit = Math.Max(0, profile.Target - count)
                };
            })
            .ToList();
    }

    private async Task<MealTemplate?> TryGenerateSpecialTreatMealWithAiAsync(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<string> excludedMealNames,
        CancellationToken cancellationToken)
    {
        if (!_enableAiGeneration || _httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        if (!mealTypeSlots.Any(slot => slot.Equals("Dinner", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var strictModes = ResolveHardDietaryModes(context.DietaryModes);
        var prompt = BuildAiSpecialTreatMealPrompt(request, context, mealTypeSlots.Count, excludedMealNames);
        var requestBody = new
        {
            model = _model,
            temperature = 0.85,
            max_tokens = SpecialTreatMealMaxTokens,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You generate one indulgent special-treat dinner for a UK grocery-planning app. Always return valid JSON only. Use UK English."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        var responseContent = await SendOpenAiRequestWithRetryAsync(requestBody, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
        var rawJson = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        var normalizedJson = NormalizeModelJson(rawJson);
        if (!TryParseAiMealPayloadWithRecovery(normalizedJson, out var aiPayload))
        {
            return null;
        }

        var specialTreatMeal = ValidateAndMapAiMeal(
            aiPayload,
            strictModes,
            requireRecipeSteps: true,
            suitableMealTypes: ["Dinner"],
            out _);
        if (specialTreatMeal is null)
        {
            return null;
        }

        if (excludedMealNames.Contains(specialTreatMeal.Name, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!IsSpecialTreatMealCandidate(specialTreatMeal))
        {
            return null;
        }

        return MarkMealAsSpecialTreat(specialTreatMeal);
    }

    private async Task<MealTemplate?> TryGenerateWarmupMealWithAiAsync(
        IReadOnlyList<string> dietaryModes,
        IReadOnlyList<string> excludedMealNames,
        CancellationToken cancellationToken)
    {
        if (!_enableAiGeneration || _httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var strictModes = dietaryModes
            .Where(mode => !mode.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (strictModes.Length == 0)
        {
            return null;
        }

        var prompt = BuildAiWarmupMealPrompt(strictModes, excludedMealNames);
        var requestBody = new
        {
            model = _model,
            temperature = 0.75,
            max_tokens = WarmupMealMaxTokens,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You generate one practical dinner for a UK grocery-planning app. Always return valid JSON only. Use UK English."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        var responseContent = await SendOpenAiRequestWithRetryAsync(requestBody, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
        var rawJson = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        var normalizedJson = NormalizeModelJson(rawJson);
        if (!TryParseAiMealPayloadWithRecovery(normalizedJson, out var aiPayload))
        {
            return null;
        }

        var meal = ValidateAndMapAiMeal(aiPayload, strictModes, requireRecipeSteps: true, out _);
        if (meal is null)
        {
            return null;
        }

        return excludedMealNames.Contains(meal.Name, StringComparer.OrdinalIgnoreCase)
            ? null
            : meal;
    }

    private static string BuildAiSpecialTreatMealPrompt(
        AislePilotRequestModel request,
        PlanContext context,
        int mealsPerDay,
        IReadOnlyList<string> excludedMealNames)
    {
        var strictModes = context.DietaryModes
            .Where(mode => !mode.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var strictModeText = strictModes.Length == 0 ? "Balanced" : string.Join(", ", strictModes);
        var dislikesText = string.IsNullOrWhiteSpace(context.DislikesOrAllergens) ? "none" : context.DislikesOrAllergens;
        var excludedText = excludedMealNames.Count == 0 ? "none" : string.Join(", ", excludedMealNames);
        var targetMealBudget = decimal.Round(
            request.WeeklyBudget / (7m * Math.Max(1, mealsPerDay)),
            2,
            MidpointRounding.AwayFromZero);

        return $$"""
Generate one indulgent dinner for a UK grocery-planning app.

Planner inputs:
- Supermarket: {{context.Supermarket}}
- Target meal budget: {{targetMealBudget.ToString("0.##", CultureInfo.InvariantCulture)}} GBP for serving 2
- Household size: {{request.HouseholdSize}}
- Portion size: {{context.PortionSize}}
- Dietary requirements: {{strictModeText}}
- Dislikes or allergens: {{dislikesText}}
- Avoid these meal names: {{excludedText}}

Rules:
- Return exactly one dinner object.
- It must be clearly indulgent and feel like a special occasion dinner, not a normal weekday dinner.
- It must be different from every excluded meal name.
- Use UK English.
- Keep it realistic for a UK supermarket shop.
- Use typical UK non-promo shelf prices (no loyalty-only offers, markdowns, or extreme bulk discounts).
- Respect dietary requirements and dislikes/allergens strictly.
- Every meal must include 3-7 ingredients only.
- Department must be one of: Produce, Bakery, Meat & Fish, Dairy & Eggs, Frozen, Tins & Dry Goods, Spices & Sauces, Snacks, Drinks, Household, Other
- Unit should be short plain text such as kg, g, pcs, tins, jar, bottle, pack, head, fillets.
- `baseCostForTwo` is an estimated GBP cost for serving 2 people once.
- `estimatedCostForTwo` is the portion of the meal cost attributable to that ingredient for serving 2 people once.
- Use realistic prices, avoid placeholder values, and keep all monetary values to 2 decimal places.
- The sum of `estimatedCostForTwo` across ingredients should be broadly consistent with `baseCostForTwo`.
- `quantityForTwo` must be a positive number.
- `tags` must only use values from: Balanced, High-Protein, Vegetarian, Vegan, Pescatarian, Gluten-Free, Special Treat
- Include all requested dietary modes in `tags` (except Balanced is optional) and include `Special Treat` in `tags`.
- `recipeSteps` must contain 5-6 concrete, meal-specific cooking steps in order.
- Include `nutritionPerServing` for one medium serving (not household total), with calories and grams for protein/carbs/fat.

Return JSON only with this schema:
{
  "name": "",
  "baseCostForTwo": 0,
  "isQuick": false,
  "tags": ["Special Treat"],
  "recipeSteps": [
    "",
    "",
    "",
    "",
    ""
  ],
  "nutritionPerServing": {
    "calories": 0,
    "proteinGrams": 0,
    "carbsGrams": 0,
    "fatGrams": 0
  },
  "ingredients": [
    {
      "name": "",
      "department": "",
      "quantityForTwo": 0,
      "unit": "",
      "estimatedCostForTwo": 0
    }
  ]
}
""";
    }

    private static string BuildAiWarmupMealPrompt(
        IReadOnlyList<string> strictModes,
        IReadOnlyList<string> excludedMealNames)
    {
        var strictModeText = string.Join(", ", strictModes);
        var excludedText = excludedMealNames.Count == 0
            ? "none"
            : string.Join(", ", excludedMealNames);

        return $$"""
Generate one dinner for a UK grocery-planning app to improve future cache coverage.

Planner inputs:
- Dietary requirements: {{strictModeText}}
- Avoid these meal names: {{excludedText}}
- Target budget: 4.5 to 9.5 GBP for serving 2

Rules:
- Return exactly one dinner object.
- It must be different from all excluded meal names.
- Use UK English.
- Keep it realistic for a UK supermarket shop.
- Use typical UK non-promo shelf prices (no loyalty-only offers, markdowns, or extreme bulk discounts).
- Respect dietary requirements strictly.
- Keep it practical for a weekday dinner.
- Every meal must include 3-7 ingredients only.
- Department must be one of: Produce, Bakery, Meat & Fish, Dairy & Eggs, Frozen, Tins & Dry Goods, Spices & Sauces, Snacks, Drinks, Household, Other
- Unit should be short plain text such as kg, g, pcs, tins, jar, bottle, pack, head, fillets.
- `baseCostForTwo` is an estimated GBP cost for serving 2 people once.
- `estimatedCostForTwo` is the portion of the meal cost attributable to that ingredient for serving 2 people once.
- Use realistic prices, avoid placeholder values, and keep all monetary values to 2 decimal places.
- The sum of `estimatedCostForTwo` across ingredients should be broadly consistent with `baseCostForTwo`.
- `quantityForTwo` must be a positive number.
- `tags` must only use values from: Balanced, High-Protein, Vegetarian, Vegan, Pescatarian, Gluten-Free, Special Treat
- `tags` must include every listed dietary requirement.
- `recipeSteps` must contain 5-6 concrete, meal-specific cooking steps in order.
- Keep each recipe step concise (ideally <= 140 characters).
- Include `nutritionPerServing` for one medium serving (not household total), with calories and grams for protein/carbs/fat.

Return JSON only with this schema:
{
  "name": "",
  "baseCostForTwo": 0,
  "isQuick": true,
  "tags": ["Balanced"],
  "recipeSteps": [
    "",
    "",
    "",
    "",
    ""
  ],
  "nutritionPerServing": {
    "calories": 0,
    "proteinGrams": 0,
    "carbsGrams": 0,
    "fatGrams": 0
  },
  "ingredients": [
    {
      "name": "",
      "department": "",
      "quantityForTwo": 0,
      "unit": "",
      "estimatedCostForTwo": 0
    }
  ]
}
""";
    }

    private void EnsureAiMealPoolHydrated()
    {
        try
        {
            EnsureAiMealPoolHydratedAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to hydrate AislePilot AI meal pool from Firestore.");
        }
    }

    internal async Task EnsureAiMealPoolHydratedAsync(CancellationToken cancellationToken = default)
    {
        PruneAiMealPool(DateTime.UtcNow);

        if (_db is null)
        {
            return;
        }

        var shouldRefresh =
            AiMealPool.IsEmpty ||
            !_lastAiMealPoolRefreshUtc.HasValue ||
            DateTime.UtcNow - _lastAiMealPoolRefreshUtc.Value > TimeSpan.FromMinutes(10);

        if (!shouldRefresh)
        {
            return;
        }

        await AiMealPoolRefreshLock.WaitAsync(cancellationToken);
        try
        {
            shouldRefresh =
                AiMealPool.IsEmpty ||
                !_lastAiMealPoolRefreshUtc.HasValue ||
                DateTime.UtcNow - _lastAiMealPoolRefreshUtc.Value > TimeSpan.FromMinutes(10);

            if (!shouldRefresh)
            {
                return;
            }

            var snapshot = await _db.Collection(AiMealsCollection)
                .OrderByDescending(nameof(FirestoreAislePilotMeal.CreatedAtUtc))
                .Limit(150)
                .GetSnapshotAsync(cancellationToken);
            var refreshedAtUtc = DateTime.UtcNow;

            foreach (var doc in snapshot.Documents)
            {
                if (!doc.Exists)
                {
                    continue;
                }

                var firestoreMeal = doc.ConvertTo<FirestoreAislePilotMeal>();
                var mappedMeal = FromFirestoreDocument(firestoreMeal);
                if (mappedMeal is not null)
                {
                    UpsertAiMealPoolEntry(mappedMeal, refreshedAtUtc);
                }
            }

            PruneAiMealPool(refreshedAtUtc);
            _lastAiMealPoolRefreshUtc = refreshedAtUtc;
        }
        finally
        {
            AiMealPoolRefreshLock.Release();
        }
    }

    private async Task<IReadOnlyList<MealTemplate>> PersistAiMealsAsync(
        IReadOnlyList<MealTemplate> meals,
        CancellationToken cancellationToken = default)
    {
        if (meals.Count == 0)
        {
            return [];
        }

        if (_db is null)
        {
            _logger?.LogWarning(
                "AislePilot generated {MealCount} meals but Firestore is unavailable; meals will be memory-only for this runtime.",
                meals.Count);
            return [];
        }

        var persistedMeals = new List<MealTemplate>(meals.Count);
        foreach (var meal in meals)
        {
            try
            {
                var docRef = _db.Collection(AiMealsCollection).Document(ToAiMealDocumentId(meal.Name));
                await docRef.SetAsync(ToFirestoreDocument(meal), cancellationToken: cancellationToken);
                persistedMeals.Add(meal);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unable to persist AislePilot AI meal '{MealName}'.", meal.Name);
            }
        }

        if (persistedMeals.Count < meals.Count)
        {
            _logger?.LogWarning(
                "AislePilot persisted {PersistedCount} of {TotalCount} AI meals to Firestore.",
                persistedMeals.Count,
                meals.Count);
        }

        return persistedMeals;
    }

    private static string BuildAiMealSwapPrompt(
        AislePilotRequestModel request,
        PlanContext context,
        string currentMealName,
        IReadOnlyList<string> excludedMealNames,
        int dayMultiplier,
        string mealType,
        int mealsPerDay)
    {
        var strictModes = context.DietaryModes
            .Where(mode => !mode.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var strictModeText = strictModes.Length == 0 ? "Balanced" : string.Join(", ", strictModes);
        var excludedText = excludedMealNames.Count == 0 ? "none" : string.Join(", ", excludedMealNames);
        var dislikesText = string.IsNullOrWhiteSpace(context.DislikesOrAllergens) ? "none" : context.DislikesOrAllergens;
        var safeMealsPerDay = NormalizeMealsPerDay(mealsPerDay);
        var targetMealBudget = decimal.Round(
            (request.WeeklyBudget / (7m * safeMealsPerDay)) * Math.Max(1, dayMultiplier),
            2,
            MidpointRounding.AwayFromZero);

        return $$"""
Generate one replacement {{mealType}} for a UK grocery-planning app.

Planner inputs:
- Replace this meal: {{currentMealName}}
- Meal slot to fill: {{mealType}}
- Supermarket: {{context.Supermarket}}
- Target meal budget: {{targetMealBudget.ToString("0.##", CultureInfo.InvariantCulture)}} GBP
- Household size: {{request.HouseholdSize}}
- Portion size: {{context.PortionSize}}
- Prefer quick meals: {{(request.PreferQuickMeals ? "yes" : "no")}}
- Dietary requirements: {{strictModeText}}
- Dislikes or allergens: {{dislikesText}}
- Avoid these other meals already in the week: {{excludedText}}

Rules:
- Return exactly one {{mealType}} object.
- It must be different from the current meal and different from the excluded meals.
- Use UK English.
- Keep it realistic for a UK supermarket shop.
- Use typical UK non-promo shelf prices (no loyalty-only offers, markdowns, or extreme bulk discounts).
- Respect dietary requirements and dislikes/allergens strictly.
- If quick meals are preferred, target 30 minutes or less.
- Every meal must include 3-7 ingredients only.
- Department must be one of: Produce, Bakery, Meat & Fish, Dairy & Eggs, Frozen, Tins & Dry Goods, Spices & Sauces, Snacks, Drinks, Household, Other
- Unit should be short plain text such as kg, g, pcs, tins, jar, bottle, pack, head, fillets.
- `baseCostForTwo` is an estimated GBP cost for serving 2 people once.
- `estimatedCostForTwo` is the portion of the meal cost attributable to that ingredient for serving 2 people once.
- Use realistic prices, avoid placeholder values, and keep all monetary values to 2 decimal places.
- The sum of `estimatedCostForTwo` across ingredients should be broadly consistent with `baseCostForTwo`.
- `quantityForTwo` must be a positive number.
- `tags` must only use values from: Balanced, High-Protein, Vegetarian, Vegan, Pescatarian, Gluten-Free, Special Treat
- `recipeSteps` must contain 5-8 concrete, meal-specific cooking steps in order.
- Include `nutritionPerServing` for one medium serving (not household total), with calories and grams for protein/carbs/fat.

Return JSON only with this schema:
{
  "name": "",
  "baseCostForTwo": 0,
  "isQuick": true,
  "tags": ["Balanced"],
  "recipeSteps": [
    "",
    "",
    "",
    "",
    ""
  ],
  "nutritionPerServing": {
    "calories": 0,
    "proteinGrams": 0,
    "carbsGrams": 0,
    "fatGrams": 0
  },
  "ingredients": [
    {
      "name": "",
      "department": "",
      "quantityForTwo": 0,
      "unit": "",
      "estimatedCostForTwo": 0
    }
  ]
}
""";
    }

}
