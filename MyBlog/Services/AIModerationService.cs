using System.Text.Json;
using System.Net.Http.Headers;
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

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly ILogger<AIModerationService> _logger;

    public AIModerationService(HttpClient httpClient, IConfiguration configuration, ILogger<AIModerationService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _logger = logger;
    }

    public async Task<ModerationEvaluationResult> EvaluateCommentAsync(string content)
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
            var requestBody = new
            {
                model = "omni-moderation-latest",
                input = content
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/moderations")
            {
                Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var moderationResult = JsonSerializer.Deserialize<OpenAIModerationResponse>(responseContent, JsonOptions);
            var firstResult = moderationResult?.Results?.FirstOrDefault();
            if (firstResult?.Flagged is not bool flagged)
            {
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
        catch (Exception ex)
        {
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

    private class OpenAIModerationResponse
    {
        public List<ModerationResult>? Results { get; set; }
    }

    private class ModerationResult
    {
        public bool? Flagged { get; set; }
    }
}
