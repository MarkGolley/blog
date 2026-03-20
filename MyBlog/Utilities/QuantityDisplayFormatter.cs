namespace MyBlog.Utilities;

public static class QuantityDisplayFormatter
{
    private static readonly HashSet<string> WholePurchaseUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "bottle",
        "bottles",
        "jar",
        "jars",
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

        if (WholePurchaseUnits.Contains(normalizedUnit))
        {
            var unitsToBuy = (int)Math.Ceiling(quantity);
            return $"{unitsToBuy} {ToDisplayUnit(normalizedUnit, unitsToBuy)}";
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

    private static string ToDisplayUnit(string normalizedUnit, int quantity)
    {
        return normalizedUnit switch
        {
            "bottle" or "bottles" => quantity == 1 ? "bottle" : "bottles",
            "jar" or "jars" => quantity == 1 ? "jar" : "jars",
            "pack" or "packs" => quantity == 1 ? "pack" : "packs",
            "tin" or "tins" => quantity == 1 ? "tin" : "tins",
            "head" or "heads" => quantity == 1 ? "head" : "heads",
            "pc" or "pcs" or "piece" or "pieces" => quantity == 1 ? "pc" : "pcs",
            _ => normalizedUnit
        };
    }
}
