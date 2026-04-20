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
    private static readonly HashSet<string> SwapSimilarityNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "bread",
        "breast",
        "breasts",
        "broth",
        "couscous",
        "fillet",
        "fillets",
        "garlic",
        "herb",
        "herbs",
        "leaf",
        "leaves",
        "lettuce",
        "milk",
        "noodle",
        "noodles",
        "oil",
        "onion",
        "onions",
        "pasta",
        "pepper",
        "peppers",
        "potato",
        "potatoes",
        "quinoa",
        "rice",
        "salad",
        "sauce",
        "sauces",
        "stock",
        "tomato",
        "tomatoes",
        "wrap",
        "wraps"
    };

    private static readonly HashSet<string> SwapSimilarityProteinTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "anchovy",
        "beef",
        "bean",
        "beans",
        "chickpea",
        "chickpeas",
        "chicken",
        "cod",
        "egg",
        "eggs",
        "halloumi",
        "lentil",
        "lentils",
        "lamb",
        "mackerel",
        "mince",
        "mushroom",
        "mushrooms",
        "paneer",
        "pork",
        "prawn",
        "prawns",
        "quorn",
        "salmon",
        "sausage",
        "sausages",
        "shrimp",
        "tofu",
        "tuna",
        "turkey",
        "yogurt",
        "yoghurt"
    };

    internal static MealTemplate? SelectSwapCandidate(
        IReadOnlyList<MealTemplate> allCandidates,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        MealTemplate? currentMeal,
        string currentMealName,
        decimal weeklyBudget,
        decimal householdFactor,
        decimal priceFactor,
        bool preferQuickMeals,
        bool preferHighProtein,
        string mealType,
        int dayMultiplier,
        int mealsPerDay)
    {
        if (allCandidates.Count == 0)
        {
            return null;
        }

        var usedNames = selectedMeals
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dedupedCandidates = allCandidates
            .Select(meal => EnsureMealTypeSuitability(meal))
            .ToList();
        var preferredPool = dedupedCandidates
            .Where(meal =>
                !meal.Name.Equals(currentMealName, StringComparison.OrdinalIgnoreCase) &&
                !usedNames.Contains(meal.Name))
            .ToList();

        if (preferredPool.Count == 0)
        {
            return null;
        }

        var normalizedDayMultiplier = Math.Max(1, dayMultiplier);
        var safeMealsPerDay = NormalizeMealsPerDay(mealsPerDay);
        var targetMealCost = (weeklyBudget / (7m * safeMealsPerDay)) * normalizedDayMultiplier;
        var previousName = dayIndex > 0 ? selectedMeals[dayIndex - 1].Name : null;
        var nextName = dayIndex < selectedMeals.Count - 1 ? selectedMeals[dayIndex + 1].Name : null;
        var slotCompatiblePool = preferredPool
            .Where(meal => SupportsMealType(meal, mealType))
            .ToList();
        if (slotCompatiblePool.Count == 0)
        {
            return null;
        }

        var adventurousPool = PreferAdventurousSwapCandidates(slotCompatiblePool, currentMeal);
        var effectivePool = adventurousPool.Count > 0 ? adventurousPool : slotCompatiblePool;

        return effectivePool
            .Select(template => new
            {
                template,
                score = BuildMealSelectionScore(
                    template,
                    targetMealCost,
                    householdFactor,
                    priceFactor,
                    preferQuickMeals,
                    preferHighProtein,
                    normalizedDayMultiplier,
                    previousName,
                    nextName)
            })
            .OrderBy(item => item.score)
            .ThenBy(item => item.template.Name, StringComparer.OrdinalIgnoreCase)
            .First()
            .template;
    }

    private static IReadOnlyList<MealTemplate> PreferAdventurousSwapCandidates(
        IReadOnlyList<MealTemplate> candidates,
        MealTemplate? currentMeal)
    {
        if (currentMeal is null || candidates.Count <= 1)
        {
            return candidates;
        }

        var adventurous = candidates
            .Where(candidate => !SharesKeySwapIngredients(candidate, currentMeal))
            .ToList();

        return adventurous.Count > 0 ? adventurous : candidates;
    }

    private static bool SharesKeySwapIngredients(MealTemplate candidate, MealTemplate currentMeal)
    {
        var candidateSignature = BuildSwapIngredientSignature(candidate);
        var currentSignature = BuildSwapIngredientSignature(currentMeal);
        if (candidateSignature.AllTokens.Count == 0 || currentSignature.AllTokens.Count == 0)
        {
            return false;
        }

        if (candidateSignature.ProteinTokens.Overlaps(currentSignature.ProteinTokens))
        {
            return true;
        }

        var sharedTokenCount = candidateSignature.AllTokens
            .Count(currentSignature.AllTokens.Contains);
        return sharedTokenCount >= 2;
    }

    private static SwapIngredientSignature BuildSwapIngredientSignature(MealTemplate meal)
    {
        var allTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var proteinTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prioritizedIngredients = meal.Ingredients
            .Where(ingredient => !IsMinorPantryAssumptionIngredient(ingredient.Name))
            .OrderByDescending(ingredient => ResolveSwapIngredientPriority(ingredient))
            .ThenByDescending(ingredient => ingredient.EstimatedCostForTwo)
            .Take(3)
            .ToList();

        foreach (var ingredient in prioritizedIngredients)
        {
            foreach (var token in ExtractSwapIngredientTokens(ingredient))
            {
                allTokens.Add(token);
                if (IsProteinLikeSwapIngredient(ingredient, token))
                {
                    proteinTokens.Add(token);
                }
            }
        }

        return new SwapIngredientSignature(allTokens, proteinTokens);
    }

    private static int ResolveSwapIngredientPriority(IngredientTemplate ingredient)
    {
        return ingredient.Department.Equals("Meat & Fish", StringComparison.OrdinalIgnoreCase)
            ? 3
            : ingredient.Department.Equals("Dairy & Eggs", StringComparison.OrdinalIgnoreCase)
                ? 2
                : 1;
    }

    private static IReadOnlyList<string> ExtractSwapIngredientTokens(IngredientTemplate ingredient)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var searchTerm in BuildIngredientSearchTerms(ingredient.Name))
        {
            if (string.IsNullOrWhiteSpace(searchTerm) ||
                GenericPantryTokensNormalized.Contains(searchTerm) ||
                AssumedPantryBasicsNormalized.Contains(searchTerm))
            {
                continue;
            }

            var coreWords = ExtractCoreIngredientWords(
                searchTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            foreach (var word in coreWords)
            {
                if (word.Length < 4 || SwapSimilarityNoiseWords.Contains(word))
                {
                    continue;
                }

                tokens.Add(word);
            }
        }

        return tokens.ToList();
    }

    private static bool IsProteinLikeSwapIngredient(IngredientTemplate ingredient, string token)
    {
        if (ingredient.Department.Equals("Meat & Fish", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (SwapSimilarityProteinTokens.Contains(token))
        {
            return true;
        }

        var normalizedIngredientName = NormalizePantryText(ingredient.Name);
        return SwapSimilarityProteinTokens.Any(proteinToken =>
            normalizedIngredientName.Contains(proteinToken, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record SwapIngredientSignature(
        HashSet<string> AllTokens,
        HashSet<string> ProteinTokens);

    private static bool HasUniqueMealNames(
        IReadOnlyList<MealTemplate> meals,
        int expectedMeals,
        IReadOnlyList<string>? mealTypeSlots = null)
    {
        if (meals.Count < expectedMeals)
        {
            return false;
        }

        if (mealTypeSlots is { Count: > 0 })
        {
            var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
            var slotTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < expectedMeals; i++)
            {
                var slotMealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
                slotTypeCounts[slotMealType] = slotTypeCounts.GetValueOrDefault(slotMealType, 0) + 1;
            }

            var slotTypeMealNameCounts = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < expectedMeals; i++)
            {
                var meal = meals[i];
                var slotMealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
                if (!SupportsMealType(meal, slotMealType))
                {
                    return false;
                }

                if (!slotTypeMealNameCounts.TryGetValue(slotMealType, out var mealNameCounts))
                {
                    mealNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    slotTypeMealNameCounts[slotMealType] = mealNameCounts;
                }

                var nextCount = mealNameCounts.GetValueOrDefault(meal.Name, 0) + 1;
                mealNameCounts[meal.Name] = nextCount;

                var slotCount = slotTypeCounts.GetValueOrDefault(slotMealType, 0);
                var maxRepeats = ResolveMaxMealRepeatsForSlotType(slotMealType, slotCount);
                if (nextCount > maxRepeats)
                {
                    return false;
                }
            }

            return true;
        }

        var uniqueCount = meals
            .Take(expectedMeals)
            .Select(meal => meal.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return uniqueCount == expectedMeals;
    }

    private static decimal BuildMealSelectionScore(
        MealTemplate template,
        decimal targetMealCost,
        decimal householdFactor,
        decimal priceFactor,
        bool preferQuickMeals,
        bool preferHighProtein,
        int dayMultiplier = 1,
        string? previousName = null,
        string? nextName = null)
    {
        var normalizedDayMultiplier = Math.Max(1, dayMultiplier);
        var scaledCost = template.BaseCostForTwo * householdFactor * NormalizeSupermarketPriceFactor(priceFactor) * normalizedDayMultiplier;
        var budgetDistance = Math.Abs(scaledCost - targetMealCost);
        var quickPenalty = preferQuickMeals && !template.IsQuick ? 0.8m : 0m;
        var highProteinPenalty =
            preferHighProtein &&
            !template.Tags.Contains("High-Protein", StringComparer.OrdinalIgnoreCase)
                ? 0.45m
                : 0m;
        var adjacencyPenalty =
            (previousName is not null && template.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase)) ||
            (nextName is not null && template.Name.Equals(nextName, StringComparison.OrdinalIgnoreCase))
                ? 1.2m
                : 0m;

        return budgetDistance + quickPenalty + highProteinPenalty + adjacencyPenalty;
    }

}
