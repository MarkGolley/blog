using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

public partial class AislePilotController : Controller
{
    private IActionResult BuildPantrySuggestionResponse(
        AislePilotRequestModel request,
        string resolvedReturnUrl,
        IReadOnlyList<string>? excludedMealNames,
        string? swapCurrentMealName,
        IReadOnlyList<string>? currentSuggestionMealNames = null)
    {
        ValidateRequestForSuggestions(request);

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        var normalizedExcludedMealNames = NormalizePantryMealNames(excludedMealNames);
        var normalizedSwapCurrentMealName = string.IsNullOrWhiteSpace(swapCurrentMealName)
            ? string.Empty
            : swapCurrentMealName.Trim();
        var isSwapRequest = normalizedSwapCurrentMealName.Length > 0;
        var historyMealNames = ParsePantrySuggestionHistoryState(request.PantrySuggestionHistoryState);

        var strictExcludedMealNames = new HashSet<string>(normalizedExcludedMealNames, StringComparer.OrdinalIgnoreCase);
        foreach (var historyMealName in historyMealNames)
        {
            strictExcludedMealNames.Add(historyMealName);
        }

        if (isSwapRequest)
        {
            strictExcludedMealNames.Add(normalizedSwapCurrentMealName);
        }

        var generationNonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}"[..28];
        try
        {
            IReadOnlyList<AislePilotPantrySuggestionViewModel> suggestions;
            if (isSwapRequest)
            {
                suggestions = BuildSingleCardSwapSuggestions(
                    request,
                    normalizedSwapCurrentMealName,
                    currentSuggestionMealNames,
                    historyMealNames,
                    generationNonce);

                if (suggestions.Count == 0)
                {
                    var relaxedExcludedMealNames = new HashSet<string>(normalizedExcludedMealNames, StringComparer.OrdinalIgnoreCase);
                    relaxedExcludedMealNames.Add(normalizedSwapCurrentMealName);
                    suggestions = aislePilotService.SuggestMealsFromPantry(
                        request,
                        3,
                        relaxedExcludedMealNames.ToList(),
                        generationNonce);
                }
            }
            else
            {
                suggestions = aislePilotService.SuggestMealsFromPantry(
                    request,
                    3,
                    strictExcludedMealNames.ToList(),
                    generationNonce);
            }

            if (suggestions.Count == 0)
            {
                var noMatchMessage = isSwapRequest
                    ? "Could not find a different meal right now. Try showing 3 more ideas."
                    : request.RequireCorePantryIngredients
                        ? "No meals fit strict core mode. Add more core ingredients or turn strict mode off."
                        : "No close meals found from your current pantry items. Add more ingredients or generate a full weekly plan.";
                ModelState.AddModelError("Request.PantryItems", noMatchMessage);
                return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
            }

            request.PantrySuggestionHistoryState = UpdatePantrySuggestionHistoryState(
                request.PantrySuggestionHistoryState,
                suggestions.Select(suggestion => suggestion.MealName));
            return View("Index", BuildPageModel(request, pantrySuggestions: suggestions, returnUrl: resolvedReturnUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot pantry suggestion failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Meal generator hit a temporary issue. Please retry in a few seconds.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    private static IReadOnlyList<string> NormalizePantryMealNames(IReadOnlyList<string>? mealNames)
    {
        return (mealNames ?? [])
            .Where(mealName => !string.IsNullOrWhiteSpace(mealName))
            .Select(mealName => mealName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ParsePantrySuggestionHistoryState(string? pantrySuggestionHistoryState)
    {
        if (string.IsNullOrWhiteSpace(pantrySuggestionHistoryState))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(pantrySuggestionHistoryState, SetupStateJsonOptions);
            return NormalizePantryMealNames(parsed);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseSavedEnjoyedMealNamesState(string? savedEnjoyedMealNamesState)
    {
        if (string.IsNullOrWhiteSpace(savedEnjoyedMealNamesState))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(savedEnjoyedMealNamesState, SetupStateJsonOptions);
            return NormalizeSavedEnjoyedMealNames(parsed);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> NormalizeSavedEnjoyedMealNames(IReadOnlyList<string>? mealNames)
    {
        return (mealNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => name.Length is > 0 and <= MaxSavedMealNameLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSavedEnjoyedMealNames)
            .ToList();
    }

    private static string SerializeSavedEnjoyedMealNameState(IReadOnlyList<string>? savedMealNames)
    {
        var normalized = NormalizeSavedEnjoyedMealNames(savedMealNames);
        return normalized.Count == 0
            ? string.Empty
            : JsonSerializer.Serialize(normalized, SetupStateJsonOptions);
    }

    private static string ToggleSavedEnjoyedMealNameState(string? currentState, string mealName)
    {
        var normalizedMealName = string.IsNullOrWhiteSpace(mealName) ? string.Empty : mealName.Trim();
        if (normalizedMealName.Length == 0 || normalizedMealName.Length > MaxSavedMealNameLength)
        {
            return SerializeSavedEnjoyedMealNameState(ParseSavedEnjoyedMealNamesState(currentState));
        }

        var savedMealNames = ParseSavedEnjoyedMealNamesState(currentState).ToList();
        var existingIndex = savedMealNames.FindIndex(
            name => name.Equals(normalizedMealName, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            savedMealNames.RemoveAt(existingIndex);
        }
        else
        {
            savedMealNames.Add(normalizedMealName);
        }

        return SerializeSavedEnjoyedMealNameState(savedMealNames);
    }

    private static string RemoveSavedEnjoyedMealNameState(string? currentState, string mealName)
    {
        var normalizedMealName = string.IsNullOrWhiteSpace(mealName) ? string.Empty : mealName.Trim();
        if (normalizedMealName.Length == 0 || normalizedMealName.Length > MaxSavedMealNameLength)
        {
            return SerializeSavedEnjoyedMealNameState(ParseSavedEnjoyedMealNamesState(currentState));
        }

        var savedMealNames = ParseSavedEnjoyedMealNamesState(currentState)
            .Where(name => !name.Equals(normalizedMealName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return SerializeSavedEnjoyedMealNameState(savedMealNames);
    }

    private static string UpdatePantrySuggestionHistoryState(
        string? currentState,
        IEnumerable<string> additionalMealNames)
    {
        const int MaxMealNames = 48;

        var history = ParsePantrySuggestionHistoryState(currentState)
            .ToList();
        foreach (var mealName in additionalMealNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Select(name => name.Trim()))
        {
            if (!history.Contains(mealName, StringComparer.OrdinalIgnoreCase))
            {
                history.Add(mealName);
            }
        }

        if (history.Count > MaxMealNames)
        {
            history = history
                .Skip(history.Count - MaxMealNames)
                .ToList();
        }

        return JsonSerializer.Serialize(history, SetupStateJsonOptions);
    }

    private IReadOnlyList<AislePilotPantrySuggestionViewModel> BuildSingleCardSwapSuggestions(
        AislePilotRequestModel request,
        string currentMealName,
        IReadOnlyList<string>? currentSuggestionMealNames,
        IReadOnlyList<string> historyMealNames,
        string generationNonce)
    {
        var normalizedCurrentSuggestionMealNames = NormalizePantryMealNames(currentSuggestionMealNames);
        if (normalizedCurrentSuggestionMealNames.Count == 0 || string.IsNullOrWhiteSpace(currentMealName))
        {
            return [];
        }

        if (!normalizedCurrentSuggestionMealNames.Contains(currentMealName, StringComparer.OrdinalIgnoreCase))
        {
            normalizedCurrentSuggestionMealNames = normalizedCurrentSuggestionMealNames
                .Append(currentMealName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var unchangedMealNames = normalizedCurrentSuggestionMealNames
            .Where(mealName => !mealName.Equals(currentMealName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        var strictReplacementExclusions = new HashSet<string>(historyMealNames, StringComparer.OrdinalIgnoreCase);
        foreach (var mealName in normalizedCurrentSuggestionMealNames)
        {
            strictReplacementExclusions.Add(mealName);
        }

        var replacement = TrySelectPantrySwapReplacement(
            request,
            strictReplacementExclusions,
            generationNonce);
        if (replacement is null)
        {
            var relaxedReplacementExclusions = new HashSet<string>(normalizedCurrentSuggestionMealNames, StringComparer.OrdinalIgnoreCase);
            replacement = TrySelectPantrySwapReplacement(
                request,
                relaxedReplacementExclusions,
                generationNonce);
        }

        if (replacement is null)
        {
            var minimalReplacementExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                currentMealName
            };
            replacement = TrySelectPantrySwapReplacement(
                request,
                minimalReplacementExclusions,
                generationNonce);
        }

        if (replacement is null)
        {
            return [];
        }

        var targetMealNames = unchangedMealNames
            .Append(replacement.MealName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        if (targetMealNames.Count == 0)
        {
            return [];
        }

        var detailCandidates = aislePilotService.SuggestMealsFromPantry(
            request,
            12,
            excludedMealNames: [],
            generationNonce);
        var detailByMealName = detailCandidates.ToDictionary(
            suggestion => suggestion.MealName,
            suggestion => suggestion,
            StringComparer.OrdinalIgnoreCase);
        detailByMealName[replacement.MealName] = replacement;

        var finalSuggestions = new List<AislePilotPantrySuggestionViewModel>(targetMealNames.Count);
        foreach (var mealName in targetMealNames)
        {
            if (!detailByMealName.TryGetValue(mealName, out var suggestion))
            {
                return [];
            }

            finalSuggestions.Add(suggestion);
        }

        return OrderPantrySuggestionsForDisplay(finalSuggestions);
    }

    private AislePilotPantrySuggestionViewModel? TrySelectPantrySwapReplacement(
        AislePilotRequestModel request,
        IReadOnlyCollection<string> excludedMealNames,
        string generationNonce)
    {
        var replacements = aislePilotService.SuggestMealsFromPantry(
            request,
            12,
            excludedMealNames.ToList(),
            generationNonce);
        return replacements.FirstOrDefault();
    }

    private static IReadOnlyList<AislePilotPantrySuggestionViewModel> OrderPantrySuggestionsForDisplay(
        IReadOnlyList<AislePilotPantrySuggestionViewModel> suggestions)
    {
        if (suggestions.Count <= 1)
        {
            return suggestions;
        }

        return suggestions
            .OrderByDescending(suggestion => suggestion.MatchPercent)
            .ThenBy(suggestion => suggestion.MissingIngredientsEstimatedCost)
            .ThenBy(suggestion => suggestion.MissingCoreIngredientCount)
            .ThenBy(suggestion => suggestion.MealName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

}
