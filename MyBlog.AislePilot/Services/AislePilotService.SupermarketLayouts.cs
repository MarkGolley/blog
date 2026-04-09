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
    private IReadOnlyList<string> ResolveAisleOrder(string supermarket, string customAisleOrder)
    {
        if (supermarket.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            var custom = customAisleOrder
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (custom.Count >= 3)
            {
                return NormalizeResolvedAisleOrder(custom);
            }
        }

        EnsureSupermarketLayoutCacheHydrated();
        if (SupermarketLayoutCache.TryGetValue(supermarket, out var cachedLayout) &&
            cachedLayout.AisleOrder.Count >= 3)
        {
            if (!IsSupermarketLayoutStale(cachedLayout.UpdatedAtUtc))
            {
                return cachedLayout.AisleOrder;
            }

            var refreshedStaleOrder = TryRefreshSupermarketLayoutSynchronously(supermarket);
            if (refreshedStaleOrder is not null)
            {
                return refreshedStaleOrder;
            }

            QueueSupermarketLayoutRefresh(supermarket);
            return cachedLayout.AisleOrder;
        }

        var discoveredOrder = TryRefreshSupermarketLayoutSynchronously(supermarket);
        if (discoveredOrder is not null)
        {
            return discoveredOrder;
        }

        QueueSupermarketLayoutRefresh(supermarket);

        if (SupermarketAisleOrders.TryGetValue(supermarket, out var predefined))
        {
            return NormalizeResolvedAisleOrder(predefined);
        }

        return NormalizeResolvedAisleOrder(DefaultAisleOrder);
    }

    private async Task<IReadOnlyList<string>> ResolveAisleOrderAsync(
        string supermarket,
        string customAisleOrder,
        CancellationToken cancellationToken = default)
    {
        if (supermarket.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            var custom = customAisleOrder
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (custom.Count >= 3)
            {
                return NormalizeResolvedAisleOrder(custom);
            }
        }

        await EnsureSupermarketLayoutCacheHydratedAsync(cancellationToken);
        if (SupermarketLayoutCache.TryGetValue(supermarket, out var cachedLayout) &&
            cachedLayout.AisleOrder.Count >= 3)
        {
            if (!IsSupermarketLayoutStale(cachedLayout.UpdatedAtUtc))
            {
                return cachedLayout.AisleOrder;
            }

            var refreshedStaleOrder = await TryRefreshSupermarketLayoutWithTimeoutAsync(supermarket, cancellationToken);
            if (refreshedStaleOrder is not null)
            {
                return refreshedStaleOrder;
            }

            QueueSupermarketLayoutRefresh(supermarket);
            return cachedLayout.AisleOrder;
        }

        var discoveredOrder = await TryRefreshSupermarketLayoutWithTimeoutAsync(supermarket, cancellationToken);
        if (discoveredOrder is not null)
        {
            return discoveredOrder;
        }

        QueueSupermarketLayoutRefresh(supermarket);

        if (SupermarketAisleOrders.TryGetValue(supermarket, out var predefined))
        {
            return NormalizeResolvedAisleOrder(predefined);
        }

        return NormalizeResolvedAisleOrder(DefaultAisleOrder);
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

                var updatedAtUtc = mapped.UpdatedAtUtc == default
                    ? DateTime.UtcNow.AddDays(-SupermarketLayoutStaleDays - 1)
                    : mapped.UpdatedAtUtc;
                SupermarketLayoutCache[mapped.Supermarket.Trim()] = new SupermarketLayoutCacheEntry
                {
                    AisleOrder = normalizedOrder,
                    UpdatedAtUtc = updatedAtUtc
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
        return _db is not null &&
               _enableAiGeneration &&
               _httpClient is not null &&
               !string.IsNullOrWhiteSpace(_apiKey);
    }

    private async Task<IReadOnlyList<string>?> TryRefreshSupermarketLayoutWithTimeoutAsync(
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

    private IReadOnlyList<string>? TryRefreshSupermarketLayoutSynchronously(string supermarket)
    {
        return TryRefreshSupermarketLayoutWithTimeoutAsync(supermarket, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private async Task<IReadOnlyList<string>?> TryRefreshSupermarketLayoutAsync(
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
                ? recentCached.AisleOrder
                : null;
        }

        if (!SupermarketLayoutRefreshInFlight.TryAdd(normalizedSupermarket, 1))
        {
            return SupermarketLayoutCache.TryGetValue(normalizedSupermarket, out var existingCached)
                ? existingCached.AisleOrder
                : null;
        }

        SupermarketLayoutLastAttemptUtc[normalizedSupermarket] = DateTime.UtcNow;
        try
        {
            var discoveredOrder = await TryDiscoverSupermarketLayoutWithAiAsync(
                normalizedSupermarket,
                cancellationToken);
            if (discoveredOrder is null || discoveredOrder.Count < 3)
            {
                return null;
            }

            var normalizedOrder = NormalizeResolvedAisleOrder(discoveredOrder);
            SupermarketLayoutCache[normalizedSupermarket] = new SupermarketLayoutCacheEntry
            {
                AisleOrder = normalizedOrder,
                UpdatedAtUtc = DateTime.UtcNow
            };
            await PersistSupermarketLayoutAsync(
                normalizedSupermarket,
                normalizedOrder,
                "openai-web-search",
                cancellationToken);
            return normalizedOrder;
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

    private async Task<IReadOnlyList<string>?> TryDiscoverSupermarketLayoutWithAiAsync(
        string supermarket,
        CancellationToken cancellationToken)
    {
        if (_httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var aisleLabels = string.Join(", ", DefaultAisleOrder);
        var inputPrompt =
            "Find current online information for typical in-store aisle flow in a UK " + supermarket + " supermarket.\n" +
            "Return JSON only with this exact schema:\n" +
            "{\"aisleOrder\":[\"Produce\",\"Bakery\",\"Meat & Fish\",\"Dairy & Eggs\",\"Frozen\",\"Tins & Dry Goods\",\"Spices & Sauces\",\"Snacks\",\"Drinks\",\"Household\",\"Other\"]}\n\n" +
            "Rules:\n" +
            "- Use only these aisle labels: " + aisleLabels + ".\n" +
            "- Order should reflect likely customer walking sequence in a typical UK branch.\n" +
            "- If online sources disagree, choose the most common sequence and still return all labels.";

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

            return ParseSupermarketAisleOrderFromAiResponse(responseContent);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Supermarket layout lookup failed for '{Supermarket}'.", supermarket);
            return null;
        }
    }

    private static IReadOnlyList<string>? ParseSupermarketAisleOrderFromAiResponse(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(responseContent);
        if (doc.RootElement.TryGetProperty("output_text", out var outputTextElement) &&
            outputTextElement.ValueKind == JsonValueKind.String)
        {
            var parsedFromOutputText = ParseSupermarketAisleOrderPayload(outputTextElement.GetString());
            if (parsedFromOutputText is not null)
            {
                return parsedFromOutputText;
            }
        }

        foreach (var textCandidate in EnumerateJsonStringValues(doc.RootElement))
        {
            var parsed = ParseSupermarketAisleOrderPayload(textCandidate);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        if (doc.RootElement.TryGetProperty("aisleOrder", out var directOrderElement) &&
            directOrderElement.ValueKind == JsonValueKind.Array)
        {
            var directOrder = directOrderElement
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
            return directOrder.Count >= 3 ? NormalizeResolvedAisleOrder(directOrder) : null;
        }

        return null;
    }

    private static IReadOnlyList<string>? ParseSupermarketAisleOrderPayload(string? rawPayload)
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
            var payload = JsonSerializer.Deserialize<SupermarketAisleOrderPayload>(normalized, JsonOptions);
            var order = payload?.AisleOrder?
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
            return order is { Count: >= 3 } ? NormalizeResolvedAisleOrder(order) : null;
        }
        catch
        {
            return null;
        }
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
        IReadOnlyList<string> aisleOrder,
        string source,
        CancellationToken cancellationToken)
    {
        if (_db is null || string.IsNullOrWhiteSpace(supermarket) || aisleOrder.Count < 3)
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
                    AisleOrder = aisleOrder.ToList(),
                    Version = SupermarketLayoutCacheVersion,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Source = source
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
