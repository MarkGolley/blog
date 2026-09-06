using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace MyBlog.Services;

public static partial class AislePilotMealImageVersioning
{
    public static string BuildFileName(string mealSlug, ReadOnlySpan<byte> imageBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mealSlug);
        if (imageBytes.IsEmpty)
        {
            throw new ArgumentException("Image content cannot be empty.", nameof(imageBytes));
        }

        var contentHash = SHA256.HashData(imageBytes);
        var version = Convert.ToHexString(contentHash.AsSpan(0, 8)).ToLowerInvariant();
        return $"{mealSlug}-{version}.jpg";
    }

    public static bool IsVersionedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.Replace('\\', '/').Contains("/aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return VersionedFileNameRegex().IsMatch(Path.GetFileName(path));
    }

    [GeneratedRegex(@"-[a-f0-9]{16}\.(?:jpe?g|png|webp|avif)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionedFileNameRegex();
}
