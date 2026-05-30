using System.Text.Json;
using System.Net.Http.Headers;
using System.Net;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBlog.Models;

namespace MyBlog.Services;

public class AIModerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private const int MaxModerationAttempts = 2;

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<AIModerationService> _logger;

    public AIModerationService(HttpClient httpClient, IConfiguration configuration, ILogger<AIModerationService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _logger = logger;
    }

    public async Task<ModerationEvaluationResult> EvaluateCommentAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ModerationEvaluationResult
            {
                Decision = ModerationDecision.Allow,
                ReasonCode = "empty_input"
            };
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("OPENAI_API_KEY is missing. Comment will require manual review.");
            return new ModerationEvaluationResult
            {
                Decision = ModerationDecision.ManualReview,
                ReasonCode = "missing_api_key"
            };
        }

        try
        {
            for (var attempt = 1; attempt <= MaxModerationAttempts; attempt++)
            {
                using var activity = MyBlogTelemetry.StartActivity(
                    "ai.moderation.evaluate",
                    ActivityKind.Client);
                activity?.SetTag("ai.provider", "openai");
                activity?.SetTag("ai.operation", "moderation");
                activity?.SetTag("ai.model", "omni-moderation-latest");
                activity?.SetTag("ai.attempt", attempt);
                var requestStopwatch = Stopwatch.StartNew();

                var requestBody = new
                {
                    model = "omni-moderation-latest",
                    input = content
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/moderations")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(requestBody),
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    MyBlogTelemetry.RecordAiRequest(
                        provider: "openai",
                        operation: "moderation",
                        model: "omni-moderation-latest",
                        duration: requestStopwatch.Elapsed,
                        success: false,
                        errorType: $"http_{(int)response.StatusCode}");
                    if (attempt < MaxModerationAttempts && IsTransientStatus(response.StatusCode))
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt), cancellationToken);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                }

                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var moderationResult = JsonSerializer.Deserialize<OpenAIModerationResponse>(responseContent, JsonOptions);
                var firstResult = moderationResult?.Results?.FirstOrDefault();
                if (firstResult?.Flagged is not bool flagged)
                {
                    MyBlogTelemetry.RecordAiRequest(
                        provider: "openai",
                        operation: "moderation",
                        model: "omni-moderation-latest",
                        duration: requestStopwatch.Elapsed,
                        success: false,
                        errorType: "invalid_payload");
                    _logger.LogWarning(
                        "OpenAI moderation response did not include a usable flagged decision. Comment will require manual review.");
                    return new ModerationEvaluationResult
                    {
                        Decision = ModerationDecision.ManualReview,
                        ReasonCode = "invalid_response_payload"
                    };
                }

                var requestId = response.Headers.TryGetValues("x-request-id", out var values)
                    ? values.FirstOrDefault()
                    : null;
                TryExtractTokenUsage(responseContent, out var promptTokens, out var completionTokens);

                MyBlogTelemetry.RecordAiRequest(
                    provider: "openai",
                    operation: "moderation",
                    model: "omni-moderation-latest",
                    duration: requestStopwatch.Elapsed,
                    success: true,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    estimatedCostUsd: 0d);
                activity?.SetTag("ai.request_id", requestId ?? string.Empty);
                activity?.SetTag("ai.success", true);
                activity?.SetTag("ai.prompt_tokens", promptTokens ?? 0);
                activity?.SetTag("ai.completion_tokens", completionTokens ?? 0);

                _logger.LogInformation(
                    "OpenAI moderation completed. Flagged={Flagged}. OpenAIRequestId={OpenAIRequestId}",
                    flagged,
                    requestId ?? "n/a");

                return new ModerationEvaluationResult
                {
                    Decision = flagged ? ModerationDecision.Block : ModerationDecision.Allow,
                    ReasonCode = flagged ? "openai_flagged" : "openai_clear",
                    FlaggedByModel = flagged,
                    OpenAiRequestId = requestId
                };
            }

            return new ModerationEvaluationResult
            {
                Decision = ModerationDecision.ManualReview,
                ReasonCode = "request_failed"
            };
        }
        catch (Exception ex)
        {
            MyBlogTelemetry.RecordAiRequest(
                provider: "openai",
                operation: "moderation",
                model: "omni-moderation-latest",
                duration: TimeSpan.Zero,
                success: false,
                errorType: ex.GetType().Name);
            _logger.LogError(ex, "AI moderation request failed. Comment will require manual review.");
            return new ModerationEvaluationResult
            {
                Decision = ModerationDecision.ManualReview,
                ReasonCode = "request_failed"
            };
        }
    }

    public async Task<bool> IsCommentSafeAsync(string content)
    {
        var result = await EvaluateCommentAsync(content);
        return result.IsSafe;
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests ||
               statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout ||
               (int)statusCode >= 500;
    }

    private static void TryExtractTokenUsage(
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
            // Ignore usage parsing failures and proceed without usage metrics.
        }
    }

    private class OpenAIModerationResponse
    {
        public List<ModerationResult>? Results { get; set; }
    }

    private class ModerationResult
    {
        public bool? Flagged { get; set; }
    }
}
