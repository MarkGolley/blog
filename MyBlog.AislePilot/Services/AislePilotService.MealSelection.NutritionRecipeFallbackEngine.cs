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
    private static readonly string[] ColdBreakfastContradictoryCookingTerms =
    [
        "hob",
        "pan",
        "skillet",
        "wok",
        "oven",
        "roast",
        "bake",
        "simmer",
        "boil",
        "fry",
        "stock",
        "broth"
    ];

    internal static IReadOnlyList<string> BuildRecipeSteps(MealTemplate template)
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

        if (IsColdBreakfastMeal(template, mealName, ingredientNames))
        {
            return BuildColdBreakfastRecipeSteps(ingredientNames);
        }

        if (mealName.Contains("muffin", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "Heat oven to 190C (fan 170C) and lightly grease a muffin tin.",
                $"Whisk {primaryIngredient} until smooth, then fold through {secondaryIngredient} and {thirdIngredient}.",
                "Season lightly, divide the mixture between the muffin holes, and level the tops.",
                "Bake for 18-22 minutes until puffed and just set in the centre.",
                "Leave to cool for 5 minutes before lifting out; serve warm or chill for later."
            ];
        }

        if (mealName.Contains("scramble", StringComparison.OrdinalIgnoreCase) ||
            mealName.Contains("scrambled", StringComparison.OrdinalIgnoreCase) ||
            mealName.Contains("omelette", StringComparison.OrdinalIgnoreCase) ||
            mealName.Contains("omelet", StringComparison.OrdinalIgnoreCase))
        {
            var scrambleBase = ingredientNames.FirstOrDefault(name => name.Contains("egg", StringComparison.OrdinalIgnoreCase)) ??
                               ingredientNames.FirstOrDefault(name => name.Contains("tofu", StringComparison.OrdinalIgnoreCase)) ??
                               primaryIngredient;
            var scrambleSupportingIngredients = ingredientNames
                .Where(name => !name.Equals(scrambleBase, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var firstSupportingIngredient = scrambleSupportingIngredients.ElementAtOrDefault(0) ?? secondaryIngredient;
            var secondSupportingIngredient = scrambleSupportingIngredients.ElementAtOrDefault(1) ?? thirdIngredient;
            var finalIngredient = scrambleSupportingIngredients.ElementAtOrDefault(2);
            var hasEggBase = scrambleBase.Contains("egg", StringComparison.OrdinalIgnoreCase);
            var basePreparationStep = hasEggBase
                ? $"Crack and beat {scrambleBase} until smooth, then prep {firstSupportingIngredient} and {secondSupportingIngredient}."
                : $"Crumble {scrambleBase} into small pieces, then prep {firstSupportingIngredient} and {secondSupportingIngredient}.";
            var finalStep = string.IsNullOrWhiteSpace(finalIngredient)
                ? "Serve straight away while the scramble is still soft and hot."
                : $"Fold through {finalIngredient} right at the end, then serve straight away.";

            return
            [
                basePreparationStep,
                "Heat a non-stick pan over medium heat for 1-2 minutes with a little oil.",
                $"Cook {firstSupportingIngredient} and {secondSupportingIngredient} for 2-3 minutes until softened.",
                hasEggBase
                    ? $"Pour in {scrambleBase} and stir gently for 2-4 minutes until softly set."
                    : $"Add {scrambleBase} and stir for 3-4 minutes until heated through and lightly golden in places.",
                finalStep
            ];
        }

        if (mealName.Contains("toast", StringComparison.OrdinalIgnoreCase))
        {
            var toastBase = ingredientNames.FirstOrDefault(name => name.Contains("bread", StringComparison.OrdinalIgnoreCase)) ??
                            ingredientNames.FirstOrDefault(name => name.Contains("toast", StringComparison.OrdinalIgnoreCase)) ??
                            primaryIngredient;
            var toastTopping = ingredientNames.FirstOrDefault(name => name.Contains("egg", StringComparison.OrdinalIgnoreCase)) ??
                               ingredientNames.FirstOrDefault(name =>
                                   !name.Equals(toastBase, StringComparison.OrdinalIgnoreCase) &&
                                   !name.Contains("milk", StringComparison.OrdinalIgnoreCase)) ??
                               secondaryIngredient;
            var toastFinishingIngredient = ingredientNames.FirstOrDefault(name =>
                                              !name.Equals(toastBase, StringComparison.OrdinalIgnoreCase) &&
                                              !name.Equals(toastTopping, StringComparison.OrdinalIgnoreCase) &&
                                              !name.Contains("milk", StringComparison.OrdinalIgnoreCase)) ??
                                          thirdIngredient;

            return
            [
                $"Toast {toastBase} until golden and crisp, then keep it warm.",
                $"Prep {toastTopping} and {toastFinishingIngredient} so they are ready to go as soon as the toast is done.",
                "Heat a non-stick pan over medium-low heat with a little oil or butter.",
                $"Cook {toastTopping} gently until ready, then pile it over {toastBase}.",
                $"Finish with {toastFinishingIngredient} and serve immediately."
            ];
        }

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

        if (IsColdBreakfastMeal(template) &&
            recipeSteps.Any(step =>
                ColdBreakfastContradictoryCookingTerms.Any(term =>
                    ContainsWholeWord(step, term))))
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

    private static bool IsColdBreakfastMeal(
        MealTemplate template,
        string? mealName = null,
        IReadOnlyList<string>? ingredientNames = null)
    {
        var resolvedMealName = mealName ?? template.Name.Trim().ToLowerInvariant();
        var resolvedIngredientNames = ingredientNames ??
            template.Ingredients
                .Select(ingredient => ClampAndNormalize(ingredient.Name, MaxAiIngredientNameLength))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        var isBreakfastMeal = ResolveSuitableMealTypes(template)
            .Contains("Breakfast", StringComparer.OrdinalIgnoreCase) ||
            BreakfastNameKeywords.Any(keyword =>
                resolvedMealName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (!isBreakfastMeal)
        {
            return false;
        }

        var isExplicitlyHotBreakfast = resolvedMealName.Contains("scramble", StringComparison.OrdinalIgnoreCase) ||
                                       resolvedMealName.Contains("scrambled", StringComparison.OrdinalIgnoreCase) ||
                                       resolvedMealName.Contains("omelette", StringComparison.OrdinalIgnoreCase) ||
                                       resolvedMealName.Contains("omelet", StringComparison.OrdinalIgnoreCase) ||
                                       resolvedMealName.Contains("muffin", StringComparison.OrdinalIgnoreCase) ||
                                       resolvedMealName.Contains("toast", StringComparison.OrdinalIgnoreCase) ||
                                       resolvedMealName.Contains("pancake", StringComparison.OrdinalIgnoreCase) ||
                                       resolvedMealName.Contains("porridge", StringComparison.OrdinalIgnoreCase);
        if (isExplicitlyHotBreakfast)
        {
            return false;
        }

        return resolvedIngredientNames.Any(name =>
            name.Contains("yogurt", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("yoghurt", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("oat", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("granola", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("muesli", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("chia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("berry", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("berries", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("honey", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> BuildColdBreakfastRecipeSteps(IReadOnlyList<string> ingredientNames)
    {
        var baseIngredient = ingredientNames.ElementAtOrDefault(0) ?? "your base";
        var mixInIngredient = ingredientNames.ElementAtOrDefault(1) ?? baseIngredient;
        var toppingIngredient = ingredientNames.ElementAtOrDefault(2) ?? mixInIngredient;
        var finishingIngredient = ingredientNames.ElementAtOrDefault(3) ?? toppingIngredient;
        var usesOatsOrChia = ingredientNames.Any(name =>
            name.Contains("oat", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("chia", StringComparison.OrdinalIgnoreCase));
        var chillStep = usesOatsOrChia
            ? "Cover and chill for at least 10 minutes, or overnight if you want a softer set."
            : "Let everything sit for 5-10 minutes so the flavours settle and any frozen fruit can soften.";

        return
        [
            $"Divide {baseIngredient} between jars or bowls.",
            $"Stir in {mixInIngredient} until evenly combined.",
            $"Top with {toppingIngredient} and spread it out evenly.",
            $"Finish with {finishingIngredient} just before serving if you want the top to stay distinct.",
            chillStep
        ];
    }

    internal static MealNutritionEstimate EstimateMealNutritionPerServing(
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

}
