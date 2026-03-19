using MyBlog.Models;
using MyBlog.Utilities;
using System.Globalization;
using System.Text;

namespace MyBlog.Services;

public sealed class AislePilotService : IAislePilotService
{
    private static readonly HashSet<string> GenericPantryTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "rice",
        "pasta",
        "noodles",
        "potatoes",
        "sweet potatoes",
        "olive oil",
        "oil",
        "soy sauce",
        "sauce",
        "passata",
        "chopped tomatoes",
        "tomatoes",
        "onion",
        "onions",
        "garlic",
        "spices",
        "seasoning",
        "paprika",
        "salt",
        "pepper",
        "frozen peas",
        "frozen mixed veg",
        "frozen veg"
    };

    private static readonly Dictionary<string, string[]> IngredientAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Frozen mixed veg"] =
        [
            "mixed veg",
            "mixed vegetables",
            "vegetables",
            "veg",
            "peas",
            "frozen peas"
        ],
        ["Soy sauce"] =
        [
            "sauce",
            "sauces",
            "stir fry sauce"
        ]
    };

    private static readonly HashSet<string> GenericPantryTokensNormalized = new(
        GenericPantryTokens.Select(NormalizePantryText),
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SupportedSupermarkets =
    [
        "Tesco",
        "Sainsbury's",
        "Aldi",
        "Lidl",
        "Asda",
        "Custom"
    ];

    private static readonly string[] SupportedDietaryModes =
    [
        "Balanced",
        "High-Protein",
        "Vegetarian",
        "Vegan",
        "Pescatarian",
        "Gluten-Free"
    ];

    private static readonly string[] DefaultAisleOrder =
    [
        "Produce",
        "Bakery",
        "Meat & Fish",
        "Dairy & Eggs",
        "Frozen",
        "Tins & Dry Goods",
        "Spices & Sauces",
        "Snacks",
        "Drinks",
        "Household",
        "Other"
    ];

    private static readonly Dictionary<string, string[]> SupermarketAisleOrders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tesco"] =
        [
            "Produce", "Bakery", "Meat & Fish", "Dairy & Eggs", "Frozen", "Tins & Dry Goods", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ],
        ["Sainsbury's"] =
        [
            "Produce", "Bakery", "Dairy & Eggs", "Meat & Fish", "Frozen", "Tins & Dry Goods", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ],
        ["Aldi"] =
        [
            "Produce", "Bakery", "Meat & Fish", "Dairy & Eggs", "Tins & Dry Goods", "Frozen", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ],
        ["Lidl"] =
        [
            "Produce", "Bakery", "Dairy & Eggs", "Meat & Fish", "Tins & Dry Goods", "Frozen", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ],
        ["Asda"] =
        [
            "Produce", "Bakery", "Meat & Fish", "Dairy & Eggs", "Frozen", "Tins & Dry Goods", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ]
    };

    private static readonly IReadOnlyList<MealTemplate> MealTemplates =
    [
        new(
            "Chicken stir fry with rice",
            6.40m,
            IsQuick: true,
            ["Balanced", "High-Protein"],
            [
                new IngredientTemplate("Chicken breast", "Meat & Fish", 0.45m, "kg", 3.25m),
                new IngredientTemplate("Bell peppers", "Produce", 3m, "pcs", 1.50m),
                new IngredientTemplate("Rice", "Tins & Dry Goods", 0.40m, "kg", 0.85m),
                new IngredientTemplate("Soy sauce", "Spices & Sauces", 0.12m, "bottle", 0.45m)
            ]),
        new(
            "Salmon, potatoes, and broccoli",
            8.90m,
            IsQuick: true,
            ["Balanced", "High-Protein", "Pescatarian", "Gluten-Free"],
            [
                new IngredientTemplate("Salmon fillets", "Meat & Fish", 0.36m, "kg", 4.40m),
                new IngredientTemplate("Potatoes", "Produce", 0.90m, "kg", 1.10m),
                new IngredientTemplate("Broccoli", "Produce", 2m, "pcs", 1.60m),
                new IngredientTemplate("Olive oil", "Spices & Sauces", 0.08m, "bottle", 0.55m)
            ]),
        new(
            "Turkey chilli with beans",
            7.20m,
            IsQuick: false,
            ["Balanced", "High-Protein", "Gluten-Free"],
            [
                new IngredientTemplate("Turkey mince", "Meat & Fish", 0.50m, "kg", 3.20m),
                new IngredientTemplate("Kidney beans", "Tins & Dry Goods", 2m, "tins", 1.20m),
                new IngredientTemplate("Chopped tomatoes", "Tins & Dry Goods", 2m, "tins", 1.00m),
                new IngredientTemplate("Chilli seasoning", "Spices & Sauces", 1m, "pack", 0.55m)
            ]),
        new(
            "Veggie lentil curry",
            5.10m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "Vegan", "Gluten-Free"],
            [
                new IngredientTemplate("Red lentils", "Tins & Dry Goods", 0.45m, "kg", 1.10m),
                new IngredientTemplate("Coconut milk", "Tins & Dry Goods", 2m, "tins", 1.60m),
                new IngredientTemplate("Spinach", "Produce", 0.30m, "kg", 1.20m),
                new IngredientTemplate("Curry paste", "Spices & Sauces", 1m, "jar", 0.80m)
            ]),
        new(
            "Tofu noodle bowls",
            5.80m,
            IsQuick: true,
            ["Balanced", "Vegetarian", "Vegan"],
            [
                new IngredientTemplate("Firm tofu", "Dairy & Eggs", 0.40m, "kg", 1.60m),
                new IngredientTemplate("Egg noodles", "Tins & Dry Goods", 0.35m, "kg", 1.00m),
                new IngredientTemplate("Carrots", "Produce", 4m, "pcs", 0.80m),
                new IngredientTemplate("Stir fry sauce", "Spices & Sauces", 1m, "jar", 0.75m)
            ]),
        new(
            "Greek yogurt chicken wraps",
            6.00m,
            IsQuick: true,
            ["Balanced", "High-Protein"],
            [
                new IngredientTemplate("Chicken thigh strips", "Meat & Fish", 0.45m, "kg", 2.70m),
                new IngredientTemplate("Wraps", "Bakery", 1m, "pack", 1.00m),
                new IngredientTemplate("Greek yogurt", "Dairy & Eggs", 0.35m, "kg", 1.35m),
                new IngredientTemplate("Lettuce", "Produce", 1m, "head", 0.95m)
            ]),
        new(
            "Paneer tikka tray bake",
            6.50m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "Gluten-Free"],
            [
                new IngredientTemplate("Paneer", "Dairy & Eggs", 0.40m, "kg", 2.40m),
                new IngredientTemplate("Onions", "Produce", 0.60m, "kg", 0.70m),
                new IngredientTemplate("Bell peppers", "Produce", 3m, "pcs", 1.50m),
                new IngredientTemplate("Tikka seasoning", "Spices & Sauces", 1m, "pack", 0.85m)
            ]),
        new(
            "Prawn tomato pasta",
            7.10m,
            IsQuick: true,
            ["Balanced", "Pescatarian"],
            [
                new IngredientTemplate("King prawns", "Meat & Fish", 0.32m, "kg", 3.40m),
                new IngredientTemplate("Pasta", "Tins & Dry Goods", 0.45m, "kg", 0.90m),
                new IngredientTemplate("Passata", "Tins & Dry Goods", 0.70m, "bottle", 0.85m),
                new IngredientTemplate("Parmesan", "Dairy & Eggs", 0.12m, "kg", 1.10m)
            ]),
        new(
            "Beef and veg rice bowls",
            8.20m,
            IsQuick: false,
            ["Balanced", "High-Protein"],
            [
                new IngredientTemplate("Lean beef mince", "Meat & Fish", 0.50m, "kg", 3.80m),
                new IngredientTemplate("Rice", "Tins & Dry Goods", 0.45m, "kg", 0.95m),
                new IngredientTemplate("Frozen peas", "Frozen", 0.40m, "kg", 0.80m),
                new IngredientTemplate("Onions", "Produce", 0.40m, "kg", 0.55m)
            ]),
        new(
            "Chickpea quinoa salad bowls",
            5.40m,
            IsQuick: true,
            ["Balanced", "Vegetarian", "Vegan", "Gluten-Free"],
            [
                new IngredientTemplate("Chickpeas", "Tins & Dry Goods", 2m, "tins", 1.10m),
                new IngredientTemplate("Quinoa", "Tins & Dry Goods", 0.35m, "kg", 1.70m),
                new IngredientTemplate("Cucumber", "Produce", 1m, "pcs", 0.60m),
                new IngredientTemplate("Cherry tomatoes", "Produce", 0.30m, "kg", 1.00m)
            ]),
        new(
            "Egg fried rice",
            4.80m,
            IsQuick: true,
            ["Balanced", "Vegetarian"],
            [
                new IngredientTemplate("Eggs", "Dairy & Eggs", 6m, "pcs", 1.30m),
                new IngredientTemplate("Rice", "Tins & Dry Goods", 0.45m, "kg", 0.95m),
                new IngredientTemplate("Frozen mixed veg", "Frozen", 0.50m, "kg", 1.05m),
                new IngredientTemplate("Soy sauce", "Spices & Sauces", 0.10m, "bottle", 0.40m)
            ]),
        new(
            "Baked cod with sweet potato wedges",
            7.90m,
            IsQuick: false,
            ["Balanced", "High-Protein", "Pescatarian", "Gluten-Free"],
            [
                new IngredientTemplate("Cod fillets", "Meat & Fish", 0.36m, "kg", 3.90m),
                new IngredientTemplate("Sweet potatoes", "Produce", 0.95m, "kg", 1.65m),
                new IngredientTemplate("Green beans", "Produce", 0.30m, "kg", 1.25m),
                new IngredientTemplate("Paprika", "Spices & Sauces", 0.06m, "jar", 0.55m)
            ])
    ];

    public IReadOnlyList<string> GetSupportedSupermarkets()
    {
        return SupportedSupermarkets;
    }

    public IReadOnlyList<string> GetSupportedDietaryModes()
    {
        return SupportedDietaryModes;
    }

    public bool HasCompatibleMeals(AislePilotRequestModel request)
    {
        var dietaryModes = NormalizeDietaryModes(request.DietaryModes);
        var dislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty;
        var candidates = FilterMeals(dietaryModes, dislikesOrAllergens);
        return candidates.Count > 0;
    }

    public IReadOnlyList<AislePilotPantrySuggestionViewModel> SuggestMealsFromPantry(
        AislePilotRequestModel request,
        int maxResults = 5)
    {
        var dietaryModes = NormalizeDietaryModes(request.DietaryModes);
        var dislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty;
        var pantryTokens = ParsePantryTokens(request.PantryItems);
        var specificPantryTokens = ParseSpecificPantryTokens(pantryTokens);
        if (pantryTokens.Count == 0)
        {
            return [];
        }

        var candidates = FilterMeals(dietaryModes, dislikesOrAllergens);
        if (candidates.Count == 0)
        {
            return [];
        }

        var cappedResults = Math.Clamp(maxResults, 1, 12);
        return candidates
            .Select(template => BuildPantrySuggestion(template, pantryTokens))
            .Where(suggestion => suggestion.MatchPercent > 0)
            .Where(suggestion => SuggestionMatchesSpecificTokens(suggestion, specificPantryTokens))
            .Where(suggestion => suggestion.MatchPercent == 100)
            .OrderByDescending(suggestion => suggestion.MatchPercent)
            .ThenBy(suggestion => suggestion.MissingIngredients.Count)
            .ThenBy(suggestion => suggestion.MealName, StringComparer.OrdinalIgnoreCase)
            .Take(cappedResults)
            .ToList();
    }

    public AislePilotPlanResultViewModel BuildPlan(AislePilotRequestModel request)
    {
        var context = BuildPlanContext(request);
        var cookDays = NormalizeCookDays(request.CookDays);
        var selectedMeals = SelectMeals(
            context.DietaryModes,
            request.WeeklyBudget,
            context.HouseholdFactor,
            request.PreferQuickMeals,
            context.DislikesOrAllergens,
            cookDays);
        return BuildPlanFromMeals(request, context, selectedMeals, cookDays);
    }

    public AislePilotPlanResultViewModel SwapMealForDay(
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName)
    {
        var cookDays = NormalizeCookDays(request.CookDays);
        if (dayIndex < 0 || dayIndex >= cookDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayIndex),
                $"Day index must be between 0 and {cookDays - 1}.");
        }

        var context = BuildPlanContext(request);
        var selectedMeals = SelectMeals(
            context.DietaryModes,
            request.WeeklyBudget,
            context.HouseholdFactor,
            request.PreferQuickMeals,
            context.DislikesOrAllergens,
            cookDays).ToList();

        var currentName = string.IsNullOrWhiteSpace(currentMealName)
            ? selectedMeals[dayIndex].Name
            : currentMealName.Trim();
        var leftoverDays = Math.Max(0, 7 - cookDays);
        var requestedLeftoverSourceDays = ParseRequestedLeftoverSourceDays(
            request.LeftoverCookDayIndexesCsv,
            cookDays,
            leftoverDays);
        var mealPortionMultipliers = BuildMealPortionMultipliers(
            cookDays,
            leftoverDays,
            requestedLeftoverSourceDays);
        var dayMultiplier = mealPortionMultipliers[dayIndex];

        var candidates = FilterMeals(context.DietaryModes, context.DislikesOrAllergens);
        var replacement = SelectSwapCandidate(
            candidates,
            selectedMeals,
            dayIndex,
            currentName,
            request.WeeklyBudget,
            context.HouseholdFactor,
            request.PreferQuickMeals,
            dayMultiplier);

        selectedMeals[dayIndex] = replacement;
        return BuildPlanFromMeals(request, context, selectedMeals, cookDays);
    }

    private static PlanContext BuildPlanContext(AislePilotRequestModel request)
    {
        var supermarket = NormalizeSupermarket(request.Supermarket);
        var dietaryModes = NormalizeDietaryModes(request.DietaryModes);
        var customAisleOrder = request.CustomAisleOrder ?? string.Empty;
        var dislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty;
        var aisleOrder = ResolveAisleOrder(supermarket, customAisleOrder);
        var householdFactor = Math.Max(0.5m, request.HouseholdSize / 2m);

        return new PlanContext(
            supermarket,
            dietaryModes,
            aisleOrder,
            householdFactor,
            dislikesOrAllergens);
    }

    private static AislePilotPlanResultViewModel BuildPlanFromMeals(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> selectedMeals,
        int cookDays)
    {
        var leftoverDays = Math.Max(0, 7 - cookDays);
        var requestedLeftoverSourceDays = ParseRequestedLeftoverSourceDays(
            request.LeftoverCookDayIndexesCsv,
            cookDays,
            leftoverDays);
        var mealPortionMultipliers = BuildMealPortionMultipliers(
            cookDays,
            leftoverDays,
            requestedLeftoverSourceDays);
        var dailyPlans = BuildDailyPlans(
            selectedMeals,
            mealPortionMultipliers,
            context.HouseholdFactor,
            context.DietaryModes,
            context.DislikesOrAllergens);
        var shoppingItems = BuildShoppingList(
            selectedMeals,
            mealPortionMultipliers,
            context.HouseholdFactor,
            context.AisleOrder);
        var estimatedTotalCost = decimal.Round(dailyPlans.Sum(x => x.EstimatedCost), 2, MidpointRounding.AwayFromZero);
        var budgetDelta = decimal.Round(request.WeeklyBudget - estimatedTotalCost, 2, MidpointRounding.AwayFromZero);
        var isOverBudget = budgetDelta < 0;

        return new AislePilotPlanResultViewModel
        {
            Supermarket = context.Supermarket,
            AppliedDietaryModes = context.DietaryModes,
            CookDays = cookDays,
            LeftoverDays = leftoverDays,
            WeeklyBudget = request.WeeklyBudget,
            EstimatedTotalCost = estimatedTotalCost,
            BudgetDelta = budgetDelta,
            IsOverBudget = isOverBudget,
            AisleOrderUsed = context.AisleOrder,
            BudgetTips = BuildBudgetTips(isOverBudget, budgetDelta, leftoverDays),
            MealPlan = dailyPlans,
            ShoppingItems = shoppingItems
        };
    }

    private static MealTemplate SelectSwapCandidate(
        IReadOnlyList<MealTemplate> allCandidates,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        string currentMealName,
        decimal weeklyBudget,
        decimal householdFactor,
        bool preferQuickMeals,
        int dayMultiplier)
    {
        if (allCandidates.Count == 0)
        {
            throw new InvalidOperationException("No meals are available for swap.");
        }

        var usedNames = selectedMeals
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var preferredPool = allCandidates
            .Where(meal =>
                !meal.Name.Equals(currentMealName, StringComparison.OrdinalIgnoreCase) &&
                !usedNames.Contains(meal.Name))
            .ToList();

        var fallbackPool = allCandidates
            .Where(meal => !meal.Name.Equals(currentMealName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var candidatePool = preferredPool.Count > 0
            ? preferredPool
            : fallbackPool.Count > 0
                ? fallbackPool
                : allCandidates.ToList();

        var normalizedDayMultiplier = Math.Max(1, dayMultiplier);
        var targetMealCost = (weeklyBudget / 7m) * normalizedDayMultiplier;
        var previousName = dayIndex > 0 ? selectedMeals[dayIndex - 1].Name : null;
        var nextName = dayIndex < selectedMeals.Count - 1 ? selectedMeals[dayIndex + 1].Name : null;

        return candidatePool
            .Select(template => new
            {
                template,
                score = BuildMealSelectionScore(
                    template,
                    targetMealCost,
                    householdFactor,
                    preferQuickMeals,
                    normalizedDayMultiplier,
                    previousName,
                    nextName)
            })
            .OrderBy(item => item.score)
            .ThenBy(item => item.template.Name, StringComparer.OrdinalIgnoreCase)
            .First()
            .template;
    }

    private static decimal BuildMealSelectionScore(
        MealTemplate template,
        decimal targetMealCost,
        decimal householdFactor,
        bool preferQuickMeals,
        int dayMultiplier = 1,
        string? previousName = null,
        string? nextName = null)
    {
        var normalizedDayMultiplier = Math.Max(1, dayMultiplier);
        var scaledCost = template.BaseCostForTwo * householdFactor * normalizedDayMultiplier;
        var budgetDistance = Math.Abs(scaledCost - targetMealCost);
        var quickPenalty = preferQuickMeals && !template.IsQuick ? 0.8m : 0m;
        var adjacencyPenalty =
            (previousName is not null && template.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase)) ||
            (nextName is not null && template.Name.Equals(nextName, StringComparison.OrdinalIgnoreCase))
                ? 1.2m
                : 0m;

        return budgetDistance + quickPenalty + adjacencyPenalty;
    }

    private static IReadOnlyList<AislePilotMealDayViewModel> BuildDailyPlans(
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<int> mealPortionMultipliers,
        decimal householdFactor,
        IReadOnlyList<string> dietaryModes,
        string dislikesOrAllergens)
    {
        var normalizedCookDays = Math.Min(selectedMeals.Count, mealPortionMultipliers.Count);
        var cookDayNames = BuildCookDayNames(mealPortionMultipliers).Take(normalizedCookDays).ToArray();
        var plans = new List<AislePilotMealDayViewModel>(cookDayNames.Length);
        for (var i = 0; i < cookDayNames.Length; i++)
        {
            var template = selectedMeals[i];
            var mealPortionMultiplier = Math.Max(1, mealPortionMultipliers[i]);
            var estimatedCost = decimal.Round(
                template.BaseCostForTwo * householdFactor * mealPortionMultiplier,
                2,
                MidpointRounding.AwayFromZero);
            var reason = template.IsQuick
                ? "Quick prep for busy days."
                : "Batch-friendly and good for leftovers.";

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

            plans.Add(new AislePilotMealDayViewModel
            {
                Day = cookDayNames[i],
                MealName = template.Name,
                MealReason = reason,
                LeftoverDaysCovered = leftoverDaysCovered,
                EstimatedCost = estimatedCost,
                EstimatedPrepMinutes = estimatedPrepMinutes,
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
                "Heat a deep pan with a little oil and soften chopped onion and garlic for 4-5 minutes.",
                "Add turkey mince and cook until no pink remains, breaking it up with a spoon.",
                "Stir in chilli seasoning, then add chopped tomatoes, beans, and a small splash of water.",
                "Simmer uncovered for 20-25 minutes, stirring occasionally until thickened.",
                "Taste and adjust salt, pepper, or spice level before serving."
            ],
            "veggie lentil curry" =>
            [
                "Rinse the lentils until the water runs mostly clear.",
                "Cook onion, garlic, and curry paste in a pan for 3-4 minutes until fragrant.",
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
                "Cook aromatics (onion/garlic/spices) until fragrant.",
                "Add protein or pulses and stir so everything is coated in spices.",
                "Pour in liquids and simmer gently until the base thickens.",
                "Adjust with water as needed and cook until ingredients are tender.",
                "Taste, season, and serve with your preferred side."
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
                "Preheat oven and line a tray.",
                "Season vegetables first and give them a short head start in the oven.",
                "Add protein or main component and continue roasting until cooked through.",
                "Turn halfway for even colour and texture.",
                "Rest briefly, then serve with pan juices."
            ],
            _ =>
            [
                "Prep and portion ingredients before you start cooking.",
                "Cook your base carb if using one, then keep warm.",
                "Cook protein and key vegetables until done.",
                "Combine everything with sauce or seasoning and heat through.",
                "Taste, adjust seasoning, and serve warm."
            ]
        };
    }

    private static IReadOnlyList<AislePilotShoppingItemViewModel> BuildShoppingList(
        IReadOnlyList<MealTemplate> selectedMeals,
        IReadOnlyList<int> mealPortionMultipliers,
        decimal householdFactor,
        IReadOnlyList<string> aisleOrder)
    {
        var aggregated = new Dictionary<string, MutableShoppingItem>(StringComparer.OrdinalIgnoreCase);
        var mealCount = Math.Min(selectedMeals.Count, mealPortionMultipliers.Count);
        for (var i = 0; i < mealCount; i++)
        {
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

    private static IReadOnlyList<MealTemplate> SelectMeals(
        IReadOnlyList<string> dietaryModes,
        decimal weeklyBudget,
        decimal householdFactor,
        bool preferQuickMeals,
        string dislikesOrAllergens,
        int cookDays)
    {
        var candidates = FilterMeals(dietaryModes, dislikesOrAllergens);
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "No meals match the selected dietary modes and dislikes/allergens.");
        }

        var targetMealCost = weeklyBudget / 7m;
        var scoredCandidates = candidates
            .Select(template =>
            {
                var scaledCost = template.BaseCostForTwo * householdFactor;
                var budgetDistance = Math.Abs(scaledCost - targetMealCost);
                var quickPenalty = preferQuickMeals && !template.IsQuick ? 0.8m : 0m;
                return new { template, score = budgetDistance + quickPenalty };
            })
            .OrderBy(item => item.score)
            .ThenBy(item => item.template.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.template)
            .ToList();

        var normalizedCookDays = NormalizeCookDays(cookDays);
        var selected = new List<MealTemplate>(normalizedCookDays);
        var startIndex = Math.Abs(DateOnly.FromDateTime(DateTime.UtcNow).DayNumber) % scoredCandidates.Count;
        for (var i = 0; i < normalizedCookDays; i++)
        {
            var candidate = scoredCandidates[(startIndex + i) % scoredCandidates.Count];
            if (selected.Count > 0 && selected[^1].Name == candidate.Name && scoredCandidates.Count > 1)
            {
                candidate = scoredCandidates[(startIndex + i + 1) % scoredCandidates.Count];
            }

            selected.Add(candidate);
        }

        return selected;
    }

    private static List<MealTemplate> FilterMeals(IReadOnlyList<string> dietaryModes, string dislikesOrAllergens)
    {
        var disallowedTokens = dislikesOrAllergens
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length >= 3)
            .ToList();

        var strictModes = dietaryModes
            .Where(x => !x.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var baseFiltered = MealTemplates
            .Where(meal => disallowedTokens.All(token => !ContainsToken(meal, token)))
            .ToList();

        // Treat explicitly selected dietary modes as hard constraints.
        if (strictModes.Count == 0)
        {
            return baseFiltered;
        }

        var strictFiltered = baseFiltered
            .Where(meal => strictModes.All(mode => meal.Tags.Contains(mode, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        return strictFiltered;
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
        IReadOnlyList<string> pantryTokens)
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

        return new AislePilotPantrySuggestionViewModel
        {
            MealName = template.Name,
            MatchPercent = matchPercent,
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
        return (pantryItems ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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

        if (normalizedToken.Length >= 3 &&
            normalizedIngredient.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokenWords = normalizedToken.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokenWords.Any(word =>
            word.Length >= 3 &&
            normalizedIngredient.Contains(word, StringComparison.OrdinalIgnoreCase));
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

    private static int NormalizeCookDays(int cookDays)
    {
        return Math.Clamp(cookDays, 1, 7);
    }

    private static int RoundToNearestFiveMinutes(int minutes)
    {
        var safeMinutes = Math.Max(5, minutes);
        return (int)(Math.Round(safeMinutes / 5m, MidpointRounding.AwayFromZero) * 5m);
    }

    private static IReadOnlyList<int> BuildMealPortionMultipliers(
        int cookDays,
        int leftoverDays,
        IReadOnlyList<int>? requestedLeftoverSourceDays = null)
    {
        var normalizedCookDays = NormalizeCookDays(cookDays);
        var normalizedLeftoverDays = Math.Clamp(leftoverDays, 0, 7 - normalizedCookDays);
        var defaultMultipliers = Enumerable.Repeat(1, normalizedCookDays).ToArray();
        for (var i = 0; i < normalizedLeftoverDays; i++)
        {
            defaultMultipliers[i % normalizedCookDays]++;
        }

        if (normalizedLeftoverDays == 0)
        {
            return defaultMultipliers;
        }

        var requestedWeekDays = (requestedLeftoverSourceDays ?? [])
            .Where(dayIndex => dayIndex >= 0 && dayIndex < 7)
            .Take(normalizedLeftoverDays)
            .ToList();

        if (requestedWeekDays.Count == 0)
        {
            return defaultMultipliers;
        }

        var candidates = BuildMealPortionMultiplierCandidates(normalizedCookDays).ToList();
        if (candidates.Count == 0)
        {
            return defaultMultipliers;
        }

        return candidates
            .Select(candidate =>
            {
                var sourceWeekDays = BuildLeftoverSourceWeekDays(candidate);
                return new
                {
                    Candidate = candidate,
                    SourceWeekDays = sourceWeekDays,
                    Overlap = CalculateLeftoverSourceOverlap(requestedWeekDays, sourceWeekDays)
                };
            })
            .OrderByDescending(item => item.Overlap)
            .ThenBy(item => CalculateLeftoverSourceDistance(requestedWeekDays, item.SourceWeekDays))
            .ThenBy(item => string.Join(",", item.Candidate))
            .First()
            .Candidate;
    }

    private static IReadOnlyList<int> ParseRequestedLeftoverSourceDays(
        string? leftoverCookDayIndexesCsv,
        int cookDays,
        int leftoverDays)
    {
        var normalizedCookDays = NormalizeCookDays(cookDays);
        var normalizedLeftoverDays = Math.Clamp(leftoverDays, 0, 7 - normalizedCookDays);
        if (normalizedLeftoverDays == 0 || string.IsNullOrWhiteSpace(leftoverCookDayIndexesCsv))
        {
            return [];
        }

        var requestedDays = new List<int>(normalizedLeftoverDays);
        var tokens = leftoverCookDayIndexesCsv
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (requestedDays.Count >= normalizedLeftoverDays)
            {
                break;
            }

            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dayIndex))
            {
                continue;
            }

            if (dayIndex < 0 || dayIndex >= 7)
            {
                continue;
            }

            requestedDays.Add(dayIndex);
        }

        return requestedDays;
    }

    private static IEnumerable<int[]> BuildMealPortionMultiplierCandidates(int cookDays)
    {
        var normalizedCookDays = NormalizeCookDays(cookDays);
        var candidate = new int[normalizedCookDays];
        foreach (var composition in BuildMultiplierCompositions(0, normalizedCookDays, 7, candidate))
        {
            yield return composition;
        }
    }

    private static IEnumerable<int[]> BuildMultiplierCompositions(
        int position,
        int totalSlots,
        int remainingTotal,
        int[] candidate)
    {
        if (position == totalSlots - 1)
        {
            if (remainingTotal >= 1)
            {
                candidate[position] = remainingTotal;
                yield return [.. candidate];
            }

            yield break;
        }

        var maxValue = remainingTotal - (totalSlots - position - 1);
        for (var value = 1; value <= maxValue; value++)
        {
            candidate[position] = value;
            foreach (var composition in BuildMultiplierCompositions(position + 1, totalSlots, remainingTotal - value, candidate))
            {
                yield return composition;
            }
        }
    }

    private static IReadOnlyList<int> BuildLeftoverSourceWeekDays(IReadOnlyList<int> multipliers)
    {
        var sourceDays = new List<int>();
        var dayCursor = 0;
        foreach (var multiplier in multipliers)
        {
            var extras = Math.Max(0, multiplier - 1);
            for (var i = 0; i < extras; i++)
            {
                sourceDays.Add(Math.Clamp(dayCursor, 0, 6));
            }

            dayCursor += Math.Max(1, multiplier);
        }

        return sourceDays;
    }

    private static int CalculateLeftoverSourceOverlap(
        IReadOnlyList<int> requestedSourceDays,
        IReadOnlyList<int> candidateSourceDays)
    {
        var requestedCounts = BuildWeekDayCountMap(requestedSourceDays);
        var candidateCounts = BuildWeekDayCountMap(candidateSourceDays);
        var overlap = 0;
        for (var day = 0; day < 7; day++)
        {
            overlap += Math.Min(requestedCounts[day], candidateCounts[day]);
        }

        return overlap;
    }

    private static int CalculateLeftoverSourceDistance(
        IReadOnlyList<int> requestedSourceDays,
        IReadOnlyList<int> candidateSourceDays)
    {
        if (requestedSourceDays.Count == 0 || candidateSourceDays.Count == 0)
        {
            return int.MaxValue / 4;
        }

        var requestedSorted = requestedSourceDays.OrderBy(x => x).ToArray();
        var candidateSorted = candidateSourceDays.OrderBy(x => x).ToArray();
        var comparableLength = Math.Min(requestedSorted.Length, candidateSorted.Length);
        var distance = 0;
        for (var i = 0; i < comparableLength; i++)
        {
            distance += Math.Abs(requestedSorted[i] - candidateSorted[i]);
        }

        if (requestedSorted.Length != candidateSorted.Length)
        {
            distance += Math.Abs(requestedSorted.Length - candidateSorted.Length) * 7;
        }

        return distance;
    }

    private static int[] BuildWeekDayCountMap(IReadOnlyList<int> sourceDays)
    {
        var counts = new int[7];
        foreach (var day in sourceDays)
        {
            if (day >= 0 && day < 7)
            {
                counts[day]++;
            }
        }

        return counts;
    }

    private static string NormalizeSupermarket(string value)
    {
        var selected = SupportedSupermarkets.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        return selected ?? SupportedSupermarkets[0];
    }

    private static IReadOnlyList<string> NormalizeDietaryModes(IReadOnlyList<string>? incomingModes)
    {
        var normalized = incomingModes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => SupportedDietaryModes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];

        if (normalized.Count == 0)
        {
            normalized.Add("Balanced");
        }

        return normalized;
    }

    private static IReadOnlyList<string> ResolveAisleOrder(string supermarket, string customAisleOrder)
    {
        if (supermarket.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            var custom = customAisleOrder
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (custom.Count >= 3)
            {
                if (!custom.Contains("Other", StringComparer.OrdinalIgnoreCase))
                {
                    custom.Add("Other");
                }

                return custom;
            }
        }

        if (SupermarketAisleOrders.TryGetValue(supermarket, out var predefined))
        {
            return predefined;
        }

        return DefaultAisleOrder;
    }

    private sealed record PlanContext(
        string Supermarket,
        IReadOnlyList<string> DietaryModes,
        IReadOnlyList<string> AisleOrder,
        decimal HouseholdFactor,
        string DislikesOrAllergens);

    private sealed class MutableShoppingItem
    {
        public string Department { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal EstimatedCost { get; set; }
    }

    private sealed record MealTemplate(
        string Name,
        decimal BaseCostForTwo,
        bool IsQuick,
        IReadOnlyList<string> Tags,
        IReadOnlyList<IngredientTemplate> Ingredients);

    private sealed record IngredientTemplate(
        string Name,
        string Department,
        decimal QuantityForTwo,
        string Unit,
        decimal EstimatedCostForTwo);
}
