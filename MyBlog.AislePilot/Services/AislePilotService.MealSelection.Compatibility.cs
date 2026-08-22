using System.Text.RegularExpressions;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private static readonly IReadOnlyDictionary<string, string[]> AllergenIngredientTerms =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["milk"] = ["milk", "butter", "cream", "cheese", "yogurt", "yoghurt", "paneer", "halloumi", "parmesan", "mozzarella"],
            ["egg"] = ["egg", "mayonnaise"],
            ["gluten"] = ["wheat", "flour", "bread", "wrap", "pasta", "couscous", "noodle", "pastry", "barley", "rye", "oat", "soy sauce"],
            ["tree-nut"] = ["almond", "hazelnut", "walnut", "cashew", "pecan", "pistachio", "macadamia", "brazil nut", "marzipan"],
            ["peanut"] = ["peanut", "groundnut", "satay"],
            ["soy"] = ["soy", "soya", "tofu", "edamame", "tempeh"],
            ["sesame"] = ["sesame", "tahini"],
            ["fish"] = ["fish", "salmon", "tuna", "cod", "haddock", "anchovy", "mackerel"],
            ["crustacean"] = ["prawn", "shrimp", "crab", "lobster", "scampi"],
            ["mollusc"] = ["mussel", "oyster", "squid", "clam", "whelk", "snail"],
            ["mustard"] = ["mustard"],
            ["celery"] = ["celery", "celeriac"],
            ["lupin"] = ["lupin"],
            ["sulphite"] = ["sulphite", "sulfite", "sulphur dioxide"]
        };

    private static readonly IReadOnlyDictionary<string, string> AllergenAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["milk"] = "milk", ["dairy"] = "milk", ["lactose"] = "milk",
            ["egg"] = "egg", ["eggs"] = "egg",
            ["gluten"] = "gluten", ["wheat"] = "gluten", ["coeliac"] = "gluten", ["celiac"] = "gluten",
            ["nut"] = "tree-nut", ["nuts"] = "tree-nut", ["tree nut"] = "tree-nut", ["tree nuts"] = "tree-nut",
            ["peanut"] = "peanut", ["peanuts"] = "peanut", ["groundnut"] = "peanut",
            ["soy"] = "soy", ["soya"] = "soy", ["soybean"] = "soy", ["soybeans"] = "soy",
            ["sesame"] = "sesame", ["fish"] = "fish",
            ["crustacean"] = "crustacean", ["crustaceans"] = "crustacean", ["shellfish"] = "crustacean",
            ["mollusc"] = "mollusc", ["molluscs"] = "mollusc",
            ["mustard"] = "mustard", ["celery"] = "celery", ["lupin"] = "lupin",
            ["sulphite"] = "sulphite", ["sulphites"] = "sulphite", ["sulfite"] = "sulphite", ["sulfites"] = "sulphite"
        };

    private static readonly string[] LandMeatTerms =
        ["chicken", "turkey", "beef", "pork", "lamb", "bacon", "ham", "sausage", "duck", "gelatin", "gelatine"];

    private static bool IsMealCompatibleWithDietaryIngredients(
        MealTemplate meal,
        IReadOnlyList<string> dietaryModes)
    {
        var ingredientNames = meal.Ingredients.Select(ingredient => ingredient.Name).ToList();
        if (dietaryModes.Any(mode => mode.Equals("Vegan", StringComparison.OrdinalIgnoreCase)) &&
            ContainsAnyIngredientTerm(ingredientNames, LandMeatTerms
                .Concat(AllergenIngredientTerms["fish"])
                .Concat(AllergenIngredientTerms["crustacean"])
                .Concat(AllergenIngredientTerms["mollusc"])
                .Concat(AllergenIngredientTerms["milk"])
                .Concat(AllergenIngredientTerms["egg"])
                .Append("honey")))
        {
            return false;
        }

        if (dietaryModes.Any(mode => mode.Equals("Vegetarian", StringComparison.OrdinalIgnoreCase)) &&
            ContainsAnyIngredientTerm(ingredientNames, LandMeatTerms
                .Concat(AllergenIngredientTerms["fish"])
                .Concat(AllergenIngredientTerms["crustacean"])
                .Concat(AllergenIngredientTerms["mollusc"])))
        {
            return false;
        }

        if (dietaryModes.Any(mode => mode.Equals("Pescatarian", StringComparison.OrdinalIgnoreCase)) &&
            ContainsAnyIngredientTerm(ingredientNames, LandMeatTerms))
        {
            return false;
        }

        return !dietaryModes.Any(mode => mode.Equals("Gluten-Free", StringComparison.OrdinalIgnoreCase)) ||
               !ContainsAnyIngredientTerm(ingredientNames, AllergenIngredientTerms["gluten"]);
    }

    private static bool ContainsKnownAllergen(MealTemplate meal, string notes)
    {
        var selectedGroups = notes
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(note => note.Trim().ToLowerInvariant())
            .Where(AllergenAliases.ContainsKey)
            .Select(note => AllergenAliases[note])
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var ingredientNames = meal.Ingredients.Select(ingredient => ingredient.Name).ToList();
        return selectedGroups.Any(group =>
            AllergenIngredientTerms.TryGetValue(group, out var terms) &&
            ContainsAnyIngredientTerm(ingredientNames, terms));
    }

    private static bool ContainsAnyIngredientTerm(IEnumerable<string> ingredientNames, IEnumerable<string> terms)
    {
        var materializedTerms = terms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return ingredientNames.Any(name => materializedTerms.Any(term => IngredientContainsTerm(name, term)));
    }

    private static bool IngredientContainsTerm(string ingredientName, string term)
    {
        var normalizedName = ingredientName.Trim().ToLowerInvariant();
        if ((normalizedName.Contains("gluten-free", StringComparison.Ordinal) ||
             normalizedName.Contains("gluten free", StringComparison.Ordinal)) &&
            AllergenIngredientTerms["gluten"].Contains(term, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (term.Equals("milk", StringComparison.OrdinalIgnoreCase) &&
            (normalizedName.Contains("coconut milk", StringComparison.Ordinal) ||
             normalizedName.Contains("almond milk", StringComparison.Ordinal) ||
             normalizedName.Contains("oat milk", StringComparison.Ordinal) ||
             normalizedName.Contains("soy milk", StringComparison.Ordinal)))
        {
            return false;
        }

        if (term.Equals("cream", StringComparison.OrdinalIgnoreCase) &&
            normalizedName.Contains("coconut cream", StringComparison.Ordinal))
        {
            return false;
        }

        if (term.Equals("butter", StringComparison.OrdinalIgnoreCase) &&
            (normalizedName.Contains("peanut butter", StringComparison.Ordinal) ||
             normalizedName.Contains("nut butter", StringComparison.Ordinal) ||
             normalizedName.Contains("butter beans", StringComparison.Ordinal)))
        {
            return false;
        }

        return ContainsWholeWord(ingredientName, term) || ContainsWholeWord(ingredientName, $"{term}s");
    }

    private static bool IsDessertCompatible(
        DessertAddOnTemplate dessert,
        IReadOnlyList<string> dietaryModes,
        string dislikesOrAllergens)
    {
        var meal = new MealTemplate(dessert.Name, 1m, false, ["Balanced"], dessert.Ingredients);
        return IsMealCompatibleWithDietaryIngredients(meal, dietaryModes) &&
               !ContainsKnownAllergen(meal, dislikesOrAllergens) &&
               !dislikesOrAllergens
                   .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Where(token => token.Length >= 3)
                   .Any(token => ContainsToken(meal, token));
    }
}
