namespace MyBlog.Utilities;

public static class QuantityDisplayFormatter
{
    private static readonly HashSet<string> FractionalContainerUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "bottle",
        "bottles",
        "jar",
        "jars"
    };
    private static readonly Dictionary<string, decimal> RecipeContainerUnitMillilitres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pot"] = 300m,
        ["pots"] = 300m,
        ["bottle"] = 500m,
        ["bottles"] = 500m,
        ["jar"] = 190m,
        ["jars"] = 190m
    };
    private static readonly HashSet<string> RecipeLitreUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "l",
        "lt",
        "ltr",
        "litre",
        "litres",
        "liter",
        "liters"
    };
    private static readonly HashSet<string> RecipeMillilitreUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "ml",
        "millilitre",
        "millilitres",
        "milliliter",
        "milliliters"
    };
    private static readonly HashSet<string> TeaspoonUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "tsp",
        "tsps",
        "teaspoon",
        "teaspoons"
    };
    private static readonly HashSet<string> TablespoonUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "tbsp",
        "tbsps",
        "tablespoon",
        "tablespoons"
    };

    private static readonly HashSet<string> WholePurchaseUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "pack",
        "packs",
        "tin",
        "tins",
        "head",
        "heads",
        "pc",
        "pcs",
        "piece",
        "pieces"
    };

    public static string Format(decimal quantity, string? unit)
    {
        return FormatForShoppingList(quantity, unit);
    }

    public static string FormatForShoppingList(decimal quantity, string? unit)
    {
        if (quantity <= 0)
        {
            return "-";
        }

        var normalizedUnit = NormalizeUnit(unit);

        if (string.Equals(normalizedUnit, "kg", StringComparison.OrdinalIgnoreCase))
        {
            return FormatKg(quantity);
        }

        if (TryFormatSpoonQuantity(quantity, normalizedUnit, out var spoonQuantityDisplay))
        {
            return spoonQuantityDisplay;
        }

        if (FractionalContainerUnits.Contains(normalizedUnit))
        {
            return FormatShoppingListFractionalContainer(quantity, normalizedUnit);
        }

        if (WholePurchaseUnits.Contains(normalizedUnit))
        {
            var unitsToBuy = (int)Math.Ceiling(quantity);
            return $"{unitsToBuy} {ToDisplayUnit(normalizedUnit, unitsToBuy)}";
        }

        if (RecipeLitreUnits.Contains(normalizedUnit))
        {
            return FormatShoppingLiquidVolume(quantity * 1000m);
        }

        if (RecipeMillilitreUnits.Contains(normalizedUnit))
        {
            return FormatShoppingLiquidVolume(quantity);
        }

        var roundedQuantity = decimal.Round(quantity, 2, MidpointRounding.AwayFromZero);
        return string.IsNullOrWhiteSpace(normalizedUnit)
            ? $"{roundedQuantity:0.##}"
            : $"{roundedQuantity:0.##} {normalizedUnit}";
    }

    public static string FormatForRecipe(decimal quantity, string? unit)
    {
        if (quantity <= 0)
        {
            return "-";
        }

        var normalizedUnit = NormalizeUnit(unit);

        if (string.Equals(normalizedUnit, "kg", StringComparison.OrdinalIgnoreCase))
        {
            return FormatKg(quantity);
        }

        if (TryFormatSpoonQuantity(quantity, normalizedUnit, out var spoonQuantityDisplay))
        {
            return spoonQuantityDisplay;
        }

        if (RecipeLitreUnits.Contains(normalizedUnit))
        {
            var totalMillilitres = quantity * 1000m;
            return FormatRecipeLiquidVolume(totalMillilitres);
        }

        if (RecipeMillilitreUnits.Contains(normalizedUnit))
        {
            return FormatRecipeLiquidVolume(quantity);
        }

        if (RecipeContainerUnitMillilitres.TryGetValue(normalizedUnit, out var millilitresPerUnit))
        {
            var totalMillilitres = decimal.Round(
                quantity * millilitresPerUnit,
                0,
                MidpointRounding.AwayFromZero);
            var roundedUpMillilitres = RoundUpToNearest(totalMillilitres, 5m);
            return $"{roundedUpMillilitres:0} ml";
        }

        if (WholePurchaseUnits.Contains(normalizedUnit))
        {
            return FormatRecipeCountQuantity(quantity, normalizedUnit);
        }

        if (string.IsNullOrWhiteSpace(normalizedUnit) && quantity < 1m)
        {
            return FormatFractionalAmount(RoundToNearestQuarter(quantity), null, null);
        }

        var roundedQuantity = decimal.Round(quantity, 2, MidpointRounding.AwayFromZero);
        return string.IsNullOrWhiteSpace(normalizedUnit)
            ? $"{roundedQuantity:0.##}"
            : $"{roundedQuantity:0.##} {normalizedUnit}";
    }

    private static string NormalizeUnit(string? unit)
    {
        return (unit ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string FormatKg(decimal quantity)
    {
        if (quantity > 1m)
        {
            var roundedKilograms = decimal.Round(quantity, 1, MidpointRounding.AwayFromZero);
            return $"{roundedKilograms:0.0} kg";
        }

        var grams = decimal.Round(quantity * 1000m, 0, MidpointRounding.AwayFromZero);
        return $"{grams:0} g";
    }

    private static string ToDisplayUnit(string normalizedUnit, decimal quantity)
    {
        var isSingle = quantity <= 1m;
        return normalizedUnit switch
        {
            "bottle" or "bottles" => isSingle ? "bottle" : "bottles",
            "jar" or "jars" => isSingle ? "jar" : "jars",
            "pack" or "packs" => isSingle ? "pack" : "packs",
            "tin" or "tins" => isSingle ? "tin" : "tins",
            "head" or "heads" => isSingle ? "head" : "heads",
            "pc" or "pcs" or "piece" or "pieces" => isSingle ? "pc" : "pcs",
            _ => normalizedUnit
        };
    }

    private static string ToDisplayUnit(string normalizedUnit, int quantity)
    {
        return ToDisplayUnit(normalizedUnit, (decimal)quantity);
    }

    private static string FormatShoppingListFractionalContainer(decimal quantity, string normalizedUnit)
    {
        var wholeContainers = (int)decimal.Truncate(quantity);
        var fractionalQuantity = quantity - wholeContainers;
        var parts = new List<string>(2);

        if (wholeContainers > 0)
        {
            parts.Add($"{wholeContainers} {ToDisplayUnit(normalizedUnit, wholeContainers)}");
        }

        if (fractionalQuantity > 0m &&
            RecipeContainerUnitMillilitres.TryGetValue(normalizedUnit, out var millilitresPerUnit))
        {
            var teaspoons = RoundToNearestHalf((fractionalQuantity * millilitresPerUnit) / 5m);
            if (teaspoons < 0.5m)
            {
                teaspoons = 0.5m;
            }

            parts.Add(FormatFractionalAmount(teaspoons, "tsp", "tsp"));
        }

        if (parts.Count > 0)
        {
            return string.Join(" + ", parts);
        }

        var roundedContainerQuantity = decimal.Round(quantity, 2, MidpointRounding.AwayFromZero);
        return $"{roundedContainerQuantity:0.##} {ToDisplayUnit(normalizedUnit, roundedContainerQuantity)}";
    }

    internal static bool TryConvertToMillilitres(decimal quantity, string? unit, out decimal totalMillilitres)
    {
        totalMillilitres = 0m;
        if (quantity <= 0m)
        {
            return false;
        }

        var normalizedUnit = NormalizeUnit(unit);
        if (RecipeLitreUnits.Contains(normalizedUnit))
        {
            totalMillilitres = quantity * 1000m;
            return true;
        }

        if (RecipeMillilitreUnits.Contains(normalizedUnit))
        {
            totalMillilitres = quantity;
            return true;
        }

        if (TeaspoonUnits.Contains(normalizedUnit))
        {
            totalMillilitres = quantity * 5m;
            return true;
        }

        if (TablespoonUnits.Contains(normalizedUnit))
        {
            totalMillilitres = quantity * 15m;
            return true;
        }

        if (RecipeContainerUnitMillilitres.TryGetValue(normalizedUnit, out var millilitresPerUnit))
        {
            totalMillilitres = quantity * millilitresPerUnit;
            return true;
        }

        return false;
    }

    private static decimal RoundUpToNearest(decimal value, decimal step)
    {
        if (step <= 0m)
        {
            return value;
        }

        return decimal.Ceiling(value / step) * step;
    }

    private static bool TryFormatSpoonQuantity(decimal quantity, string normalizedUnit, out string formattedQuantity)
    {
        formattedQuantity = string.Empty;

        if (TeaspoonUnits.Contains(normalizedUnit))
        {
            formattedQuantity = FormatFractionalAmount(RoundToNearestQuarter(quantity), "tsp", "tsp");
            return true;
        }

        if (!TablespoonUnits.Contains(normalizedUnit))
        {
            return false;
        }

        var roundedTablespoons = RoundToNearestQuarter(quantity);
        if (roundedTablespoons < 1m)
        {
            var teaspoons = RoundToNearestQuarter(quantity * 3m);
            formattedQuantity = FormatFractionalAmount(teaspoons, "tsp", "tsp");
            return true;
        }

        formattedQuantity = FormatFractionalAmount(roundedTablespoons, "tbsp", "tbsp");
        return true;
    }

    private static string FormatShoppingLiquidVolume(decimal totalMillilitres)
    {
        if (totalMillilitres <= 0m)
        {
            return "-";
        }

        if (totalMillilitres < 15m)
        {
            var teaspoons = RoundToNearestQuarter(totalMillilitres / 5m);
            return FormatFractionalAmount(teaspoons, "tsp", "tsp");
        }

        if (totalMillilitres <= 120m)
        {
            var tablespoons = RoundToNearestQuarter(totalMillilitres / 15m);
            return FormatFractionalAmount(tablespoons, "tbsp", "tbsp");
        }

        var roundedUpMillilitres = RoundUpToNearest(
            decimal.Round(totalMillilitres, 0, MidpointRounding.AwayFromZero),
            5m);
        return $"{roundedUpMillilitres:0} ml";
    }

    private static string FormatRecipeLiquidVolume(decimal totalMillilitres)
    {
        if (totalMillilitres <= 0m)
        {
            return "-";
        }

        if (totalMillilitres <= 30m)
        {
            var teaspoons = RoundToNearestHalf(totalMillilitres / 5m);
            var minimumDisplay = 0.5m;
            if (teaspoons < minimumDisplay)
            {
                teaspoons = minimumDisplay;
            }

            if (teaspoons >= 3m)
            {
                var tablespoons = RoundToNearestHalf(teaspoons / 3m);
                return FormatFractionalAmount(tablespoons, "tbsp", "tbsp");
            }

            return FormatFractionalAmount(teaspoons, "tsp", "tsp");
        }

        var roundedUpMillilitres = RoundUpToNearest(
            decimal.Round(totalMillilitres, 0, MidpointRounding.AwayFromZero),
            5m);
        return $"{roundedUpMillilitres:0} ml";
    }

    private static string FormatRecipeCountQuantity(decimal quantity, string normalizedUnit)
    {
        var roundedToQuarter = RoundToNearestQuarter(quantity);
        if (roundedToQuarter == 0.5m)
        {
            return $"half a {ToDisplayUnit(normalizedUnit, 1)}";
        }

        return FormatFractionalAmount(
            roundedToQuarter,
            ToDisplayUnit(normalizedUnit, 1),
            ToDisplayUnit(normalizedUnit, roundedToQuarter));
    }

    private static string FormatFractionalAmount(decimal quantity, string? singularUnit, string? pluralUnit)
    {
        var whole = decimal.Truncate(quantity);
        var fraction = quantity - whole;
        var fractionText = fraction switch
        {
            0.25m => "1/4",
            0.5m => "1/2",
            0.75m => "3/4",
            _ => string.Empty
        };

        var unit = string.Empty;
        if (!string.IsNullOrWhiteSpace(singularUnit) || !string.IsNullOrWhiteSpace(pluralUnit))
        {
            var useSingular = quantity <= 1m;
            unit = useSingular ? singularUnit ?? string.Empty : pluralUnit ?? string.Empty;
        }

        if (fraction == 0m)
        {
            return string.IsNullOrWhiteSpace(unit)
                ? $"{whole:0}"
                : $"{whole:0} {unit}";
        }

        if (whole == 0m)
        {
            return string.IsNullOrWhiteSpace(unit)
                ? fractionText
                : $"{fractionText} {unit}";
        }

        return string.IsNullOrWhiteSpace(unit)
            ? $"{whole:0} {fractionText}"
            : $"{whole:0} {fractionText} {unit}";
    }

    private static decimal RoundToNearestQuarter(decimal value)
    {
        if (value <= 0m)
        {
            return 0m;
        }

        var rounded = decimal.Round(value * 4m, 0, MidpointRounding.AwayFromZero) / 4m;
        return rounded == 0m ? 0.25m : rounded;
    }

    private static decimal RoundToNearestHalf(decimal value)
    {
        if (value <= 0m)
        {
            return 0m;
        }

        return decimal.Round(value * 2m, 0, MidpointRounding.AwayFromZero) / 2m;
    }
}
