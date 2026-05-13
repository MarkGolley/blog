namespace MyBlog.Services;

public static class AislePilotMealImageUrlResolver
{
    private const string AislePilotImagePrefix = "/projects/aisle-pilot/images/";
    private const string DefaultMealImageUrl = "/projects/aisle-pilot/images/aislepilot-icon.svg";
    private static readonly char[] MealImageSlugChars = ['-', '_'];

    public static string ResolveClientMealImageUrl(string? imageUrl)
    {
        var normalized = imageUrl?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultMealImageUrl;
        }

        if (normalized.StartsWith(AislePilotImagePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var relativeFromImagesRoot = normalized[AislePilotImagePrefix.Length..];
            if (TryResolveLegacyMealImageSlugPath(relativeFromImagesRoot, out var resolvedLegacyImagesPath))
            {
                return $"{AislePilotImagePrefix}{resolvedLegacyImagesPath}";
            }

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
            var relativeFromImagesRoot = normalized["/images/".Length..];
            if (TryResolveLegacyMealImageSlugPath(relativeFromImagesRoot, out var resolvedLegacyImagesPath))
            {
                return $"{AislePilotImagePrefix}{resolvedLegacyImagesPath}";
            }

            return $"{AislePilotImagePrefix}{relativeFromImagesRoot}";
        }

        normalized = normalized.Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            var trimmed = normalized.TrimStart('/');
            if (trimmed.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            {
                var relativeFromImagesRoot = trimmed["images/".Length..];
                if (TryResolveLegacyMealImageSlugPath(relativeFromImagesRoot, out var resolvedLegacyImagesPath))
                {
                    return $"{AislePilotImagePrefix}{resolvedLegacyImagesPath}";
                }

                return $"{AislePilotImagePrefix}{relativeFromImagesRoot}";
            }

            if (trimmed.StartsWith("aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
            {
                if (TryResolveLegacyMealImageSlugPath(trimmed, out var resolvedLegacyMealPath))
                {
                    return $"{AislePilotImagePrefix}{resolvedLegacyMealPath}";
                }

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

            if (IsLegacyMealImageSlug(trimmed))
            {
                return $"{AislePilotImagePrefix}aislepilot-meals/{trimmed}.png";
            }
        }

        return normalized;
    }

    private static bool TryResolveLegacyMealImageSlugPath(string path, out string resolvedPath)
    {
        resolvedPath = path;
        var normalizedPath = path.Trim();
        if (!normalizedPath.StartsWith("aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relativeMealPath = normalizedPath["aislepilot-meals/".Length..];
        if (relativeMealPath.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var hasImageExtension =
            relativeMealPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            relativeMealPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            relativeMealPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            relativeMealPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            relativeMealPath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
        if (hasImageExtension)
        {
            return false;
        }

        if (!IsLegacyMealImageSlug(relativeMealPath))
        {
            return false;
        }

        resolvedPath = $"aislepilot-meals/{relativeMealPath}.png";
        return true;
    }

    private static bool IsLegacyMealImageSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains('/') || value.Contains('\\') || value.Contains(' '))
        {
            return false;
        }

        return value.All(ch => char.IsLetterOrDigit(ch) || MealImageSlugChars.Contains(ch));
    }
}
