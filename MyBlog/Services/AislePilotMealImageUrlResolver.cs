namespace MyBlog.Services;

public static class AislePilotMealImageUrlResolver
{
    private const string AislePilotImagePrefix = "/projects/aisle-pilot/images/";
    private const string DefaultMealImageUrl = "/projects/aisle-pilot/images/aislepilot-icon.svg";

    public static string ResolveClientMealImageUrl(string? imageUrl)
    {
        var normalized = imageUrl?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultMealImageUrl;
        }

        if (normalized.StartsWith(AislePilotImagePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        if (normalized.StartsWith("/images/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{AislePilotImagePrefix}{normalized["/images/".Length..]}";
        }

        normalized = normalized.Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            var trimmed = normalized.TrimStart('/');
            if (trimmed.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            {
                return $"{AislePilotImagePrefix}{trimmed["images/".Length..]}";
            }

            if (trimmed.StartsWith("aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
            {
                return $"{AislePilotImagePrefix}{trimmed}";
            }

            var hasImageExtension =
                trimmed.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            if (hasImageExtension)
            {
                return $"{AislePilotImagePrefix}aislepilot-meals/{trimmed}";
            }
        }

        return normalized;
    }
}
