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

    private async Task<bool> TryRestoreMealImageFromBase64Async(
        string imageUrl,
        string imageBase64,
        CancellationToken cancellationToken)
    {
        if (!TryResolveMealImageDiskPath(imageUrl, out var fullPath))
        {
            return false;
        }

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(imageBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (imageBytes.Length == 0)
        {
            return false;
        }

        try
        {
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return false;
            }

            Directory.CreateDirectory(directoryPath);
            await File.WriteAllBytesAsync(fullPath, imageBytes, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to restore AislePilot meal image file for url '{ImageUrl}'.", imageUrl);
            return false;
        }
    }

    private async Task<byte[]?> TryReadMealImageBytesFromDiskAsync(
        string imageUrl,
        CancellationToken cancellationToken)
    {
        if (!TryResolveMealImageDiskPath(imageUrl, out var fullPath) || !File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            return bytes.Length == 0 ? null : bytes;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryReadMealImageBase64FromChunksAsync(
        string docId,
        int expectedChunkCount,
        CancellationToken cancellationToken)
    {
        if (_db is null || string.IsNullOrWhiteSpace(docId) || expectedChunkCount <= 0)
        {
            return null;
        }

        try
        {
            var chunkSnapshot = await _db.Collection(MealImagesCollection)
                .Document(docId)
                .Collection(MealImageChunksSubcollection)
                .GetSnapshotAsync(cancellationToken);
            if (chunkSnapshot.Documents.Count == 0)
            {
                return null;
            }

            var chunks = chunkSnapshot.Documents
                .Select(doc =>
                {
                    try
                    {
                        return doc.ConvertTo<FirestoreAislePilotMealImageChunk>();
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(chunk => chunk is not null && !string.IsNullOrWhiteSpace(chunk.Data))
                .OrderBy(chunk => chunk!.Index)
                .ToList();
            if (chunks.Count == 0)
            {
                return null;
            }

            var joined = string.Concat(chunks.Select(chunk => chunk!.Data));
            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to read meal image backup chunks for doc '{DocId}'.", docId);
            return null;
        }
    }

    private async Task PersistMealImageChunksAsync(
        DocumentReference docRef,
        IReadOnlyList<string> chunks,
        CancellationToken cancellationToken)
    {
        if (_db is null)
        {
            return;
        }

        var chunkCollection = docRef.Collection(MealImageChunksSubcollection);
        const int batchSize = 450;
        for (var offset = 0; offset < chunks.Count; offset += batchSize)
        {
            var batch = _db.StartBatch();
            var count = Math.Min(batchSize, chunks.Count - offset);
            for (var i = 0; i < count; i++)
            {
                var chunkIndex = offset + i;
                var chunkDocId = chunkIndex.ToString("D4", CultureInfo.InvariantCulture);
                var chunkRef = chunkCollection.Document(chunkDocId);
                batch.Set(
                    chunkRef,
                    new FirestoreAislePilotMealImageChunk
                    {
                        Index = chunkIndex,
                        Data = chunks[chunkIndex]
                    });
            }

            await batch.CommitAsync(cancellationToken);
        }
    }

    private async Task DeleteMealImageChunksAsync(DocumentReference docRef, CancellationToken cancellationToken)
    {
        if (_db is null)
        {
            return;
        }

        try
        {
            var snapshot = await docRef.Collection(MealImageChunksSubcollection).GetSnapshotAsync(cancellationToken);
            if (snapshot.Documents.Count == 0)
            {
                return;
            }

            const int batchSize = 450;
            for (var offset = 0; offset < snapshot.Documents.Count; offset += batchSize)
            {
                var batch = _db.StartBatch();
                var count = Math.Min(batchSize, snapshot.Documents.Count - offset);
                for (var i = 0; i < count; i++)
                {
                    batch.Delete(snapshot.Documents[offset + i].Reference);
                }

                await batch.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed deleting meal image backup chunks for '{DocId}'.", docRef.Id);
        }
    }

    internal void QueueSpecialTreatGeneration(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<string>? excludedMealNames = null)
    {
        if (!request.IncludeSpecialTreatMeal || !CanAttemptAiGenerationForPlanRequest())
        {
            return;
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        if (!mealTypeSlots.Any(slot => slot.Equals("Dinner", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var key = BuildSpecialTreatBackgroundKey(request, context);
        if (!SpecialTreatGenerationInFlight.TryAdd(key, 1))
        {
            return;
        }

        var requestSnapshot = CloneRequest(request);
        var excludedNames = (excludedMealNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                using var generationBudgetCts = new CancellationTokenSource(OpenAiGenerationBudget);
                var generatedTreat = await TryGenerateSpecialTreatMealWithAiAsync(
                    requestSnapshot,
                    context,
                    excludedNames,
                    generationBudgetCts.Token);
                if (generatedTreat is null)
                {
                    return;
                }

                var persistedTreatMeals = await PersistAiMealsAsync([generatedTreat], generationBudgetCts.Token);
                if (persistedTreatMeals.Count > 0)
                {
                    AddMealsToAiPool(persistedTreatMeals);
                }
                else
                {
                    AddMealsToAiPool([generatedTreat]);
                }

                QueueMealImageGeneration(generatedTreat);
                _logger?.LogInformation(
                    "AislePilot generated a deferred special treat meal in the background: {MealName}",
                    generatedTreat.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AislePilot deferred special treat generation failed.");
            }
            finally
            {
                SpecialTreatGenerationInFlight.TryRemove(key, out _);
            }
        });
    }

    private void QueueDessertAddOnRecovery(string? selectedDessertAddOnName)
    {
        var key = string.IsNullOrWhiteSpace(selectedDessertAddOnName)
            ? "__default__"
            : ToAiMealDocumentId(selectedDessertAddOnName.Trim());
        if (!DessertAddOnRecoveryInFlight.TryAdd(key, 1))
        {
            return;
        }

        var selectedDessertNameSnapshot = selectedDessertAddOnName;
        _ = Task.Run(async () =>
        {
            try
            {
                var resolvedTemplate = await ResolveDessertAddOnTemplateAsync(
                    selectedDessertNameSnapshot,
                    CancellationToken.None);
                await PersistDessertAddOnTemplateAsync(resolvedTemplate, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AislePilot deferred dessert add-on recovery failed.");
            }
            finally
            {
                DessertAddOnRecoveryInFlight.TryRemove(key, out _);
            }
        });
    }

    private string BuildSpecialTreatBackgroundKey(AislePilotRequestModel request, PlanContext context)
    {
        var dietaryKey = string.Join(
            "|",
            context.DietaryModes
                .Where(mode => !string.IsNullOrWhiteSpace(mode))
                .Select(mode => mode.Trim())
                .OrderBy(mode => mode, StringComparer.OrdinalIgnoreCase));
        var mealTypeKey = string.Join("|", BuildMealTypeSlots(request));
        var dislikesKey = string.IsNullOrWhiteSpace(context.DislikesOrAllergens)
            ? string.Empty
            : context.DislikesOrAllergens.Trim().ToLowerInvariant();
        return string.Join(
            "|",
            context.Supermarket,
            context.PortionSize,
            request.HouseholdSize.ToString(CultureInfo.InvariantCulture),
            mealTypeKey,
            dietaryKey,
            dislikesKey);
    }

    private void QueueMealImageGeneration(MealTemplate meal)
    {
        if (!CanGenerateMealImages())
        {
            return;
        }

        var key = ToAiMealDocumentId(meal.Name);
        if (!MealImageGenerationInFlight.TryAdd(key, 1))
        {
            return;
        }

        MarkMealImageLookupMiss(meal.Name, DateTime.UtcNow);

        _ = Task.Run(async () =>
        {
            var throttleAcquired = false;
            try
            {
                await MealImageGenerationThrottle.WaitAsync(CancellationToken.None);
                throttleAcquired = true;
                if (TryGetCachedMealImageUrl(meal.Name, out _))
                {
                    return;
                }

                var imageBytes = await TryGenerateMealImageBytesWithAiAsync(meal, CancellationToken.None);
                if (imageBytes is null || imageBytes.Length == 0)
                {
                    return;
                }

                var imageUrl = await SaveMealImageToDiskAsync(meal.Name, imageBytes, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    return;
                }

                MealImagePool[meal.Name] = imageUrl;
                ClearMealImageLookupMiss(meal.Name);

                if (AiMealPool.TryGetValue(meal.Name, out var existingMeal))
                {
                    UpsertAiMealPoolEntry(existingMeal with { ImageUrl = imageUrl }, DateTime.UtcNow);
                }

                await PersistMealImageAsync(meal.Name, imageUrl, imageBytes, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AislePilot meal image generation failed for '{MealName}'.", meal.Name);
            }
            finally
            {
                if (throttleAcquired)
                {
                    MealImageGenerationThrottle.Release();
                }

                MealImageGenerationInFlight.TryRemove(key, out _);
            }
        });
    }

    private async Task<byte[]?> TryGenerateMealImageBytesWithAiAsync(
        MealTemplate meal,
        CancellationToken cancellationToken)
    {
        if (_httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var requestBody = new
        {
            model = _imageModel,
            prompt = BuildAiMealImagePrompt(meal),
            size = "auto",
            quality = "low",
            n = 1
        };
        var serializedBody = JsonSerializer.Serialize(requestBody);

        for (var attempt = 1; attempt <= OpenAiImageMaxAttempts; attempt++)
        {
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCts.CancelAfter(OpenAiImageRequestTimeout);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, OpenAiImageGenerationsEndpoint)
            {
                Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            try
            {
                using var response = await _httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestCts.Token);
                var responseContent = await response.Content.ReadAsStringAsync(CancellationToken.None);
                if (!response.IsSuccessStatusCode)
                {
                    var errorSample = responseContent.Length <= 220 ? responseContent : responseContent[..220];
                    _logger?.LogWarning(
                        "AislePilot meal image request failed with status {StatusCode}. Attempt={Attempt}/{MaxAttempts}. ResponseSample={ResponseSample}",
                        (int)response.StatusCode,
                        attempt,
                        OpenAiImageMaxAttempts,
                        errorSample);
                    continue;
                }

                var payload = JsonSerializer.Deserialize<OpenAiImageGenerationResponse>(responseContent, JsonOptions);
                var imageData = payload?.Data?.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(imageData?.B64Json))
                {
                    try
                    {
                        return Convert.FromBase64String(imageData.B64Json);
                    }
                    catch (FormatException)
                    {
                        _logger?.LogWarning("AislePilot meal image payload had invalid base64 data for '{MealName}'.", meal.Name);
                    }
                }

                if (!string.IsNullOrWhiteSpace(imageData?.Url))
                {
                    try
                    {
                        using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        downloadCts.CancelAfter(OpenAiImageDownloadTimeout);
                        using var imageResponse = await _httpClient.GetAsync(
                            imageData.Url,
                            HttpCompletionOption.ResponseHeadersRead,
                            downloadCts.Token);
                        if (imageResponse.IsSuccessStatusCode)
                        {
                            return await imageResponse.Content.ReadAsByteArrayAsync(CancellationToken.None);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.LogInformation(
                            "AislePilot image CDN fetch timed out for '{MealName}'. Attempt={Attempt}/{MaxAttempts}.",
                            meal.Name,
                            attempt,
                            OpenAiImageMaxAttempts);
                    }
                    catch
                    {
                        // Ignore and return null below.
                    }
                }
            }
            catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
            {
                _logger?.LogInformation(
                    "AislePilot meal image request timed out for '{MealName}'. Attempt={Attempt}/{MaxAttempts}.",
                    meal.Name,
                    attempt,
                    OpenAiImageMaxAttempts);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AislePilot meal image request failed for '{MealName}'.", meal.Name);
            }
        }

        return null;
    }

    private static string BuildAiMealImagePrompt(MealTemplate meal)
    {
        var ingredientNames = meal.Ingredients
            .Select(ingredient => ingredient.Name.Trim().ToLowerInvariant())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        var ingredientText = string.Join(
            ", ",
            ingredientNames.Take(6));
        if (string.IsNullOrWhiteSpace(ingredientText))
        {
            ingredientText = "seasonal ingredients";
        }

        var stapleTokens = new[]
        {
            "rice",
            "pasta",
            "noodle",
            "bread",
            "potato"
        };
        var excludedStapleNames = stapleTokens
            .Where(token => ingredientNames.All(name => !ContainsWholeWord(name, token)))
            .ToArray();
        var excludedStapleRule = excludedStapleNames.Length == 0
            ? string.Empty
            : $"Do not depict {string.Join(", ", excludedStapleNames)} unless included in the ingredient list.";

        return $"""
Create a photorealistic hero image of the finished plated dish: "{meal.Name}".
Style: natural light food photography, 45-degree angle, realistic textures, appetising and modern.
Use this ingredient list as ground truth: {ingredientText}.
Keep ingredient visuals consistent with that list and avoid adding extra staple carbs.
{excludedStapleRule}
Single plated meal only, neutral background, no people, no text, no logos, no watermarks.
""";
    }

    private async Task<string?> SaveMealImageToDiskAsync(
        string mealName,
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        if (_webHostEnvironment is null || string.IsNullOrWhiteSpace(_webHostEnvironment.WebRootPath))
        {
            return null;
        }

        var directoryPath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "aislepilot-meals");
        Directory.CreateDirectory(directoryPath);

        var fileName = $"{ToAiMealDocumentId(mealName)}.png";
        var filePath = Path.Combine(directoryPath, fileName);
        await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken);
        return $"/images/aislepilot-meals/{fileName}";
    }

    private async Task PersistMealImageAsync(
        string mealName,
        string imageUrl,
        byte[]? imageBytes,
        CancellationToken cancellationToken)
    {
        if (_db is null || string.IsNullOrWhiteSpace(mealName) || string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        try
        {
            var docRef = _db.Collection(MealImagesCollection).Document(ToAiMealDocumentId(mealName));
            var imageBase64 = string.Empty;
            var imageChunkCount = 0;
            if (imageBytes is { Length: > 0 and <= MaxMealImageBytesForFirestore })
            {
                imageBase64 = Convert.ToBase64String(imageBytes);
                await DeleteMealImageChunksAsync(docRef, cancellationToken);
            }
            else if (imageBytes is { Length: > MaxMealImageBytesForFirestore })
            {
                var oversizedBase64 = Convert.ToBase64String(imageBytes);
                var chunks = SplitBase64IntoChunks(oversizedBase64, MealImageChunkCharLength);
                imageChunkCount = chunks.Count;
                await DeleteMealImageChunksAsync(docRef, cancellationToken);
                await PersistMealImageChunksAsync(docRef, chunks, cancellationToken);
                _logger?.LogInformation(
                    "Persisted oversized meal image backup for '{MealName}' as {ChunkCount} Firestore chunks.",
                    mealName,
                    imageChunkCount);
            }

            await docRef.SetAsync(
                new FirestoreAislePilotMealImage
                {
                    Name = mealName,
                    ImageUrl = imageUrl,
                    ImageBase64 = imageBase64,
                    ImageChunkCount = imageChunkCount,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Source = "openai-image"
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "AislePilot failed to persist meal image for '{MealName}'.",
                mealName);
        }
    }

    private static string NormalizeImageUrl(string? imageUrl)
    {
        var normalized = imageUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = normalized.Replace('\\', '/');

        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        if (normalized.StartsWith("/projects/aisle-pilot/images/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/images/{normalized["/projects/aisle-pilot/images/".Length..]}";
        }

        if (normalized.StartsWith("/images/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/{normalized}";
        }

        if (normalized.StartsWith("aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/images/{normalized}";
        }

        var trimmed = normalized.TrimStart('/');
        var hasImageExtension =
            trimmed.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
        if (hasImageExtension && !trimmed.Contains('/'))
        {
            return $"/images/aislepilot-meals/{trimmed}";
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        return normalized;
    }

    private static IReadOnlyList<string> SplitBase64IntoChunks(string base64, int chunkLength)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return [];
        }

        var normalizedChunkLength = Math.Max(1, chunkLength);
        var chunks = new List<string>((base64.Length / normalizedChunkLength) + 1);
        for (var offset = 0; offset < base64.Length; offset += normalizedChunkLength)
        {
            var length = Math.Min(normalizedChunkLength, base64.Length - offset);
            chunks.Add(base64.Substring(offset, length));
        }

        return chunks;
    }

}
