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
    private static bool ContainsToken(MealTemplate meal, string token)
    {
        if (meal.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return meal.Ingredients.Any(ingredient =>
            ingredient.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    internal static AislePilotPantrySuggestionViewModel BuildPantrySuggestion(
        MealTemplate template,
        IReadOnlyList<string> pantryTokens,
        decimal householdFactor,
        decimal priceFactor)
    {
        var ingredientNames = template.Ingredients
            .Select(ingredient => ingredient.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matched = ingredientNames
            .Where(ingredient => PantryHasIngredient(pantryTokens, ingredient))
            .ToList();
        var missing = ingredientNames
            .Where(ingredient => !PantryHasIngredient(pantryTokens, ingredient))
            .ToList();

        var total = Math.Max(1, ingredientNames.Count);
        var matchPercent = (int)Math.Round((matched.Count / (double)total) * 100.0, MidpointRounding.AwayFromZero);
        var missingCoreIngredientCount = missing.Count(ingredient => !IsMinorPantryAssumptionIngredient(ingredient));
        var normalizedPriceFactor = NormalizeSupermarketPriceFactor(priceFactor);
        var missingIngredientsEstimatedCost = decimal.Round(
            template.Ingredients
                .Where(ingredient => missing.Contains(ingredient.Name, StringComparer.OrdinalIgnoreCase))
                .Sum(ingredient => ingredient.EstimatedCostForTwo * householdFactor * normalizedPriceFactor),
            2,
            MidpointRounding.AwayFromZero);

        return new AislePilotPantrySuggestionViewModel
        {
            MealName = template.Name,
            MatchPercent = matchPercent,
            MissingCoreIngredientCount = missingCoreIngredientCount,
            MissingIngredientsEstimatedCost = missingIngredientsEstimatedCost,
            CanCookNow = missingCoreIngredientCount == 0,
            MatchedIngredients = matched,
            MissingIngredients = missing
        };
    }

    private static bool PantryHasIngredient(IReadOnlyList<string> pantryTokens, string ingredientName)
    {
        var ingredientSearchTerms = BuildIngredientSearchTerms(ingredientName);

        return pantryTokens.Any(token =>
        {
            var normalizedToken = NormalizePantryText(token);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                return false;
            }

            return ingredientSearchTerms.Any(searchTerm => PantryTokenMatchesIngredient(normalizedToken, searchTerm));
        });
    }

    internal static IReadOnlyList<string> ParsePantryTokens(string? pantryItems)
    {
        var rawPantry = pantryItems ?? string.Empty;
        var canonicalized = Regex.Replace(rawPantry, @"\s+(?:and|&)\s+", ", ", RegexOptions.IgnoreCase);

        return canonicalized
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim(' ', '.', ':'))
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static int CountMatchedPantryTokens(MealTemplate template, IReadOnlyList<string> pantryTokens)
    {
        if (pantryTokens.Count == 0)
        {
            return 0;
        }

        var ingredientSearchTerms = template.Ingredients
            .SelectMany(ingredient => BuildIngredientSearchTerms(ingredient.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matchedCount = 0;

        foreach (var pantryToken in pantryTokens)
        {
            var normalizedToken = NormalizePantryText(pantryToken);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                continue;
            }

            if (!ingredientSearchTerms.Any(searchTerm => PantryTokenMatchesIngredient(normalizedToken, searchTerm)))
            {
                continue;
            }

            matchedCount++;
        }

        return matchedCount;
    }

    internal static int ComputePantrySuggestionScore(
        AislePilotPantrySuggestionViewModel suggestion,
        AislePilotPantrySuggestionViewModel userOnlySuggestion,
        int userMatchedTokenCount,
        int specificMatchedTokenCount,
        int specificTokenCount)
    {
        var score = 0;
        score += suggestion.CanCookNow ? 240 : 0;
        score += suggestion.MatchPercent * 2;
        score += userOnlySuggestion.MatchPercent * 4;
        score += userMatchedTokenCount * 36;
        score += specificMatchedTokenCount * 72;
        score -= suggestion.MissingCoreIngredientCount * 110;
        score -= suggestion.MissingIngredients.Count * 22;

        if (userOnlySuggestion.MatchedIngredients.Count == 0)
        {
            score -= 160;
        }

        if (specificTokenCount > 0 && specificMatchedTokenCount == 0)
        {
            score -= 260;
        }

        return score;
    }

    internal static IReadOnlyList<PantrySuggestionCandidate> RankPantrySuggestionCandidates(
        IReadOnlyList<PantrySuggestionCandidate> candidates,
        int targetCount,
        bool allowVariation)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Suggestion.MissingCoreIngredientCount)
            .ThenBy(candidate => candidate.Suggestion.MissingIngredients.Count)
            .ThenBy(candidate => candidate.Template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!allowVariation || ordered.Count <= 1)
        {
            return ordered;
        }

        var topScore = ordered[0].Score;
        var topPoolSize = Math.Max(targetCount * 2, 6);
        var rotationPool = ordered
            .TakeWhile(candidate => topScore - candidate.Score <= 95)
            .Take(topPoolSize)
            .ToList();
        if (rotationPool.Count <= 1)
        {
            return ordered;
        }

        var rotation = Random.Shared.Next(rotationPool.Count);
        var rotatedPool = rotationPool
            .Skip(rotation)
            .Concat(rotationPool.Take(rotation))
            .ToList();
        var poolMealNames = new HashSet<string>(
            rotationPool.Select(candidate => candidate.Template.Name),
            StringComparer.OrdinalIgnoreCase);
        var remainder = ordered
            .Where(candidate => !poolMealNames.Contains(candidate.Template.Name))
            .ToList();
        return rotatedPool.Concat(remainder).ToList();
    }

    internal static IReadOnlyList<string> ParseSpecificPantryTokens(IReadOnlyList<string> pantryTokens)
    {
        return pantryTokens
            .Where(token =>
            {
                var normalizedToken = NormalizePantryText(token);
                if (string.IsNullOrWhiteSpace(normalizedToken))
                {
                    return false;
                }

                if (GenericPantryTokensNormalized.Contains(normalizedToken))
                {
                    return false;
                }

                return !GenericPantryTokensNormalized.Any(generic =>
                    normalizedToken.Contains(generic, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    internal static IReadOnlyList<string> MergePantryTokensWithAssumedBasics(IReadOnlyList<string> pantryTokens)
    {
        var merged = new HashSet<string>(pantryTokens, StringComparer.OrdinalIgnoreCase);
        foreach (var assumedBasic in AssumedPantryBasics)
        {
            merged.Add(assumedBasic);
        }

        return merged.ToList();
    }

    internal static bool TemplateUsesCoreIngredientsFromUserPantry(
        MealTemplate template,
        IReadOnlyList<string> userPantryTokens)
    {
        var distinctIngredients = template.Ingredients
            .Select(ingredient => ingredient.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var coreIngredients = distinctIngredients
            .Where(ingredient => !IsMinorPantryAssumptionIngredient(ingredient))
            .ToList();
        if (coreIngredients.Count == 0)
        {
            return distinctIngredients.Any(ingredient => PantryHasIngredient(userPantryTokens, ingredient));
        }

        return coreIngredients.All(ingredient => PantryHasIngredient(userPantryTokens, ingredient));
    }

    private static bool IsMinorPantryAssumptionIngredient(string ingredientName)
    {
        var normalizedIngredientTerms = BuildIngredientSearchTerms(ingredientName);

        return normalizedIngredientTerms.Any(term =>
            AssumedPantryBasicsNormalized.Any(assumed => PantryTokenMatchesIngredient(assumed, term)));
    }

    private static IReadOnlyList<string> BuildIngredientSearchTerms(string ingredientName)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NormalizePantryText(ingredientName)
        };

        if (IngredientAliases.TryGetValue(ingredientName, out var aliases))
        {
            foreach (var alias in aliases)
            {
                var normalizedAlias = NormalizePantryText(alias);
                if (!string.IsNullOrWhiteSpace(normalizedAlias))
                {
                    terms.Add(normalizedAlias);
                }
            }
        }

        return terms.ToList();
    }

    private static bool PantryTokenMatchesIngredient(string normalizedToken, string normalizedIngredient)
    {
        if (normalizedToken.Equals(normalizedIngredient, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedToken.Contains(normalizedIngredient, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokenWords = normalizedToken
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        var ingredientWords = normalizedIngredient
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (tokenWords.Count == 0 || ingredientWords.Count == 0)
        {
            return false;
        }

        var tokenCoreWords = ExtractCoreIngredientWords(tokenWords);
        var ingredientCoreWords = ExtractCoreIngredientWords(ingredientWords);
        if (tokenCoreWords.Count == 0 || ingredientCoreWords.Count == 0)
        {
            return false;
        }

        if (tokenCoreWords.All(tokenWord =>
                ingredientCoreWords.Contains(tokenWord, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (tokenCoreWords.Count == 1)
        {
            var tokenWord = tokenCoreWords[0];
            if (tokenWord.Length < 4)
            {
                return false;
            }

            return ingredientCoreWords.Any(word =>
                word.StartsWith(tokenWord, StringComparison.OrdinalIgnoreCase) ||
                tokenWord.StartsWith(word, StringComparison.OrdinalIgnoreCase));
        }

        return tokenCoreWords.Any(tokenWord =>
            tokenWord.Length >= 4 &&
            ingredientCoreWords.Any(ingredientWord =>
                ingredientWord.StartsWith(tokenWord, StringComparison.OrdinalIgnoreCase) ||
                tokenWord.StartsWith(ingredientWord, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<string> ExtractCoreIngredientWords(IReadOnlyList<string> words)
    {
        var coreWords = words
            .Where(word => !IngredientDescriptorWords.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (coreWords.Count > 0)
        {
            return coreWords;
        }

        return words
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePantryText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lower = value.ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);

        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append(' ');
            }
        }

        var words = builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizePantryWord)
            .Where(word => word.Length > 0)
            .ToArray();

        return string.Join(' ', words);
    }

    private static string NormalizePantryWord(string word)
    {
        if (word.EndsWith("oes", StringComparison.Ordinal) && word.Length > 4)
        {
            return word[..^2];
        }

        if (word.EndsWith("ies", StringComparison.Ordinal) && word.Length > 4)
        {
            return word[..^3] + "y";
        }

        if (word.EndsWith("ses", StringComparison.Ordinal) && word.Length > 4)
        {
            return word[..^2];
        }

        if (word.EndsWith("s", StringComparison.Ordinal) &&
            word.Length > 3 &&
            !word.EndsWith("ss", StringComparison.Ordinal) &&
            !word.EndsWith("ous", StringComparison.Ordinal))
        {
            return word[..^1];
        }

        return word;
    }

    private static bool SuggestionMatchesSpecificTokens(
        AislePilotPantrySuggestionViewModel suggestion,
        IReadOnlyList<string> specificPantryTokens)
    {
        if (specificPantryTokens.Count == 0)
        {
            return true;
        }

        return suggestion.MatchedIngredients.Any(ingredient => PantryHasIngredient(specificPantryTokens, ingredient));
    }

}
