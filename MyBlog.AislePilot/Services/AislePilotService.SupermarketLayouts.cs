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
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private static readonly HashSet<string> VariableLayoutSupermarkets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aldi",
        "Lidl",
        "Co-op",
        "Iceland",
        "M&S Food"
    };

    private IReadOnlyList<string> ResolveAisleOrder(string supermarket, string customAisleOrder)
    {
        return ResolveSupermarketLayout(supermarket, customAisleOrder).AisleOrder;
    }

    private async Task<IReadOnlyList<string>> ResolveAisleOrderAsync(
        string supermarket,
        string customAisleOrder,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveSupermarketLayoutAsync(supermarket, customAisleOrder, cancellationToken);
        return resolution.AisleOrder;
    }

    private SupermarketLayoutResolution ResolveSupermarketLayout(string supermarket, string customAisleOrder)
    {
        if (supermarket.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            var customResolution = TryBuildCustomSupermarketLayoutResolution(customAisleOrder);
            if (customResolution is not null)
            {
                return customResolution;
            }
        }

        EnsureSupermarketLayoutCacheHydrated();
        if (SupermarketLayoutCache.TryGetValue(supermarket, out var cachedLayout) &&
            cachedLayout.AisleOrder.Count >= 3)
        {
            if (!IsSupermarketLayoutStale(cachedLayout.UpdatedAtUtc))
            {
                return BuildCachedSupermarketLayoutResolution(cachedLayout);
            }

            var refreshedStaleLayout = TryRefreshSupermarketLayoutSynchronously(supermarket);
            if (refreshedStaleLayout is not null)
            {
                return refreshedStaleLayout;
            }

            QueueSupermarketLayoutRefresh(supermarket);
            return BuildCachedSupermarketLayoutResolution(cachedLayout);
        }

        var discoveredLayout = TryRefreshSupermarketLayoutSynchronously(supermarket);
        if (discoveredLayout is not null)
        {
            return discoveredLayout;
        }

        QueueSupermarketLayoutRefresh(supermarket);
        return BuildFallbackSupermarketLayoutResolution(supermarket);
    }

    private async Task<SupermarketLayoutResolution> ResolveSupermarketLayoutAsync(
        string supermarket,
        string customAisleOrder,
        CancellationToken cancellationToken = default)
    {
        if (supermarket.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            var customResolution = TryBuildCustomSupermarketLayoutResolution(customAisleOrder);
            if (customResolution is not null)
            {
                return customResolution;
            }
        }

        await EnsureSupermarketLayoutCacheHydratedAsync(cancellationToken);
        if (SupermarketLayoutCache.TryGetValue(supermarket, out var cachedLayout) &&
            cachedLayout.AisleOrder.Count >= 3)
        {
            if (!IsSupermarketLayoutStale(cachedLayout.UpdatedAtUtc))
            {
                return BuildCachedSupermarketLayoutResolution(cachedLayout);
            }

            var refreshedStaleLayout = await TryRefreshSupermarketLayoutWithTimeoutAsync(supermarket, cancellationToken);
            if (refreshedStaleLayout is not null)
            {
                return refreshedStaleLayout;
            }

            QueueSupermarketLayoutRefresh(supermarket);
            return BuildCachedSupermarketLayoutResolution(cachedLayout);
        }

        var discoveredLayout = await TryRefreshSupermarketLayoutWithTimeoutAsync(supermarket, cancellationToken);
        if (discoveredLayout is not null)
        {
            return discoveredLayout;
        }

        QueueSupermarketLayoutRefresh(supermarket);
        return BuildFallbackSupermarketLayoutResolution(supermarket);
    }

    private static SupermarketLayoutResolution? TryBuildCustomSupermarketLayoutResolution(string customAisleOrder)
    {
        var custom = customAisleOrder
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (custom.Count < 3)
        {
            return null;
        }

        return new SupermarketLayoutResolution
        {
            AisleOrder = NormalizeResolvedAisleOrder(custom),
            SourceLabel = "User set custom layout",
            ConfidenceScore = 1m,
            ConfidenceLabel = "User set",
            IsDefaultLayout = false,
            NeedsReview = false,
            LastVerifiedUtc = null
        };
    }

    private static SupermarketLayoutResolution BuildFallbackSupermarketLayoutResolution(string supermarket)
    {
        if (SupermarketAisleOrders.TryGetValue(supermarket, out var predefined))
        {
            return BuildCuratedSupermarketLayoutResolution(supermarket, predefined);
        }

        return BuildCuratedSupermarketLayoutResolution(supermarket, DefaultAisleOrder);
    }

    private static SupermarketLayoutResolution BuildCuratedSupermarketLayoutResolution(
        string supermarket,
        IEnumerable<string> aisleOrder)
    {
        var normalizedSupermarket = NormalizeSupermarket(supermarket);
        var needsReview = VariableLayoutSupermarkets.Contains(normalizedSupermarket);
        var confidenceScore = needsReview ? 0.55m : 0.68m;

        return new SupermarketLayoutResolution
        {
            AisleOrder = NormalizeResolvedAisleOrder(aisleOrder),
            SourceLabel = "Curated chain default",
            ConfidenceScore = confidenceScore,
            ConfidenceLabel = ResolveConfidenceLabel(confidenceScore),
            IsDefaultLayout = true,
            NeedsReview = needsReview,
            LastVerifiedUtc = null
        };
    }

    private static SupermarketLayoutResolution BuildCachedSupermarketLayoutResolution(
        SupermarketLayoutCacheEntry cachedLayout)
    {
        return new SupermarketLayoutResolution
        {
            AisleOrder = cachedLayout.AisleOrder,
            SourceLabel = cachedLayout.SourceLabel,
            ConfidenceScore = cachedLayout.ConfidenceScore,
            ConfidenceLabel = cachedLayout.ConfidenceLabel,
            IsDefaultLayout = cachedLayout.IsDefaultLayout,
            NeedsReview = cachedLayout.NeedsReview,
            LastVerifiedUtc = cachedLayout.UpdatedAtUtc == default ? null : cachedLayout.UpdatedAtUtc,
            Evidence = cachedLayout.Evidence
        };
    }

    internal static AislePilotSupermarketLayoutInsightViewModel BuildLayoutInsightViewModel(
        SupermarketLayoutResolution resolution)
    {
        return new AislePilotSupermarketLayoutInsightViewModel
        {
            SourceLabel = resolution.SourceLabel,
            ConfidenceScore = resolution.ConfidenceScore,
            ConfidenceLabel = resolution.ConfidenceLabel,
            IsDefaultLayout = resolution.IsDefaultLayout,
            NeedsReview = resolution.NeedsReview,
            LastVerifiedUtc = resolution.LastVerifiedUtc,
            Evidence = resolution.Evidence
                .Select(source => new AislePilotSupermarketLayoutEvidenceViewModel
                {
                    Title = source.Title,
                    Url = source.Url,
                    SourceType = source.SourceType
                })
                .ToList()
        };
    }

    private static SupermarketLayoutCacheEntry BuildCacheEntry(
        SupermarketLayoutResolution resolution,
        DateTime updatedAtUtc)
    {
        return new SupermarketLayoutCacheEntry
        {
            AisleOrder = resolution.AisleOrder,
            UpdatedAtUtc = updatedAtUtc,
            SourceLabel = resolution.SourceLabel,
            ConfidenceScore = resolution.ConfidenceScore,
            ConfidenceLabel = resolution.ConfidenceLabel,
            IsDefaultLayout = resolution.IsDefaultLayout,
            NeedsReview = resolution.NeedsReview,
            Evidence = resolution.Evidence
        };
    }

    private void EnsureSupermarketLayoutCacheHydrated()
    {
        try
        {
            EnsureSupermarketLayoutCacheHydratedAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to hydrate supermarket layout cache from Firestore.");
        }
    }

    private async Task EnsureSupermarketLayoutCacheHydratedAsync(CancellationToken cancellationToken = default)
    {
        if (_db is null)
        {
            return;
        }

        var shouldRefresh =
            SupermarketLayoutCache.IsEmpty ||
            !_lastSupermarketLayoutCacheRefreshUtc.HasValue ||
            DateTime.UtcNow - _lastSupermarketLayoutCacheRefreshUtc.Value >
            TimeSpan.FromMinutes(SupermarketLayoutRefreshCooldownMinutes);
        if (!shouldRefresh)
        {
            return;
        }

        await SupermarketLayoutRefreshLock.WaitAsync(cancellationToken);
        try
        {
            shouldRefresh =
                SupermarketLayoutCache.IsEmpty ||
                !_lastSupermarketLayoutCacheRefreshUtc.HasValue ||
                DateTime.UtcNow - _lastSupermarketLayoutCacheRefreshUtc.Value >
                TimeSpan.FromMinutes(SupermarketLayoutRefreshCooldownMinutes);
            if (!shouldRefresh)
            {
                return;
            }

            var snapshot = await _db.Collection(SupermarketLayoutsCollection).GetSnapshotAsync(cancellationToken);
            foreach (var doc in snapshot.Documents)
            {
                if (!doc.Exists)
                {
                    continue;
                }

                FirestoreAislePilotSupermarketLayout? mapped;
                try
                {
                    mapped = doc.ConvertTo<FirestoreAislePilotSupermarketLayout>();
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mapped.Supermarket))
                {
                    continue;
                }

                if (mapped.Version != SupermarketLayoutCacheVersion)
                {
                    continue;
                }

                var normalizedOrder = NormalizeResolvedAisleOrder(mapped.AisleOrder ?? []);
                if (normalizedOrder.Count < 3)
                {
                    continue;
                }

                var evidence = NormalizeSupermarketLayoutEvidence(mapped.Evidence?
                    .Select(source => new SupermarketLayoutEvidence
                    {
                        Title = source.Title,
                        Url = source.Url,
                        SourceType = source.SourceType
                    }) ?? []);
                var confidenceScore = Math.Clamp((decimal)mapped.ConfidenceScore, 0m, 1m);
                var updatedAtUtc = mapped.UpdatedAtUtc == default
                    ? DateTime.UtcNow.AddDays(-SupermarketLayoutStaleDays - 1)
                    : mapped.UpdatedAtUtc;

                SupermarketLayoutCache[mapped.Supermarket.Trim()] = new SupermarketLayoutCacheEntry
                {
                    AisleOrder = normalizedOrder,
                    UpdatedAtUtc = updatedAtUtc,
                    SourceLabel = string.IsNullOrWhiteSpace(mapped.Source)
                        ? "AI web research"
                        : mapped.Source.Trim(),
                    ConfidenceScore = confidenceScore,
                    ConfidenceLabel = ResolveConfidenceLabel(confidenceScore, mapped.ConfidenceLabel),
                    IsDefaultLayout = mapped.IsDefaultLayout,
                    NeedsReview = mapped.NeedsReview,
                    Evidence = evidence
                };
            }

            _lastSupermarketLayoutCacheRefreshUtc = DateTime.UtcNow;
        }
        finally
        {
            SupermarketLayoutRefreshLock.Release();
        }
    }

    private bool CanUseOnlineLayoutDiscovery()
    {
        return _enableAiGeneration &&
               _httpClient is not null &&
               !string.IsNullOrWhiteSpace(_apiKey);
    }

    private async Task<SupermarketLayoutResolution?> TryRefreshSupermarketLayoutWithTimeoutAsync(
        string supermarket,
        CancellationToken cancellationToken)
    {
        if (!CanUseOnlineLayoutDiscovery())
        {
            return null;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            return await TryRefreshSupermarketLayoutAsync(supermarket, force: false, cts.Token);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Supermarket layout refresh failed for '{Supermarket}'.", supermarket);
            return null;
        }
    }

    private SupermarketLayoutResolution? TryRefreshSupermarketLayoutSynchronously(string supermarket)
    {
        return TryRefreshSupermarketLayoutWithTimeoutAsync(supermarket, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private async Task<SupermarketLayoutResolution?> TryRefreshSupermarketLayoutAsync(
        string supermarket,
        bool force,
        CancellationToken cancellationToken)
    {
        var normalizedSupermarket = NormalizeSupermarket(supermarket);
        if (normalizedSupermarket.Equals("Custom", StringComparison.OrdinalIgnoreCase) || !CanUseOnlineLayoutDiscovery())
        {
            return null;
        }

        if (!force &&
            SupermarketLayoutLastAttemptUtc.TryGetValue(normalizedSupermarket, out var lastAttemptUtc) &&
            DateTime.UtcNow - lastAttemptUtc < TimeSpan.FromMinutes(SupermarketLayoutRefreshCooldownMinutes))
        {
            return SupermarketLayoutCache.TryGetValue(normalizedSupermarket, out var recentCached)
                ? BuildCachedSupermarketLayoutResolution(recentCached)
                : null;
        }

        if (!SupermarketLayoutRefreshInFlight.TryAdd(normalizedSupermarket, 1))
        {
            return SupermarketLayoutCache.TryGetValue(normalizedSupermarket, out var existingCached)
                ? BuildCachedSupermarketLayoutResolution(existingCached)
                : null;
        }

        SupermarketLayoutLastAttemptUtc[normalizedSupermarket] = DateTime.UtcNow;
        try
        {
            var discoveredLayout = await TryDiscoverSupermarketLayoutWithAiAsync(
                normalizedSupermarket,
                cancellationToken);
            if (discoveredLayout is null || discoveredLayout.AisleOrder.Count < 3)
            {
                return null;
            }

            var cacheEntry = BuildCacheEntry(discoveredLayout, DateTime.UtcNow);
            SupermarketLayoutCache[normalizedSupermarket] = cacheEntry;
            await PersistSupermarketLayoutAsync(
                normalizedSupermarket,
                discoveredLayout,
                cancellationToken);
            return discoveredLayout;
        }
        finally
        {
            SupermarketLayoutRefreshInFlight.TryRemove(normalizedSupermarket, out _);
        }
    }

    private void QueueSupermarketLayoutRefresh(string supermarket)
    {
        var normalizedSupermarket = NormalizeSupermarket(supermarket);
        if (normalizedSupermarket.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!CanUseOnlineLayoutDiscovery())
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await TryRefreshSupermarketLayoutAsync(
                    normalizedSupermarket,
                    force: false,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to refresh supermarket layout for '{Supermarket}'.", normalizedSupermarket);
            }
        });
    }

    private async Task<SupermarketLayoutResolution?> TryDiscoverSupermarketLayoutWithAiAsync(
        string supermarket,
        CancellationToken cancellationToken)
    {
        if (_httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var aisleLabels = string.Join(", ", DefaultAisleOrder);
        var inputPrompt =
            "Research the current in-store shopping flow for a UK " + supermarket + " supermarket.\n" +
            "Return JSON only with this exact schema:\n" +
            "{\"aisleOrder\":[\"Produce\",\"Bakery\",\"Meat & Fish\",\"Dairy & Eggs\",\"Frozen\",\"Tins & Dry Goods\",\"Spices & Sauces\",\"Snacks\",\"Drinks\",\"Household\",\"Other\"],\"confidenceScore\":0.0,\"confidenceLabel\":\"low|medium|high\",\"needsReview\":true,\"sources\":[{\"title\":\"\",\"url\":\"https://...\",\"sourceType\":\"official|store-map|article|video|forum\"}]}\n\n" +
            "Rules:\n" +
            "- Use only these aisle labels: " + aisleLabels + ".\n" +
            "- Prefer official UK retailer sources and recent UK-specific store walkthrough evidence.\n" +
            "- Ignore non-UK stores and convenience-store formats unless the chain is mainly convenience-led.\n" +
            "- If you cannot find at least 3 useful sources with URLs, set needsReview=true and confidenceScore to 0.55 or lower.\n" +
            "- If layouts vary a lot by branch or format, set needsReview=true.\n" +
            "- Never guess. If evidence is weak or mixed, still return the best normalized order but mark it for review.";

        var requestBody = new
        {
            model = _model,
            tools = new object[]
            {
                new
                {
                    type = "web_search_preview",
                    search_context_size = "medium"
                }
            },
            input = inputPrompt
        };
        var serializedBody = JsonSerializer.Serialize(requestBody);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(OpenAiRequestTimeout);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, OpenAiResponsesEndpoint)
        {
            Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(requestMessage, timeoutCts.Token);
            var responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorSample = responseContent.Length <= 240 ? responseContent : responseContent[..240];
                _logger?.LogWarning(
                    "Supermarket layout lookup failed for '{Supermarket}' with status {StatusCode}. ResponseSample={ResponseSample}",
                    supermarket,
                    (int)response.StatusCode,
                    errorSample);
                return null;
            }

            return ParseSupermarketLayoutResearchResponse(responseContent, supermarket);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Supermarket layout lookup failed for '{Supermarket}'.", supermarket);
            return null;
        }
    }

    private static SupermarketLayoutResolution? ParseSupermarketLayoutResearchResponse(
        string responseContent,
        string supermarket)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(responseContent);
        if (doc.RootElement.TryGetProperty("output_text", out var outputTextElement) &&
            outputTextElement.ValueKind == JsonValueKind.String)
        {
            var parsedFromOutputText = ParseSupermarketLayoutResearchPayload(outputTextElement.GetString(), supermarket);
            if (parsedFromOutputText is not null)
            {
                return parsedFromOutputText;
            }
        }

        foreach (var textCandidate in EnumerateJsonStringValues(doc.RootElement))
        {
            var parsed = ParseSupermarketLayoutResearchPayload(textCandidate, supermarket);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            var parsed = TryBuildAiResearchLayoutResolution(
                JsonSerializer.Deserialize<SupermarketLayoutResearchPayload>(doc.RootElement.GetRawText(), JsonOptions),
                supermarket);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }

    private static SupermarketLayoutResolution? ParseSupermarketLayoutResearchPayload(
        string? rawPayload,
        string supermarket)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return null;
        }

        var normalized = NormalizeModelJson(rawPayload);
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains("aisleOrder", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<SupermarketLayoutResearchPayload>(normalized, JsonOptions);
            return TryBuildAiResearchLayoutResolution(payload, supermarket);
        }
        catch
        {
            return null;
        }
    }

    private static SupermarketLayoutResolution? TryBuildAiResearchLayoutResolution(
        SupermarketLayoutResearchPayload? payload,
        string supermarket)
    {
        if (payload?.AisleOrder is null)
        {
            return null;
        }

        var rawDistinctOrder = payload.AisleOrder
            .Select(ClampAndNormalizeDepartmentName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (rawDistinctOrder.Count < 5)
        {
            return null;
        }

        var confidenceScore = Math.Clamp(payload.ConfidenceScore ?? 0m, 0m, 1m);
        var evidence = NormalizeSupermarketLayoutEvidence(payload.Sources?
            .Select(source => new SupermarketLayoutEvidence
            {
                Title = source.Title?.Trim() ?? string.Empty,
                Url = source.Url?.Trim() ?? string.Empty,
                SourceType = source.SourceType?.Trim() ?? string.Empty
            }) ?? []);
        var needsReview = payload.NeedsReview ?? true;

        if (!IsAcceptableAiResearchResolution(confidenceScore, needsReview, evidence))
        {
            return null;
        }

        return new SupermarketLayoutResolution
        {
            AisleOrder = NormalizeResolvedAisleOrder(rawDistinctOrder),
            SourceLabel = "AI web research",
            ConfidenceScore = confidenceScore,
            ConfidenceLabel = ResolveConfidenceLabel(confidenceScore, payload.ConfidenceLabel),
            IsDefaultLayout = false,
            NeedsReview = false,
            LastVerifiedUtc = DateTime.UtcNow,
            Evidence = evidence
        };
    }

    private static bool IsAcceptableAiResearchResolution(
        decimal confidenceScore,
        bool needsReview,
        IReadOnlyList<SupermarketLayoutEvidence> evidence)
    {
        if (needsReview || confidenceScore < 0.72m || evidence.Count < 3)
        {
            return false;
        }

        var distinctDomains = evidence
            .Select(GetEvidenceDomain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (distinctDomains < 2)
        {
            return false;
        }

        return evidence.Any(source =>
            source.SourceType.Equals("official", StringComparison.OrdinalIgnoreCase) ||
            source.SourceType.Equals("store-map", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<SupermarketLayoutEvidence> NormalizeSupermarketLayoutEvidence(
        IEnumerable<SupermarketLayoutEvidence> evidence)
    {
        return evidence
            .Where(source => !string.IsNullOrWhiteSpace(source.Url))
            .Select(source =>
            {
                var normalizedUrl = NormalizeEvidenceUrl(source.Url);
                return string.IsNullOrWhiteSpace(normalizedUrl)
                    ? null
                    : new SupermarketLayoutEvidence
                    {
                        Title = string.IsNullOrWhiteSpace(source.Title) ? normalizedUrl : source.Title.Trim(),
                        Url = normalizedUrl,
                        SourceType = NormalizeEvidenceSourceType(source.SourceType)
                    };
            })
            .Where(source => source is not null)
            .Select(source => source!)
            .DistinctBy(source => source.Url, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    private static string NormalizeEvidenceUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        return uri.ToString();
    }

    private static string NormalizeEvidenceSourceType(string? sourceType)
    {
        var normalized = (sourceType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "official" => "official",
            "store-map" => "store-map",
            "article" => "article",
            "video" => "video",
            "forum" => "forum",
            _ => "article"
        };
    }

    private static string? GetEvidenceDomain(SupermarketLayoutEvidence source)
    {
        return Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;
    }

    private static IEnumerable<string> EnumerateJsonStringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }

                break;
            }
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var value in EnumerateJsonStringValues(property.Value))
                    {
                        yield return value;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var value in EnumerateJsonStringValues(item))
                    {
                        yield return value;
                    }
                }

                break;
        }
    }

    private async Task PersistSupermarketLayoutAsync(
        string supermarket,
        SupermarketLayoutResolution resolution,
        CancellationToken cancellationToken)
    {
        if (_db is null || string.IsNullOrWhiteSpace(supermarket) || resolution.AisleOrder.Count < 3)
        {
            return;
        }

        try
        {
            var docRef = _db.Collection(SupermarketLayoutsCollection).Document(ToAiMealDocumentId(supermarket));
            await docRef.SetAsync(
                new FirestoreAislePilotSupermarketLayout
                {
                    Supermarket = supermarket,
                    AisleOrder = resolution.AisleOrder.ToList(),
                    Version = SupermarketLayoutCacheVersion,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Source = resolution.SourceLabel,
                    ConfidenceScore = (double)resolution.ConfidenceScore,
                    ConfidenceLabel = resolution.ConfidenceLabel,
                    NeedsReview = resolution.NeedsReview,
                    IsDefaultLayout = resolution.IsDefaultLayout,
                    Evidence = resolution.Evidence
                        .Select(source => new FirestoreAislePilotSupermarketLayoutEvidence
                        {
                            Title = source.Title,
                            Url = source.Url,
                            SourceType = source.SourceType
                        })
                        .ToList()
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to persist supermarket layout cache for '{Supermarket}'.", supermarket);
        }
    }

    private static bool IsSupermarketLayoutStale(DateTime updatedAtUtc)
    {
        if (updatedAtUtc == default)
        {
            return true;
        }

        return DateTime.UtcNow - updatedAtUtc > TimeSpan.FromDays(SupermarketLayoutStaleDays);
    }

    private static string ResolveConfidenceLabel(decimal confidenceScore, string? preferredLabel = null)
    {
        var normalizedPreferred = (preferredLabel ?? string.Empty).Trim();
        if (normalizedPreferred.Equals("user set", StringComparison.OrdinalIgnoreCase))
        {
            return "User set";
        }

        if (normalizedPreferred.Equals("high", StringComparison.OrdinalIgnoreCase) ||
            normalizedPreferred.Equals("medium", StringComparison.OrdinalIgnoreCase) ||
            normalizedPreferred.Equals("low", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalizedPreferred.ToLowerInvariant());
        }

        return confidenceScore switch
        {
            >= 0.8m => "High",
            >= 0.6m => "Medium",
            _ => "Low"
        };
    }

    private static IReadOnlyList<string> NormalizeResolvedAisleOrder(IEnumerable<string> source)
    {
        var normalized = source
            .Select(ClampAndNormalizeDepartmentName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var department in DefaultAisleOrder)
        {
            if (!normalized.Contains(department, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(department);
            }
        }

        return normalized;
    }
}
