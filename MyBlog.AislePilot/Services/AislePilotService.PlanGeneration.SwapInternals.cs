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
    private MealTemplate? TryBuildReplacementMealWithAi(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        string currentMealName,
        int dayMultiplier,
        string mealType,
        IReadOnlyList<string> seenMealNames)
    {
        try
        {
            return TryBuildReplacementMealWithAiAsync(
                request,
                context,
                selectedMeals,
                dayIndex,
                currentMealName,
                dayMultiplier,
                mealType,
                seenMealNames).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AislePilot AI meal swap failed.");
            return null;
        }
    }

    internal async Task<MealTemplate?> TryBuildReplacementMealWithAiAsync(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        string currentMealName,
        int dayMultiplier,
        string mealType,
        IReadOnlyList<string> seenMealNames,
        CancellationToken cancellationToken = default)
    {
        if (!_enableAiGeneration || _httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        const int maxSemanticGenerationAttempts = 3;

        var currentMeal = dayIndex >= 0 && dayIndex < selectedMeals.Count
            ? selectedMeals[dayIndex]
            : null;
        var excludedMealNames = selectedMeals
            .Where((_, index) => index != dayIndex)
            .Select(meal => meal.Name)
            .Concat(seenMealNames)
            .ToArray();
        var prompt = BuildAiMealSwapPrompt(
            request,
            context,
            currentMealName,
            excludedMealNames,
            dayMultiplier,
            mealType,
            BuildMealTypeSlots(request).Count);
        var requestBody = new
        {
            model = _model,
            temperature = 0.9,
            max_tokens = 1000,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You generate one practical replacement meal for a UK grocery-planning app. Always return valid JSON only. Use UK English."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        for (var attempt = 1; attempt <= maxSemanticGenerationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var responseContent = await SendOpenAiRequestWithRetryAsync(requestBody, cancellationToken);
            if (string.IsNullOrWhiteSpace(responseContent))
            {
                continue;
            }

            var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
            var rawJson = payload?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                continue;
            }

            var normalizedJson = NormalizeModelJson(rawJson);
            if (!TryParseAiMealPayloadWithRecovery(normalizedJson, out var aiPayload))
            {
                continue;
            }

            var strictModes = ResolveHardDietaryModes(context.DietaryModes);
            var replacement = ValidateAndMapAiMeal(
                aiPayload,
                strictModes,
                requireRecipeSteps: true,
                suitableMealTypes: [mealType],
                out var validationReason);
            if (replacement is null)
            {
                _logger?.LogInformation(
                    "AislePilot AI swap generation attempt {Attempt}/{MaxAttempts} produced an invalid meal. Reason={ValidationReason}",
                    attempt,
                    maxSemanticGenerationAttempts,
                    validationReason ?? "unknown");
                continue;
            }

            if (replacement.Name.Equals(currentMealName, StringComparison.OrdinalIgnoreCase) ||
                excludedMealNames.Contains(replacement.Name, StringComparer.OrdinalIgnoreCase))
            {
                _logger?.LogInformation(
                    "AislePilot AI swap generation attempt {Attempt}/{MaxAttempts} repeated an excluded meal '{MealName}'.",
                    attempt,
                    maxSemanticGenerationAttempts,
                    replacement.Name);
                continue;
            }

            if (currentMeal is not null && SharesKeySwapIngredients(replacement, currentMeal))
            {
                _logger?.LogInformation(
                    "AislePilot AI swap generation attempt {Attempt}/{MaxAttempts} reused key ingredients for '{MealName}'.",
                    attempt,
                    maxSemanticGenerationAttempts,
                    replacement.Name);
                continue;
            }

            var persistedMeals = await PersistAiMealsAsync([replacement], cancellationToken);
            if (persistedMeals.Count > 0)
            {
                AddMealsToAiPool(persistedMeals);
            }
            else
            {
                _logger?.LogWarning(
                    "AislePilot generated swap meal '{MealName}' but it was not persisted; skipping shared AI meal pool update.",
                    replacement.Name);
            }

            return replacement;
        }

        return null;
    }

    internal static MealTemplate? TrySelectTemplateSwapCandidate(
        PlanContext context,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        MealTemplate? currentMeal,
        string currentMealName,
        decimal weeklyBudget,
        bool preferQuickMeals,
        string mealType,
        int dayMultiplier,
        int mealsPerDay,
        IReadOnlyList<string> seenMealNames)
    {
        var templateCandidates = FilterMeals(context.DietaryModes, context.DislikesOrAllergens);
        if (templateCandidates.Count == 0)
        {
            return null;
        }

        var unseenTemplates = templateCandidates
            .Where(meal => !seenMealNames.Contains(meal.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (unseenTemplates.Count == 0)
        {
            return null;
        }

        return SelectSwapCandidate(
            unseenTemplates,
            selectedMeals,
            dayIndex,
            currentMeal,
            currentMealName,
            weeklyBudget,
            context.HouseholdFactor,
            context.PriceProfile.RelativeCostFactor,
            preferQuickMeals,
            IsHighProteinPreferred(context.DietaryModes),
            mealType,
            dayMultiplier,
            mealsPerDay);
    }

}
