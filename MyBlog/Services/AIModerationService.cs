using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace MyBlog.Services;

public class AIModerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    public AIModerationService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    public async Task<bool> IsCommentSafeAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            // Fallback: approve if no API key
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

            // If any category is flagged, consider unsafe
            return moderationResult?.Results?.FirstOrDefault()?.Flagged != true;
        }
        catch (Exception ex)
        {
            // Log error and fallback to manual review
            Console.WriteLine($"AI moderation failed: {ex.Message}");
            return false; // Err on side of caution
        }
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
