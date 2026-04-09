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
    private static MealTemplate? SelectSwapCandidate(
        IReadOnlyList<MealTemplate> allCandidates,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        string currentMealName,
        decimal weeklyBudget,
        decimal householdFactor,
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

        return slotCompatiblePool
            .Select(template => new
            {
                template,
                score = BuildMealSelectionScore(
                    template,
                    targetMealCost,
                    householdFactor,
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
        bool preferQuickMeals,
        bool preferHighProtein,
        int dayMultiplier = 1,
        string? previousName = null,
        string? nextName = null)
    {
        var normalizedDayMultiplier = Math.Max(1, dayMultiplier);
        var scaledCost = template.BaseCostForTwo * householdFactor * normalizedDayMultiplier;
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

    private static IReadOnlyList<AislePilotMealDayViewModel> BuildDailyPlans(
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
            var nutrition = EstimateMealNutritionPerServing(template, portionSizeFactor);

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
                RecipeSteps = BuildRecipeSteps(template)
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

    private static IReadOnlyList<string> BuildRecipeSteps(MealTemplate template)
    {
        var mealName = template.Name.Trim().ToLowerInvariant();
        if (template.AiRecipeSteps is { Count: >= 5 } aiRecipeSteps)
        {
            var normalizedAiRecipeSteps = CleanAiRecipeSteps(aiRecipeSteps);
            if (AreAiRecipeStepsDetailed(normalizedAiRecipeSteps, template))
            {
                return normalizedAiRecipeSteps;
            }
        }

        return BuildFallbackRecipeSteps(template, mealName);
    }

    private static IReadOnlyList<string> BuildFallbackRecipeSteps(
        MealTemplate template,
        string mealName)
    {
        var baselineFallback = BuildFallbackRecipeSteps(mealName);
        if (DoRecipeStepsReferenceIngredients(baselineFallback, template))
        {
            return baselineFallback;
        }

        return BuildIngredientAwareFallbackRecipeSteps(template, mealName);
    }

    private static bool DoRecipeStepsReferenceIngredients(
        IReadOnlyList<string> recipeSteps,
        MealTemplate template)
    {
        if (recipeSteps.Count == 0 || template.Ingredients.Count == 0)
        {
            return false;
        }

        var ingredientTerms = template.Ingredients
            .SelectMany(ingredient => BuildIngredientSearchTerms(ingredient.Name))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ingredientTerms.Count == 0)
        {
            return false;
        }

        return recipeSteps.Any(step =>
            ingredientTerms.Any(term => ContainsWholeWord(step, term)));
    }

    private static IReadOnlyList<string> BuildIngredientAwareFallbackRecipeSteps(
        MealTemplate template,
        string mealName)
    {
        var ingredientNames = template.Ingredients
            .Select(ingredient => ClampAndNormalize(ingredient.Name, MaxAiIngredientNameLength))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        var primaryIngredient = ingredientNames.ElementAtOrDefault(0) ?? "the main ingredient";
        var secondaryIngredient = ingredientNames.ElementAtOrDefault(1) ?? primaryIngredient;
        var thirdIngredient = ingredientNames.ElementAtOrDefault(2) ?? secondaryIngredient;
        var carbIngredient = ingredientNames.FirstOrDefault(IsLikelyCarbIngredientName);

        if (mealName.Contains("salad", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                $"Cook {primaryIngredient} first if needed, then let it cool for 8-10 minutes.",
                $"Prepare {secondaryIngredient} and {thirdIngredient} into bite-size pieces.",
                "Whisk a quick dressing with oil, acid, salt, and pepper.",
                $"Combine {primaryIngredient}, {secondaryIngredient}, and {thirdIngredient}, then toss through the dressing.",
                "Taste, adjust seasoning, and chill briefly before serving."
            ];
        }

        if (mealName.Contains("baked", StringComparison.OrdinalIgnoreCase) ||
            mealName.Contains("tray bake", StringComparison.OrdinalIgnoreCase) ||
            mealName.Contains("roast", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Preheat oven to 200C (fan 180C) and line a roasting tray.",
                $"Season {primaryIngredient} and {secondaryIngredient} with oil, salt, and pepper.",
                $"Roast {secondaryIngredient} for 12-15 minutes to start softening.",
                $"Add {primaryIngredient} and {thirdIngredient}, then roast for another 12-18 minutes until cooked through.",
                "Rest for 2 minutes, then serve with tray juices spooned over."
            ];
        }

        if (mealName.Contains("curry", StringComparison.OrdinalIgnoreCase) ||
            mealName.Contains("chilli", StringComparison.OrdinalIgnoreCase) ||
            mealName.Contains("stew", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Heat a deep pan over medium heat for 2 minutes with a little oil.",
                $"Cook {primaryIngredient} for 5-7 minutes until lightly browned and fragrant.",
                $"Stir in {secondaryIngredient} and {thirdIngredient} with a splash of water or stock.",
                "Bring to a gentle simmer and cook for 15-20 minutes, stirring occasionally, until thickened.",
                "Taste, adjust seasoning, and rest for 2 minutes before serving."
            ];
        }

        if (mealName.Contains("stir fry", StringComparison.OrdinalIgnoreCase) ||
            mealName.Contains("noodle", StringComparison.OrdinalIgnoreCase))
        {
            var baseIngredient = string.IsNullOrWhiteSpace(carbIngredient) ? "your cooked base" : carbIngredient;
            return
            [
                $"Cook {baseIngredient} according to pack instructions and keep it warm.",
                "Heat a large pan or wok over medium-high heat for 2 minutes with a little oil.",
                $"Cook {primaryIngredient} for 4-6 minutes until nearly done.",
                $"Add {secondaryIngredient} and {thirdIngredient}, then stir-fry for 3-4 minutes until just tender.",
                $"Toss through {baseIngredient} and any sauce for 1-2 minutes until evenly coated, then serve."
            ];
        }

        return
        [
            $"Prep {primaryIngredient}, {secondaryIngredient}, and {thirdIngredient} into even pieces.",
            "Heat a large pan over medium-high heat for 2 minutes and add a little oil.",
            $"Cook {primaryIngredient} for 6-8 minutes, stirring, until browned and nearly cooked through.",
            $"Add {secondaryIngredient} and {thirdIngredient} with a splash of water or stock, then cook for 4-6 minutes.",
            "Taste, adjust seasoning, and rest for 1-2 minutes before serving."
        ];
    }

    private static bool IsLikelyCarbIngredientName(string ingredientName)
    {
        if (string.IsNullOrWhiteSpace(ingredientName))
        {
            return false;
        }

        return ingredientName.Contains("rice", StringComparison.OrdinalIgnoreCase) ||
               ingredientName.Contains("pasta", StringComparison.OrdinalIgnoreCase) ||
               ingredientName.Contains("noodle", StringComparison.OrdinalIgnoreCase) ||
               ingredientName.Contains("quinoa", StringComparison.OrdinalIgnoreCase) ||
               ingredientName.Contains("couscous", StringComparison.OrdinalIgnoreCase) ||
               ingredientName.Contains("potato", StringComparison.OrdinalIgnoreCase) ||
               ingredientName.Contains("wrap", StringComparison.OrdinalIgnoreCase) ||
               ingredientName.Contains("bread", StringComparison.OrdinalIgnoreCase) ||
               ingredientName.Contains("oat", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildFallbackRecipeSteps(string mealName)
    {
        return mealName switch
        {
            "chicken stir fry with rice" =>
            [
                "Rinse the rice and cook according to pack instructions; spread on a tray to steam off if you want less sticky rice.",
                "Slice the chicken and peppers into even strips so they cook at the same speed.",
                "Heat a large wok or frying pan until very hot, add a little oil, then cook the chicken for 4-5 minutes until lightly browned.",
                "Add the peppers and stir-fry for 2-3 minutes so they stay slightly crisp.",
                "Add cooked rice and soy sauce, tossing over high heat for 1-2 minutes until everything is coated and hot.",
                "Taste, adjust seasoning, and serve immediately."
            ],
            "salmon, potatoes, and broccoli" =>
            [
                "Heat oven to 210C (fan 190C).",
                "Cut potatoes into wedges, toss with oil, salt, and pepper, then roast for 20 minutes.",
                "Season salmon and broccoli, then add both to the tray.",
                "Roast for another 12-15 minutes, until the salmon flakes easily and potatoes are golden.",
                "Rest for 2 minutes, then serve with any pan juices."
            ],
            "turkey chilli with beans" =>
            [
                "Heat a deep pan with a little oil over medium heat for 2 minutes.",
                "Add turkey mince and cook until no pink remains, breaking it up with a spoon.",
                "Stir in chilli seasoning, then add chopped tomatoes, beans, and a small splash of water.",
                "Simmer uncovered for 20-25 minutes, stirring occasionally until thickened.",
                "Taste and adjust salt, pepper, or spice level before serving."
            ],
            "veggie lentil curry" =>
            [
                "Rinse the lentils until the water runs mostly clear.",
                "Warm curry paste in a pan with a little oil for 1-2 minutes until fragrant.",
                "Add lentils, coconut milk, and enough water to just cover; bring to a gentle simmer.",
                "Cook for 20-25 minutes, stirring now and then, until lentils are soft.",
                "Stir through spinach for 1-2 minutes until wilted, then season and serve."
            ],
            "tofu noodle bowls" =>
            [
                "Press tofu for 10 minutes to remove excess moisture, then cube it.",
                "Cook noodles according to pack instructions, drain, and rinse quickly with warm water.",
                "Pan-fry tofu in a little oil until golden on most sides, then set aside.",
                "Stir-fry carrots and any other veg for 2-3 minutes, add sauce, then return tofu to the pan.",
                "Add noodles and toss for 1-2 minutes until evenly coated and piping hot."
            ],
            "greek yogurt chicken wraps" =>
            [
                "Season chicken strips and cook in a hot pan for 6-8 minutes until cooked through.",
                "Mix Greek yogurt with a pinch of salt and pepper for a quick sauce.",
                "Warm the wraps briefly in a dry pan or microwave so they stay flexible.",
                "Layer lettuce, cooked chicken, and yogurt sauce onto each wrap.",
                "Roll tightly, slice in half, and serve."
            ],
            "paneer tikka tray bake" =>
            [
                "Heat oven to 200C (fan 180C).",
                "Cut paneer, onions, and peppers into similar-size chunks.",
                "Toss everything with tikka seasoning, a little oil, and salt.",
                "Spread on a tray in one layer and roast for 25 minutes, turning once halfway.",
                "Roast a few more minutes if needed for lightly charred edges, then serve."
            ],
            "prawn tomato pasta" =>
            [
                "Bring a large pan of salted water to the boil and cook pasta until al dente.",
                "In a separate pan, simmer passata for 6-8 minutes with a little salt and pepper.",
                "Add prawns and cook for 2-3 minutes until pink and just firm.",
                "Drain pasta, reserving a splash of water, then combine pasta with sauce and prawns.",
                "Loosen with reserved pasta water if needed, top with parmesan, and serve."
            ],
            "beef and veg rice bowls" =>
            [
                "Cook rice first and keep warm.",
                "Brown beef mince in a hot pan, breaking it up as it cooks.",
                "Add onions and cook for 3-4 minutes until softened, then add peas.",
                "Stir in cooked rice and any sauce/seasoning, then toss for 2-3 minutes until hot.",
                "Taste, adjust seasoning, and serve in bowls."
            ],
            "chickpea quinoa salad bowls" =>
            [
                "Rinse quinoa and cook according to pack instructions, then cool for 10 minutes.",
                "Drain chickpeas and chop cucumber and tomatoes into bite-size pieces.",
                "Whisk a simple dressing (olive oil, acid, salt, pepper).",
                "Combine quinoa, chickpeas, and veg in a large bowl, then pour over dressing.",
                "Toss well and chill for 10 minutes before serving."
            ],
            "egg fried rice" =>
            [
                "Cook rice ahead if possible and let it cool slightly so grains separate.",
                "Scramble eggs in a hot pan with a little oil, then remove and set aside.",
                "Cook mixed veg for 2-3 minutes, add rice, and stir-fry until hot.",
                "Add soy sauce and return eggs to the pan, breaking eggs up as you toss.",
                "Cook for 1-2 more minutes, then serve immediately."
            ],
            "baked cod with sweet potato wedges" =>
            [
                "Heat oven to 210C (fan 190C).",
                "Cut sweet potatoes into wedges, season, and roast for 20 minutes.",
                "Add cod and green beans to the tray, drizzle lightly with oil, and season.",
                "Roast for another 12-15 minutes until cod flakes easily and wedges are tender.",
                "Rest briefly, then serve."
            ],
            _ when mealName.Contains("stir fry", StringComparison.OrdinalIgnoreCase) =>
            [
                "Prep all vegetables and protein into even pieces before heating the pan.",
                "Cook your base carb (rice/noodles) first and keep warm.",
                "Stir-fry protein on high heat until just cooked, then remove temporarily.",
                "Cook vegetables quickly so they stay crisp, then return protein to the pan.",
                "Add sauce and carb, toss until hot, taste, and serve."
            ],
            _ when mealName.Contains("curry", StringComparison.OrdinalIgnoreCase) || mealName.Contains("chilli", StringComparison.OrdinalIgnoreCase) =>
            [
                "Warm spices or paste in a little oil over medium heat for 1-2 minutes until fragrant.",
                "Add protein or pulses and cook for 4-6 minutes, stirring so they are evenly coated.",
                "Pour in liquids, bring to a gentle simmer, and reduce heat to medium-low.",
                "Cook for 15-25 minutes, stirring every few minutes, until thickened and ingredients are tender.",
                "Taste, adjust seasoning, and rest for 2 minutes before serving."
            ],
            _ when mealName.Contains("salad", StringComparison.OrdinalIgnoreCase) =>
            [
                "Cook grains or protein first if needed and let them cool slightly.",
                "Prepare all fresh vegetables and herbs.",
                "Mix a quick dressing in a separate bowl.",
                "Combine ingredients in a large bowl and toss with dressing.",
                "Rest for 5-10 minutes so flavours settle before serving."
            ],
            _ when mealName.Contains("baked", StringComparison.OrdinalIgnoreCase) || mealName.Contains("tray bake", StringComparison.OrdinalIgnoreCase) =>
            [
                "Preheat oven to 200C (fan 180C) and line a tray.",
                "Season vegetables first and roast for 12-15 minutes to give them a head start.",
                "Add protein or main component, then roast for another 12-18 minutes until cooked through.",
                "Turn ingredients halfway for even colour and texture.",
                "Rest for 2 minutes, spoon over pan juices, and serve."
            ],
            _ =>
            [
                "Prep and portion ingredients before you start, keeping similar-size pieces for even cooking.",
                "Heat a large pan over medium-high heat for 2 minutes and add a little oil.",
                "Cook the main protein or veg base for 6-8 minutes, stirring, until browned and nearly cooked through.",
                "Add remaining ingredients plus a small splash of water or stock, then cook for 4-6 minutes until the sauce coats.",
                "Taste, adjust seasoning, and rest for 1-2 minutes before serving warm."
            ]
        };
    }

    private static bool AreAiRecipeStepsDetailed(
        IReadOnlyList<string> recipeSteps,
        MealTemplate template)
    {
        if (recipeSteps.Count < 5)
        {
            return false;
        }

        var ingredientTerms = template.Ingredients
            .SelectMany(ingredient => BuildIngredientSearchTerms(ingredient.Name))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var actionStepCount = recipeSteps.Count(step =>
            RecipeActionKeywords.Any(keyword => ContainsWholeWord(step, keyword)));
        var concreteStepCount = recipeSteps.Count(step =>
            RecipeConcreteCueRegex.IsMatch(step) ||
            ingredientTerms.Any(term => ContainsWholeWord(step, term)));
        var weakStepCount = recipeSteps.Count(IsWeakRecipeStep);

        return actionStepCount >= 4 &&
               concreteStepCount >= 3 &&
               weakStepCount <= 1;
    }

    private static bool IsWeakRecipeStep(string step)
    {
        var normalized = step?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return WeakRecipeStepPhrases.Any(phrase =>
            normalized.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static MealNutritionEstimate EstimateMealNutritionPerServing(
        MealTemplate template,
        decimal portionSizeFactor)
    {
        var deterministic = EstimateDeterministicMealNutritionPerServing(template, portionSizeFactor);
        var ai = EstimateAiMealNutritionForPortion(template, portionSizeFactor);
        if (ai is null)
        {
            deterministic.SourceLabel = "Ingredient estimate";
            return deterministic;
        }

        return BlendMealNutritionEstimates(deterministic, ai);
    }

    private static MealNutritionEstimate EstimateDeterministicMealNutritionPerServing(
        MealTemplate template,
        decimal portionSizeFactor)
    {
        var safePortionFactor = Math.Clamp(portionSizeFactor, 0.75m, 1.35m);
        var caloriesForTwo = 0m;
        var proteinForTwo = 0m;
        var carbsForTwo = 0m;
        var fatForTwo = 0m;
        var ingredientCount = 0;
        var qualitySum = 0m;

        foreach (var ingredient in template.Ingredients)
        {
            ingredientCount++;
            var reference = ResolveNutritionReference(ingredient, out var mappingQuality);
            qualitySum += mappingQuality;
            var grams = ConvertIngredientQuantityToGrams(ingredient.QuantityForTwo, ingredient.Unit, reference.GramsPerUnit);
            if (grams <= 0m)
            {
                continue;
            }
            grams *= ResolveIngredientNutritionConsumptionFactor(ingredient);

            var scale = grams / 100m;
            caloriesForTwo += reference.CaloriesPer100g * scale;
            proteinForTwo += reference.ProteinPer100g * scale;
            carbsForTwo += reference.CarbsPer100g * scale;
            fatForTwo += reference.FatPer100g * scale;
        }

        if (caloriesForTwo <= 0m)
        {
            var fallback = BuildFallbackMealNutritionEstimate(template, safePortionFactor);
            fallback.ConfidenceScore = 0.35m;
            fallback.SourceLabel = "Fallback estimate";
            return fallback;
        }

        var caloriesPerServing = (caloriesForTwo / 2m) * safePortionFactor;
        var proteinPerServing = (proteinForTwo / 2m) * safePortionFactor;
        var carbsPerServing = (carbsForTwo / 2m) * safePortionFactor;
        var fatPerServing = (fatForTwo / 2m) * safePortionFactor;
        var averageMappingQuality = ingredientCount == 0
            ? 0.35m
            : qualitySum / ingredientCount;

        var roundedCalories = (int)Math.Round(caloriesPerServing, MidpointRounding.AwayFromZero);
        return new MealNutritionEstimate
        {
            CaloriesPerServing = Math.Clamp(roundedCalories, 220, 1350),
            ProteinGramsPerServing = Math.Clamp(decimal.Round(proteinPerServing, 1, MidpointRounding.AwayFromZero), 1m, 120m),
            CarbsGramsPerServing = Math.Clamp(decimal.Round(carbsPerServing, 1, MidpointRounding.AwayFromZero), 1m, 170m),
            FatGramsPerServing = Math.Clamp(decimal.Round(fatPerServing, 1, MidpointRounding.AwayFromZero), 1m, 95m),
            ConfidenceScore = Math.Clamp(0.45m + (averageMappingQuality * 0.5m), 0.45m, 0.95m),
            SourceLabel = "Ingredient estimate"
        };
    }

    private static MealNutritionEstimate? EstimateAiMealNutritionForPortion(
        MealTemplate template,
        decimal portionSizeFactor)
    {
        var ai = template.AiNutritionPerServingMedium;
        if (ai is null)
        {
            return null;
        }

        var safePortionFactor = Math.Clamp(portionSizeFactor, 0.75m, 1.35m);
        var calories = Math.Round(ai.CaloriesPerServingMedium * safePortionFactor, MidpointRounding.AwayFromZero);
        var protein = ai.ProteinGramsPerServingMedium * safePortionFactor;
        var carbs = ai.CarbsGramsPerServingMedium * safePortionFactor;
        var fat = ai.FatGramsPerServingMedium * safePortionFactor;

        return new MealNutritionEstimate
        {
            CaloriesPerServing = Math.Clamp((int)calories, 180, 1500),
            ProteinGramsPerServing = Math.Clamp(decimal.Round(protein, 1, MidpointRounding.AwayFromZero), 1m, 130m),
            CarbsGramsPerServing = Math.Clamp(decimal.Round(carbs, 1, MidpointRounding.AwayFromZero), 1m, 200m),
            FatGramsPerServing = Math.Clamp(decimal.Round(fat, 1, MidpointRounding.AwayFromZero), 1m, 120m),
            ConfidenceScore = Math.Clamp(ai.ConfidenceScore, 0.40m, 0.85m),
            SourceLabel = "AI nutrition"
        };
    }

    private static MealNutritionEstimate BlendMealNutritionEstimates(
        MealNutritionEstimate deterministic,
        MealNutritionEstimate ai)
    {
        if (!IsAiNutritionCompatibleWithDeterministic(deterministic, ai))
        {
            deterministic.SourceLabel = "Ingredient estimate";
            return deterministic;
        }

        var deterministicWeight = Math.Clamp(deterministic.ConfidenceScore, 0.45m, 0.95m);
        var aiWeight = Math.Clamp(ai.ConfidenceScore, 0.40m, 0.85m);
        var totalWeight = deterministicWeight + aiWeight;
        if (totalWeight <= 0m)
        {
            deterministic.SourceLabel = "Ingredient estimate";
            return deterministic;
        }

        var blendedCalories = (
            (deterministic.CaloriesPerServing * deterministicWeight) +
            (ai.CaloriesPerServing * aiWeight)) / totalWeight;
        var blendedProtein = (
            (deterministic.ProteinGramsPerServing * deterministicWeight) +
            (ai.ProteinGramsPerServing * aiWeight)) / totalWeight;
        var blendedCarbs = (
            (deterministic.CarbsGramsPerServing * deterministicWeight) +
            (ai.CarbsGramsPerServing * aiWeight)) / totalWeight;
        var blendedFat = (
            (deterministic.FatGramsPerServing * deterministicWeight) +
            (ai.FatGramsPerServing * aiWeight)) / totalWeight;

        return new MealNutritionEstimate
        {
            CaloriesPerServing = Math.Clamp((int)Math.Round(blendedCalories, MidpointRounding.AwayFromZero), 200, 1450),
            ProteinGramsPerServing = Math.Clamp(decimal.Round(blendedProtein, 1, MidpointRounding.AwayFromZero), 1m, 125m),
            CarbsGramsPerServing = Math.Clamp(decimal.Round(blendedCarbs, 1, MidpointRounding.AwayFromZero), 1m, 190m),
            FatGramsPerServing = Math.Clamp(decimal.Round(blendedFat, 1, MidpointRounding.AwayFromZero), 1m, 110m),
            ConfidenceScore = Math.Clamp((deterministicWeight + aiWeight) / 2m, 0.50m, 0.92m),
            SourceLabel = "Ingredient + AI blend"
        };
    }

    private static bool IsAiNutritionCompatibleWithDeterministic(
        MealNutritionEstimate deterministic,
        MealNutritionEstimate ai)
    {
        var calorieRatio = ai.CaloriesPerServing / (decimal)Math.Max(1, deterministic.CaloriesPerServing);
        if (calorieRatio < 0.55m || calorieRatio > 1.80m)
        {
            return false;
        }

        var proteinRatio = ai.ProteinGramsPerServing / Math.Max(1m, deterministic.ProteinGramsPerServing);
        var carbsRatio = ai.CarbsGramsPerServing / Math.Max(1m, deterministic.CarbsGramsPerServing);
        var fatRatio = ai.FatGramsPerServing / Math.Max(1m, deterministic.FatGramsPerServing);
        return proteinRatio >= 0.45m && proteinRatio <= 2.10m &&
               carbsRatio >= 0.45m && carbsRatio <= 2.10m &&
               fatRatio >= 0.45m && fatRatio <= 2.10m;
    }

    private static MealNutritionEstimate BuildFallbackMealNutritionEstimate(
        MealTemplate template,
        decimal portionSizeFactor)
    {
        var calories = Math.Clamp(
            (int)Math.Round(template.BaseCostForTwo * 118m * portionSizeFactor, MidpointRounding.AwayFromZero),
            320,
            1100);
        var highProtein = template.Tags.Contains("High-Protein", StringComparer.OrdinalIgnoreCase);
        var plantBased = template.Tags.Contains("Vegan", StringComparer.OrdinalIgnoreCase) ||
                         template.Tags.Contains("Vegetarian", StringComparer.OrdinalIgnoreCase);

        var protein = highProtein ? 38m : plantBased ? 20m : 30m;
        protein *= portionSizeFactor;
        var fat = (calories * 0.32m) / 9m;
        var carbs = (calories - (protein * 4m) - (fat * 9m)) / 4m;

        return new MealNutritionEstimate
        {
            CaloriesPerServing = calories,
            ProteinGramsPerServing = decimal.Round(Math.Clamp(protein, 12m, 80m), 1, MidpointRounding.AwayFromZero),
            CarbsGramsPerServing = decimal.Round(Math.Clamp(carbs, 20m, 150m), 1, MidpointRounding.AwayFromZero),
            FatGramsPerServing = decimal.Round(Math.Clamp(fat, 10m, 70m), 1, MidpointRounding.AwayFromZero),
            SourceLabel = "Fallback estimate"
        };
    }

    private static NutritionReference ResolveNutritionReference(
        IngredientTemplate ingredient,
        out decimal mappingQuality)
    {
        if (IngredientNutritionReferences.TryGetValue(ingredient.Name, out var exact))
        {
            mappingQuality = 1m;
            return exact;
        }

        var normalizedName = ingredient.Name.Trim();
        foreach (var pair in IngredientNutritionReferences)
        {
            if (normalizedName.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                mappingQuality = 0.8m;
                return pair.Value;
            }
        }

        if (DepartmentNutritionFallbacks.TryGetValue(ingredient.Department, out var departmentFallback))
        {
            mappingQuality = 0.45m;
            return departmentFallback;
        }

        mappingQuality = 0.35m;
        return DepartmentNutritionFallbacks["Other"];
    }

    private static decimal ResolveIngredientNutritionConsumptionFactor(IngredientTemplate ingredient)
    {
        var normalizedName = ingredient.Name.Trim();
        foreach (var pair in IngredientNutritionConsumptionFactors)
        {
            if (normalizedName.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return 1m;
    }

    private static decimal ConvertIngredientQuantityToGrams(
        decimal quantity,
        string unit,
        decimal? gramsPerUnit)
    {
        if (quantity <= 0m)
        {
            return 0m;
        }

        var normalizedUnit = (unit ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedUnit switch
        {
            "kg" => quantity * 1000m,
            "g" => quantity,
            "l" => quantity * 1000m,
            "ml" => quantity,
            _ => quantity * ResolveDefaultUnitWeightGrams(normalizedUnit, gramsPerUnit)
        };
    }

    private static decimal ResolveDefaultUnitWeightGrams(string unit, decimal? explicitUnitWeightGrams)
    {
        if (explicitUnitWeightGrams is > 0m)
        {
            return explicitUnitWeightGrams.Value;
        }

        return unit switch
        {
            "pcs" => 80m,
            "tin" or "tins" => 240m,
            "pack" or "packs" => 250m,
            "jar" or "jars" => 190m,
            "bottle" or "bottles" => 500m,
            "head" => 500m,
            "fillets" => 140m,
            "balls" => 125m,
            _ => 100m
        };
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

    private static IReadOnlyList<MealTemplate> SelectMeals(
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
        var candidates = FilterMeals(dietaryModes, dislikesOrAllergens, mealSource)
            .Select(meal => EnsureMealTypeSuitability(meal))
            .ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "No meals match the selected dietary modes and dislikes/allergens.");
        }

        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        var safeMealsPerDay = resolvedMealTypeSlots.Count;
        var targetMealCost = weeklyBudget / (7m * safeMealsPerDay);
        var preferHighProtein = IsHighProteinPreferred(dietaryModes);
        var scoredCandidates = candidates
            .Select(template =>
            {
                var scaledCost = template.BaseCostForTwo * householdFactor;
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

        var normalizedMealCount = NormalizeRequestedMealCount(requestedMealCount);
        var selected = new List<MealTemplate>(normalizedMealCount);
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
                .Where(meal => SupportsMealType(meal, slotMealType))
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
            var candidate = SelectCandidateForSlot(
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
            var treatApplied = TryApplySpecialTreatMeal(
                selected,
                rotatedCandidates,
                resolvedMealTypeSlots,
                householdFactor,
                selectedSpecialTreatCookDayIndex);
            if (!treatApplied || !HasSpecialTreatDinner(selected, resolvedMealTypeSlots))
            {
                throw new InvalidOperationException(
                    "No indulgent special treat dinner was available for the current settings.");
            }
        }

        return selected;
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

    private static IReadOnlyList<string> ResolveHardDietaryModes(IReadOnlyList<string> dietaryModes)
    {
        return dietaryModes
            .Where(mode =>
                !mode.Equals("Balanced", StringComparison.OrdinalIgnoreCase) &&
                !mode.Equals("High-Protein", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsHighProteinPreferred(IReadOnlyList<string> dietaryModes)
    {
        return dietaryModes.Any(mode => mode.Equals("High-Protein", StringComparison.OrdinalIgnoreCase));
    }

    private static List<MealTemplate> FilterMeals(
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

    private static IReadOnlyList<MealTemplate> GetCompatibleAiPoolMeals(
        IReadOnlyList<string> dietaryModes,
        string dislikesOrAllergens)
    {
        PruneAiMealPool(DateTime.UtcNow);
        return FilterMeals(dietaryModes, dislikesOrAllergens, AiMealPool.Values.ToList());
    }

    private static void AddMealsToAiPool(IReadOnlyList<MealTemplate> meals)
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

    private static bool ContainsToken(MealTemplate meal, string token)
    {
        if (meal.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return meal.Ingredients.Any(ingredient =>
            ingredient.Name.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static AislePilotPantrySuggestionViewModel BuildPantrySuggestion(
        MealTemplate template,
        IReadOnlyList<string> pantryTokens,
        decimal householdFactor)
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
        var missingIngredientsEstimatedCost = decimal.Round(
            template.Ingredients
                .Where(ingredient => missing.Contains(ingredient.Name, StringComparer.OrdinalIgnoreCase))
                .Sum(ingredient => ingredient.EstimatedCostForTwo * householdFactor),
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

    private static IReadOnlyList<string> ParsePantryTokens(string? pantryItems)
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

    private static int CountMatchedPantryTokens(MealTemplate template, IReadOnlyList<string> pantryTokens)
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

    private static int ComputePantrySuggestionScore(
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

    private static IReadOnlyList<PantrySuggestionCandidate> RankPantrySuggestionCandidates(
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

    private static IReadOnlyList<string> ParseSpecificPantryTokens(IReadOnlyList<string> pantryTokens)
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

    private static IReadOnlyList<string> MergePantryTokensWithAssumedBasics(IReadOnlyList<string> pantryTokens)
    {
        var merged = new HashSet<string>(pantryTokens, StringComparer.OrdinalIgnoreCase);
        foreach (var assumedBasic in AssumedPantryBasics)
        {
            merged.Add(assumedBasic);
        }

        return merged.ToList();
    }

    private static bool TemplateUsesCoreIngredientsFromUserPantry(
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

    private static IReadOnlyList<string> NormalizeMealTypeSlots(
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

    private static MealTemplate EnsureMealTypeSuitability(
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

    private static bool SupportsMealType(MealTemplate meal, string mealType)
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

    private static bool IsSpecialTreatMealCandidate(MealTemplate meal)
    {
        if (meal.Tags.Contains("Special Treat", StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return SpecialTreatNameKeywords.Any(keyword =>
            meal.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryApplySpecialTreatMeal(
        IList<MealTemplate> selectedMeals,
        IReadOnlyList<MealTemplate> candidateMeals,
        IReadOnlyList<string> resolvedMealTypeSlots,
        decimal householdFactor,
        int? selectedSpecialTreatCookDayIndex)
    {
        if (selectedMeals.Count == 0 || candidateMeals.Count == 0 || resolvedMealTypeSlots.Count == 0)
        {
            return false;
        }

        var dinnerSlotIndexes = Enumerable.Range(0, selectedMeals.Count)
            .Where(index =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count]
                    .Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dinnerSlotIndexes.Count == 0)
        {
            return false;
        }

        var targetDinnerIndex = ResolveTargetDinnerIndex(
            dinnerSlotIndexes,
            selectedMeals,
            resolvedMealTypeSlots,
            householdFactor,
            selectedSpecialTreatCookDayIndex);
        var currentDinnerMeal = selectedMeals[targetDinnerIndex];
        if (IsSpecialTreatMealCandidate(currentDinnerMeal))
        {
            selectedMeals[targetDinnerIndex] = MarkMealAsSpecialTreat(currentDinnerMeal);
            return true;
        }

        var existingTreatDinnerIndex = dinnerSlotIndexes
            .Where(index => index != targetDinnerIndex && IsSpecialTreatMealCandidate(selectedMeals[index]))
            .Cast<int?>()
            .FirstOrDefault();
        if (existingTreatDinnerIndex.HasValue)
        {
            var sourceTreatDinnerIndex = existingTreatDinnerIndex.Value;
            var targetMeal = selectedMeals[targetDinnerIndex];
            selectedMeals[targetDinnerIndex] = MarkMealAsSpecialTreat(selectedMeals[sourceTreatDinnerIndex]);
            selectedMeals[sourceTreatDinnerIndex] = targetMeal;
            return true;
        }

        var usedMealNames = selectedMeals
            .Where((_, index) => index != targetDinnerIndex)
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dinnerCosts = dinnerSlotIndexes
            .Select(index => CalculateScaledMealCost(selectedMeals[index], householdFactor, dayMultiplier: 1))
            .ToList();
        var averageDinnerCost = dinnerCosts.Count == 0
            ? 0m
            : dinnerCosts.Average();

        var rankedTreatCandidates = candidateMeals
            .Select(meal => EnsureMealTypeSuitability(meal))
            .Where(meal => SupportsMealType(meal, "Dinner"))
            .Where(IsSpecialTreatMealCandidate)
            .Where(meal => !usedMealNames.Contains(meal.Name))
            .Select(meal => new
            {
                Meal = meal,
                Cost = CalculateScaledMealCost(meal, householdFactor, dayMultiplier: 1)
            })
            .OrderByDescending(entry => entry.Cost)
            .ThenBy(entry => entry.Meal.IsQuick ? 1 : 0)
            .ThenBy(entry => entry.Meal.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var treatCandidates = rankedTreatCandidates
            .Where(entry => IsSpecialTreatMealCandidate(entry.Meal))
            .ToList();
        var premiumThreshold = decimal.Round(averageDinnerCost * 1.12m, 2, MidpointRounding.AwayFromZero);
        var replacement = treatCandidates
            .Where(entry => entry.Cost >= premiumThreshold)
            .Select(entry => entry.Meal)
            .FirstOrDefault(meal => !meal.Name.Equals(currentDinnerMeal.Name, StringComparison.OrdinalIgnoreCase));
        replacement ??= treatCandidates
            .Select(entry => entry.Meal)
            .FirstOrDefault(meal => !meal.Name.Equals(currentDinnerMeal.Name, StringComparison.OrdinalIgnoreCase));
        replacement ??= treatCandidates
            .Where(entry => entry.Cost >= premiumThreshold)
            .Select(entry => entry.Meal)
            .FirstOrDefault();
        replacement ??= treatCandidates
            .Select(entry => entry.Meal)
            .FirstOrDefault();

        if (replacement is null)
        {
            return false;
        }

        selectedMeals[targetDinnerIndex] = MarkMealAsSpecialTreat(replacement);
        return true;
    }

    private static int ResolveTargetDinnerIndex(
        IReadOnlyList<int> dinnerSlotIndexes,
        IList<MealTemplate> selectedMeals,
        IReadOnlyList<string> resolvedMealTypeSlots,
        decimal householdFactor,
        int? selectedSpecialTreatCookDayIndex)
    {
        var preferredDinnerIndex = ResolvePreferredSpecialTreatDinnerIndex(
            dinnerSlotIndexes,
            resolvedMealTypeSlots.Count,
            selectedSpecialTreatCookDayIndex);
        if (preferredDinnerIndex.HasValue)
        {
            return preferredDinnerIndex.Value;
        }

        return dinnerSlotIndexes
            .OrderBy(index => CalculateScaledMealCost(selectedMeals[index], householdFactor, dayMultiplier: 1))
            .ThenBy(index => index)
            .First();
    }

    private static int? ResolvePreferredSpecialTreatDinnerIndex(
        IReadOnlyList<int> dinnerSlotIndexes,
        int mealsPerDay,
        int? selectedSpecialTreatCookDayIndex)
    {
        if (!selectedSpecialTreatCookDayIndex.HasValue || mealsPerDay <= 0)
        {
            return null;
        }

        var preferredCookDayIndex = selectedSpecialTreatCookDayIndex.Value;
        if (preferredCookDayIndex < 0)
        {
            return null;
        }

        foreach (var dinnerSlotIndex in dinnerSlotIndexes)
        {
            var cookDayIndex = dinnerSlotIndex / mealsPerDay;
            if (cookDayIndex == preferredCookDayIndex)
            {
                return dinnerSlotIndex;
            }
        }

        return null;
    }

    private static bool HasSpecialTreatDinner(
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<string> mealTypeSlots)
    {
        if (selectedMeals.Count == 0 || mealTypeSlots.Count == 0)
        {
            return false;
        }

        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        return Enumerable.Range(0, selectedMeals.Count)
            .Where(index =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count]
                    .Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .Any(index => IsSpecialTreatMealCandidate(selectedMeals[index]));
    }

    private static int? ResolveSpecialTreatDisplayMealIndex(
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<string> mealTypeSlots,
        int? selectedSpecialTreatCookDayIndex)
    {
        if (selectedMeals.Count == 0 || mealTypeSlots.Count == 0)
        {
            return null;
        }

        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        var dinnerSlotIndexes = Enumerable.Range(0, selectedMeals.Count)
            .Where(index =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count]
                    .Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dinnerSlotIndexes.Count == 0)
        {
            return null;
        }

        var preferredDinnerIndex = ResolvePreferredSpecialTreatDinnerIndex(
            dinnerSlotIndexes,
            resolvedMealTypeSlots.Count,
            selectedSpecialTreatCookDayIndex);
        if (preferredDinnerIndex.HasValue &&
            IsSpecialTreatMealCandidate(selectedMeals[preferredDinnerIndex.Value]))
        {
            return preferredDinnerIndex.Value;
        }

        var firstTaggedDinnerIndex = dinnerSlotIndexes
            .FirstOrDefault(index =>
                selectedMeals[index].Tags.Contains("Special Treat", StringComparer.OrdinalIgnoreCase));
        if (firstTaggedDinnerIndex >= 0 &&
            firstTaggedDinnerIndex < selectedMeals.Count &&
            selectedMeals[firstTaggedDinnerIndex].Tags.Contains("Special Treat", StringComparer.OrdinalIgnoreCase))
        {
            return firstTaggedDinnerIndex;
        }

        var firstCandidateDinnerIndex = dinnerSlotIndexes
            .Cast<int?>()
            .FirstOrDefault(index => index.HasValue && IsSpecialTreatMealCandidate(selectedMeals[index.Value]));
        return firstCandidateDinnerIndex;
    }

    private static bool ForceReplaceDinnerWithSpecialTreatMeal(
        IList<MealTemplate> selectedMeals,
        MealTemplate specialTreatMeal,
        IReadOnlyList<string> mealTypeSlots,
        decimal householdFactor,
        int? selectedSpecialTreatCookDayIndex)
    {
        if (selectedMeals.Count == 0 || mealTypeSlots.Count == 0)
        {
            return false;
        }

        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        var dinnerSlotIndexes = Enumerable.Range(0, selectedMeals.Count)
            .Where(index =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count]
                    .Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dinnerSlotIndexes.Count == 0)
        {
            return false;
        }

        var targetDinnerIndex = ResolveTargetDinnerIndex(
            dinnerSlotIndexes,
            selectedMeals,
            resolvedMealTypeSlots,
            householdFactor,
            selectedSpecialTreatCookDayIndex);

        selectedMeals[targetDinnerIndex] = MarkMealAsSpecialTreat(EnsureMealTypeSuitability(specialTreatMeal));
        return true;
    }

    private static MealTemplate MarkMealAsSpecialTreat(MealTemplate meal)
    {
        var tags = meal.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!tags.Contains("Special Treat", StringComparer.OrdinalIgnoreCase))
        {
            tags.Add("Special Treat");
        }

        return meal with { Tags = tags };
    }

    private static MealTemplate SelectCandidateForSlot(
        IReadOnlyList<MealTemplate> slotCompatibleCandidates,
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<string> resolvedMealTypeSlots,
        int slotIndex,
        int totalSlotCount,
        IReadOnlySet<string>? preferredMealNames = null,
        bool shouldPreferPreferredMeals = false)
    {
        if (slotCompatibleCandidates.Count == 0)
        {
            throw new InvalidOperationException("No slot-compatible meals are available for selection.");
        }

        var slotMealType = resolvedMealTypeSlots[slotIndex % resolvedMealTypeSlots.Count];
        var isDinnerSlot = slotMealType.Equals("Dinner", StringComparison.OrdinalIgnoreCase);
        var slotTypeSlotCount = CountSlotsForMealType(resolvedMealTypeSlots, slotMealType, totalSlotCount);
        var maxRepeatsForSlotType = ResolveMaxMealRepeatsForSlotType(slotMealType, slotTypeSlotCount);
        var usedMealNames = selectedMeals
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedDinnerMealNames = selectedMeals
            .Where((_, index) =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count].Equals("Dinner", StringComparison.OrdinalIgnoreCase))
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedSlotMealTypeCounts = selectedMeals
            .Where((_, index) =>
                resolvedMealTypeSlots[index % resolvedMealTypeSlots.Count].Equals(slotMealType, StringComparison.OrdinalIgnoreCase))
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        int GetSlotTypeRepeatCount(MealTemplate meal)
        {
            return usedSlotMealTypeCounts.TryGetValue(meal.Name, out var count) ? count : 0;
        }

        string? GetMostRecentSlotMealName()
        {
            for (var selectedIndex = selectedMeals.Count - 1; selectedIndex >= 0; selectedIndex--)
            {
                if (resolvedMealTypeSlots[selectedIndex % resolvedMealTypeSlots.Count].Equals(
                        slotMealType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return selectedMeals[selectedIndex].Name;
                }
            }

            return null;
        }

        if (shouldPreferPreferredMeals && preferredMealNames is { Count: > 0 })
        {
            MealTemplate? FindPreferredCandidate(Func<MealTemplate, bool> predicate)
            {
                return slotCompatibleCandidates.FirstOrDefault(meal =>
                    preferredMealNames.Contains(meal.Name) &&
                    predicate(meal));
            }

            var preferredSavedCandidate = FindPreferredCandidate(meal =>
                GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
                (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)) &&
                (selectedMeals.Count == 0 ||
                 !meal.Name.Equals(selectedMeals[^1].Name, StringComparison.OrdinalIgnoreCase)));
            if (preferredSavedCandidate is not null)
            {
                return preferredSavedCandidate;
            }

            preferredSavedCandidate = FindPreferredCandidate(meal =>
                !usedMealNames.Contains(meal.Name) &&
                GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
                (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)));
            if (preferredSavedCandidate is not null)
            {
                return preferredSavedCandidate;
            }
        }

        var preferredCandidate = slotCompatibleCandidates.FirstOrDefault(meal =>
            !usedMealNames.Contains(meal.Name) &&
            GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
            (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)));
        if (preferredCandidate is not null)
        {
            return preferredCandidate;
        }

        var cappedRepeatCandidate = slotCompatibleCandidates.FirstOrDefault(meal =>
            GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
            (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)));
        if (cappedRepeatCandidate is not null)
        {
            return cappedRepeatCandidate;
        }

        if (!isDinnerSlot)
        {
            var previousName = selectedMeals.Count > 0 ? selectedMeals[^1].Name : null;
            var nonAdjacentCandidate = slotCompatibleCandidates
                .FirstOrDefault(meal =>
                    GetSlotTypeRepeatCount(meal) < maxRepeatsForSlotType &&
                    (previousName is null || !meal.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase)));
            if (nonAdjacentCandidate is not null)
            {
                return nonAdjacentCandidate;
            }
        }

        var previousMealName = selectedMeals.Count > 0 ? selectedMeals[^1].Name : null;
        var mostRecentSlotMealName = GetMostRecentSlotMealName();
        var leastRepeatedCandidates = slotCompatibleCandidates
            .OrderBy(GetSlotTypeRepeatCount)
            .ThenBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var balancedFallback = leastRepeatedCandidates.FirstOrDefault(meal =>
            (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)) &&
            (mostRecentSlotMealName is null || !meal.Name.Equals(mostRecentSlotMealName, StringComparison.OrdinalIgnoreCase)) &&
            (previousMealName is null || !meal.Name.Equals(previousMealName, StringComparison.OrdinalIgnoreCase)));
        if (balancedFallback is not null)
        {
            return balancedFallback;
        }

        balancedFallback = leastRepeatedCandidates.FirstOrDefault(meal =>
            (!isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name)) &&
            (mostRecentSlotMealName is null || !meal.Name.Equals(mostRecentSlotMealName, StringComparison.OrdinalIgnoreCase)));
        if (balancedFallback is not null)
        {
            return balancedFallback;
        }

        balancedFallback = leastRepeatedCandidates.FirstOrDefault(meal =>
            !isDinnerSlot || !usedDinnerMealNames.Contains(meal.Name));
        if (balancedFallback is not null)
        {
            return balancedFallback;
        }

        return leastRepeatedCandidates[0];
    }

    private static int CountSlotsForMealType(
        IReadOnlyList<string> resolvedMealTypeSlots,
        string mealType,
        int totalSlotCount)
    {
        if (resolvedMealTypeSlots.Count == 0 || totalSlotCount <= 0)
        {
            return 0;
        }

        var normalizedTotalSlotCount = Math.Max(1, totalSlotCount);
        var count = 0;
        for (var i = 0; i < normalizedTotalSlotCount; i++)
        {
            if (resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count].Equals(mealType, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static int ResolveMaxMealRepeatsForSlotType(string mealType, int slotCount)
    {
        var normalizedMealType = NormalizeMealType(mealType);
        var normalizedSlotCount = Math.Max(0, slotCount);
        if (normalizedMealType.Equals("Dinner", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        // Breakfast/lunch can repeat, but avoid a single meal dominating the week.
        return normalizedSlotCount >= 4 ? 2 : 1;
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
