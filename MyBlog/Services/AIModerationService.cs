using System.Text.Json;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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

    private static readonly Regex LocalProfanityRegex = new(
        @"\b(fuck|fucking|shit|bitch|asshole|bastard|cunt|motherfucker|dickhead|wanker)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LocalAbuseRegex = new(
        @"\b(kill yourself|kys|go die)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AIModerationService(HttpClient httpClient, IConfiguration configuration, ILogger<AIModerationService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _logger = logger;
    }

    public async Task<bool> IsCommentSafeAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        if (IsBlockedByLocalRules(content))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("OPENAI_API_KEY is missing. Falling back to local moderation rules only.");
            return true;
        }

        try
        {
            var requestBody = new
            {
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
            var flagged = moderationResult?.Results?.FirstOrDefault()?.Flagged == true;
            var requestId = response.Headers.TryGetValues("x-request-id", out var values)
                ? values.FirstOrDefault()
                : null;

            _logger.LogInformation(
                "OpenAI moderation completed. Flagged={Flagged}. OpenAIRequestId={OpenAIRequestId}",
                flagged,
                requestId ?? "n/a");

            // If any category is flagged, consider unsafe
            return !flagged;
        }
        catch (Exception ex)
        {
            // Log error and fallback to manual review
            _logger.LogError(ex, "AI moderation request failed. Comment will require manual review.");
            return false; // Err on side of caution
        }
    }

    private static bool IsBlockedByLocalRules(string content)
    {
        return LocalProfanityRegex.IsMatch(content) || LocalAbuseRegex.IsMatch(content);
    }

    private class OpenAIModerationResponse
    {
        public List<ModerationResult>? Results { get; set; }
    }

    private class ModerationResult
    {
        public bool Flagged { get; set; }
    }
}
