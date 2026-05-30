using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    static AislePilotService()
    {
        AislePilotTelemetry.ConfigureQueueDepthObserver(() =>
        [
            new Measurement<long>(
                MealImageGenerationInFlight.Count,
                new KeyValuePair<string, object?>("queue", "meal_image_generation")),
            new Measurement<long>(
                AiMealPersistenceInFlight.Count,
                new KeyValuePair<string, object?>("queue", "ai_meal_persistence")),
            new Measurement<long>(
                SpecialTreatGenerationInFlight.Count,
                new KeyValuePair<string, object?>("queue", "special_treat_generation")),
            new Measurement<long>(
                DessertAddOnRecoveryInFlight.Count,
                new KeyValuePair<string, object?>("queue", "dessert_addon_recovery")),
            new Measurement<long>(
                SupermarketLayoutRefreshInFlight.Count,
                new KeyValuePair<string, object?>("queue", "supermarket_layout_refresh"))
        ]);
    }

    private static (double InputUsdPerMillion, double OutputUsdPerMillion) ResolveModelRates(string? model)
    {
        var normalizedModel = (model ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedModel switch
        {
            "gpt-4.1-mini" => (0.40d, 1.60d),
            "gpt-4.1" => (2.00d, 8.00d),
            "gpt-image-1-mini" => (2.00d, 0d),
            _ => (0.40d, 1.60d)
        };
    }

    private static void TryExtractTokenUsageFromResponse(
        string responseContent,
        out int? promptTokens,
        out int? completionTokens)
    {
        promptTokens = null;
        completionTokens = null;
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return;
        }

        try
        {
            using var payload = JsonDocument.Parse(responseContent);
            if (!payload.RootElement.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (usage.TryGetProperty("prompt_tokens", out var promptTokenElement) &&
                promptTokenElement.TryGetInt32(out var parsedPromptTokens))
            {
                promptTokens = parsedPromptTokens;
            }

            if (usage.TryGetProperty("completion_tokens", out var completionTokenElement) &&
                completionTokenElement.TryGetInt32(out var parsedCompletionTokens))
            {
                completionTokens = parsedCompletionTokens;
            }
        }
        catch (JsonException)
        {
            // Usage parsing should not fail requests.
        }
    }

    private static double? EstimateTokenCostUsd(
        string? model,
        int? promptTokens,
        int? completionTokens)
    {
        if ((!promptTokens.HasValue || promptTokens.Value <= 0) &&
            (!completionTokens.HasValue || completionTokens.Value <= 0))
        {
            return null;
        }

        var rates = ResolveModelRates(model);
        var inputCost = ((double)(promptTokens ?? 0) / 1_000_000d) * rates.InputUsdPerMillion;
        var outputCost = ((double)(completionTokens ?? 0) / 1_000_000d) * rates.OutputUsdPerMillion;
        return Math.Round(inputCost + outputCost, 8, MidpointRounding.AwayFromZero);
    }

    private static int EstimateTokenCountFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        // Approximation only; good enough for coarse cost telemetry when provider usage is unavailable.
        return Math.Max(1, (int)Math.Ceiling(text.Length / 4d));
    }

    private void RecordAislePilotAiRequest(
        string operation,
        string model,
        TimeSpan duration,
        bool success,
        string? responseContent = null,
        string? promptText = null,
        string? errorType = null)
    {
        TryExtractTokenUsageFromResponse(responseContent ?? string.Empty, out var promptTokens, out var completionTokens);
        if (!promptTokens.HasValue || promptTokens.Value <= 0)
        {
            var estimatedPromptTokens = EstimateTokenCountFromText(promptText);
            if (estimatedPromptTokens > 0)
            {
                promptTokens = estimatedPromptTokens;
            }
        }

        var estimatedCostUsd = EstimateTokenCostUsd(model, promptTokens, completionTokens);
        AislePilotTelemetry.RecordAiRequest(
            operation,
            model,
            duration,
            success,
            promptTokens,
            completionTokens,
            estimatedCostUsd,
            errorType);
    }

    private static void RecordAislePilotBackgroundJob(
        string jobName,
        Stopwatch stopwatch,
        bool success,
        Exception? ex = null)
    {
        AislePilotTelemetry.RecordBackgroundJob(
            jobName,
            stopwatch.Elapsed,
            success,
            ex?.GetType().Name);
    }
}
