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
    private IReadOnlyDictionary<string, string> ResolveMealImageUrls(IReadOnlyList<MealTemplate> selectedMeals)
    {
        return ResolveMealImageUrlsAsync(selectedMeals).GetAwaiter().GetResult();
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveMealImageUrlsAsync(
        IReadOnlyList<MealTemplate> selectedMeals,
        CancellationToken cancellationToken = default)
    {
        await EnsureMealImagePoolHydratedAsync(
            selectedMeals.Select(meal => meal.Name).ToList(),
            cancellationToken);

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meal in selectedMeals)
        {
            var normalizedEmbeddedUrl = NormalizeImageUrl(meal.ImageUrl);
            if (!string.IsNullOrWhiteSpace(normalizedEmbeddedUrl) && IsMealImageUrlUsable(normalizedEmbeddedUrl))
            {
                MealImagePool[meal.Name] = normalizedEmbeddedUrl;
                resolved[meal.Name] = normalizedEmbeddedUrl;
                continue;
            }

            if (TryGetCachedMealImageUrl(meal.Name, out var cachedUrl))
            {
                resolved[meal.Name] = cachedUrl;
                continue;
            }

            if (TryGetBundledMealImageUrl(meal.Name, out var bundledUrl))
            {
                resolved[meal.Name] = bundledUrl;
                continue;
            }

            resolved[meal.Name] = GetFallbackMealImageUrl();
            QueueMealImageGeneration(meal);
        }

        return resolved;
    }

    private bool TryGetCachedMealImageUrl(string mealName, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (!MealImagePool.TryGetValue(mealName, out var cached))
        {
            return false;
        }

        var normalized = NormalizeImageUrl(cached);
        if (string.IsNullOrWhiteSpace(normalized) || !IsMealImageUrlUsable(normalized))
        {
            MealImagePool.TryRemove(mealName, out _);
            return false;
        }

        imageUrl = normalized;
        ClearMealImageLookupMiss(mealName);
        return true;
    }

    private bool TryGetBundledMealImageUrl(string mealName, out string imageUrl)
    {
        imageUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(mealName))
        {
            return false;
        }

        var candidateUrl = $"/images/aislepilot-meals/{ToAiMealDocumentId(mealName)}.png";
        if (!TryResolveMealImageDiskPath(candidateUrl, out var fullPath) || !File.Exists(fullPath))
        {
            return false;
        }

        MealImagePool[mealName] = candidateUrl;
        ClearMealImageLookupMiss(mealName);
        imageUrl = candidateUrl;
        return true;
    }

    private static bool ShouldSkipMealImageLookup(string mealName, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(mealName))
        {
            return false;
        }

        if (!MealImageLookupMissesUtc.TryGetValue(mealName, out var blockedUntilUtc))
        {
            return false;
        }

        if (blockedUntilUtc <= nowUtc)
        {
            MealImageLookupMissesUtc.TryRemove(mealName, out _);
            return false;
        }

        return true;
    }

    private static void MarkMealImageLookupMiss(string mealName, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(mealName))
        {
            return;
        }

        MealImageLookupMissesUtc[mealName] = nowUtc + MealImageLookupMissTtl;
        PruneMealImageLookupMisses(nowUtc);
    }

    private static void ClearMealImageLookupMiss(string mealName)
    {
        if (string.IsNullOrWhiteSpace(mealName))
        {
            return;
        }

        MealImageLookupMissesUtc.TryRemove(mealName, out _);
    }

    private static void PruneMealImageLookupMisses(DateTime nowUtc)
    {
        foreach (var entry in MealImageLookupMissesUtc)
        {
            if (entry.Value <= nowUtc)
            {
                MealImageLookupMissesUtc.TryRemove(entry.Key, out _);
            }
        }

        var overflowCount = MealImageLookupMissesUtc.Count - MaxMealImageMissCacheEntries;
        if (overflowCount <= 0)
        {
            return;
        }

        var evictionCandidates = MealImageLookupMissesUtc
            .OrderBy(entry => entry.Value)
            .Take(overflowCount)
            .Select(entry => entry.Key)
            .ToList();
        foreach (var mealName in evictionCandidates)
        {
            MealImageLookupMissesUtc.TryRemove(mealName, out _);
        }
    }

    private static string GetFallbackMealImageUrl()
    {
        return "/images/aislepilot-icon.svg";
    }

    private bool IsMealImageUrlUsable(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out _) &&
            !imageUrl.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        if (!imageUrl.StartsWith("/images/aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryResolveMealImageDiskPath(imageUrl, out var fullPath))
        {
            return false;
        }

        return File.Exists(fullPath);
    }

    private bool TryResolveMealImageDiskPath(string imageUrl, out string fullPath)
    {
        fullPath = string.Empty;
        if (_webHostEnvironment is null || string.IsNullOrWhiteSpace(_webHostEnvironment.WebRootPath))
        {
            return false;
        }

        var normalized = NormalizeImageUrl(imageUrl);
        if (!normalized.StartsWith("/images/aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relativePath = normalized.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var rootFullPath = Path.GetFullPath(_webHostEnvironment.WebRootPath);
        var combinedPath = Path.GetFullPath(Path.Combine(rootFullPath, relativePath));
        if (!combinedPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = combinedPath;
        return true;
    }

    private void EnsureMealImagePoolHydrated(IEnumerable<string> mealNames)
    {
        try
        {
            EnsureMealImagePoolHydratedAsync(mealNames.ToList()).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to hydrate AislePilot meal image cache from Firestore.");
        }
    }

    private async Task EnsureMealImagePoolHydratedAsync(
        IReadOnlyList<string> mealNames,
        CancellationToken cancellationToken = default)
    {
        if (_db is null || mealNames.Count == 0)
        {
            return;
        }

        var distinctMealNames = mealNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctMealNames.Count == 0)
        {
            return;
        }

        await MealImagePoolRefreshLock.WaitAsync(cancellationToken);
        try
        {
            var nowUtc = DateTime.UtcNow;
            foreach (var mealName in distinctMealNames)
            {
                if (TryGetCachedMealImageUrl(mealName, out _))
                {
                    continue;
                }

                if (TryGetBundledMealImageUrl(mealName, out _))
                {
                    continue;
                }

                if (ShouldSkipMealImageLookup(mealName, nowUtc))
                {
                    continue;
                }

                var docId = ToAiMealDocumentId(mealName);
                var doc = await _db.Collection(MealImagesCollection).Document(docId).GetSnapshotAsync(cancellationToken);
                if (!doc.Exists)
                {
                    MarkMealImageLookupMiss(mealName, nowUtc);
                    continue;
                }

                FirestoreAislePilotMealImage? mapped;
                try
                {
                    mapped = doc.ConvertTo<FirestoreAislePilotMealImage>();
                }
                catch
                {
                    MarkMealImageLookupMiss(mealName, nowUtc);
                    continue;
                }

                if (mapped is null)
                {
                    MarkMealImageLookupMiss(mealName, nowUtc);
                    continue;
                }

                var normalizedName = string.IsNullOrWhiteSpace(mapped.Name)
                    ? mealName
                    : mapped.Name.Trim();
                var normalizedUrl = NormalizeImageUrl(mapped.ImageUrl);
                if (string.IsNullOrWhiteSpace(normalizedUrl))
                {
                    MarkMealImageLookupMiss(mealName, nowUtc);
                    continue;
                }

                var imageBase64 = string.IsNullOrWhiteSpace(mapped.ImageBase64)
                    ? await TryReadMealImageBase64FromChunksAsync(docId, mapped.ImageChunkCount, cancellationToken)
                    : mapped.ImageBase64;
                if (!await EnsureMealImageAvailableAsync(normalizedUrl, imageBase64, cancellationToken))
                {
                    MarkMealImageLookupMiss(mealName, nowUtc);
                    continue;
                }

                MealImagePool[normalizedName] = normalizedUrl;
                ClearMealImageLookupMiss(mealName);
                ClearMealImageLookupMiss(normalizedName);

                if (string.IsNullOrWhiteSpace(mapped.ImageBase64) && mapped.ImageChunkCount <= 0)
                {
                    var diskBytes = await TryReadMealImageBytesFromDiskAsync(normalizedUrl, cancellationToken);
                    if (diskBytes is { Length: > 0 })
                    {
                        await PersistMealImageAsync(normalizedName, normalizedUrl, diskBytes, cancellationToken);
                    }
                }
            }

            _lastMealImagePoolRefreshUtc = nowUtc;
        }
        finally
        {
            MealImagePoolRefreshLock.Release();
        }
    }

    private async Task<bool> EnsureMealImageAvailableAsync(
        string imageUrl,
        string? imageBase64,
        CancellationToken cancellationToken)
    {
        if (!imageUrl.StartsWith("/images/aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsMealImageUrlUsable(imageUrl))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(imageBase64))
        {
            return false;
        }

        return await TryRestoreMealImageFromBase64Async(imageUrl, imageBase64, cancellationToken);
    }

}
