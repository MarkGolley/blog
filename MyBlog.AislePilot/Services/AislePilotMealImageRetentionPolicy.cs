using System.Text.RegularExpressions;

namespace MyBlog.Services;

public sealed record AislePilotMealImageDiskEntry(string FullPath, DateTime LastWriteUtc);

public static partial class AislePilotMealImageRetentionPolicy
{
    public static IReadOnlyList<AislePilotMealImageDiskEntry> SelectSupersededDiskFiles(
        IEnumerable<AislePilotMealImageDiskEntry> files,
        IReadOnlySet<string> protectedFileNames,
        DateTime deleteBeforeUtc,
        int maximumDeletes)
    {
        var boundedMaximum = Math.Clamp(maximumDeletes, 1, 500);
        return files
            .Where(file => AislePilotMealImageVersioning.IsVersionedPath(file.FullPath))
            .GroupBy(file => GetVersionFamily(Path.GetFileName(file.FullPath)), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group
                .OrderByDescending(file => file.LastWriteUtc)
                .ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
                .Skip(1))
            .Where(file => file.LastWriteUtc < deleteBeforeUtc)
            .Where(file => !protectedFileNames.Contains(Path.GetFileName(file.FullPath)))
            .OrderBy(file => file.LastWriteUtc)
            .ThenBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .Take(boundedMaximum)
            .ToList();
    }

    public static bool ShouldDeleteFirestoreRecord(
        string? mealName,
        DateTime updatedAtUtc,
        IReadOnlySet<string> protectedMealNames,
        DateTime deleteBeforeUtc)
    {
        return !string.IsNullOrWhiteSpace(mealName) &&
               updatedAtUtc != default &&
               updatedAtUtc < deleteBeforeUtc &&
               !protectedMealNames.Contains(mealName.Trim());
    }

    private static string GetVersionFamily(string fileName)
    {
        return VersionSuffixRegex().Replace(fileName, "$1");
    }

    [GeneratedRegex(@"-[a-f0-9]{16}(\.(?:jpe?g|png|webp|avif))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionSuffixRegex();
}
