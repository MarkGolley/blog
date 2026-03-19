namespace MyBlog.Utilities;

public static class QuantityDisplayFormatter
{
    public static string Format(decimal quantity, string? unit)
    {
        if (quantity <= 0)
        {
            return "-";
        }

        var normalizedUnit = (unit ?? string.Empty).Trim();

        if (string.Equals(normalizedUnit, "kg", StringComparison.OrdinalIgnoreCase))
        {
            if (quantity > 1m)
            {
                var roundedKilograms = decimal.Round(quantity, 1, MidpointRounding.AwayFromZero);
                return $"{roundedKilograms:0.0} kg";
            }

            var grams = decimal.Round(quantity * 1000m, 0, MidpointRounding.AwayFromZero);
            return $"{grams:0} g";
        }

        var roundedQuantity = decimal.Round(quantity, 2, MidpointRounding.AwayFromZero);
        return string.IsNullOrWhiteSpace(normalizedUnit)
            ? $"{roundedQuantity:0.##}"
            : $"{roundedQuantity:0.##} {normalizedUnit}";
    }
}
