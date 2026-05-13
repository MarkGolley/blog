using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyBlog.Services;

namespace MyBlog.Tests;

public class DailyCodingCapsuleServiceTests
{
    [Fact]
    public async Task GetCapsuleForOffsetDaysAsync_OpenAiRateLimitedWithRetryAfter_UsesFallbackWithoutRetrying()
    {
        var config = BuildConfig();
        using var handler = new SequencedCapsuleResponseHandler(
            CreateRateLimitedResponse(TimeSpan.FromMinutes(2)),
            CreateSuccessResponse());
        using var httpClient = new HttpClient(handler);
        var service = new DailyCodingCapsuleService(
            httpClient,
            config,
            NullLogger<DailyCodingCapsuleService>.Instance);

        var capsule = await service.GetCapsuleForOffsetDaysAsync(-7);

        Assert.Equal("fallback", capsule.Source);
        Assert.Equal(1, handler.RequestCount);
    }

    private static IConfiguration BuildConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key",
                ["DailyCapsule:EnableAiGeneration"] = "true",
                ["DailyCapsule:EnableHistoryFallback"] = "false"
            })
            .Build();
    }

    private static HttpResponseMessage CreateRateLimitedResponse(TimeSpan retryAfter)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Rate limit\"}}", Encoding.UTF8, "application/json")
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
        return response;
    }

    private static HttpResponseMessage CreateSuccessResponse()
    {
        const string payload =
            "{\"choices\":[{\"message\":{\"content\":\"{\\\"capsuleType\\\":\\\"Tip\\\",\\\"title\\\":\\\"Use deterministic retries\\\",\\\"body\\\":\\\"Back off with retry windows to protect downstream services.\\\",\\\"example\\\":\\\"await Task.Delay(TimeSpan.FromSeconds(2), ct);\\\"}\"}}]}";

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }

    private sealed class SequencedCapsuleResponseHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("{\"error\":\"No response configured\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
