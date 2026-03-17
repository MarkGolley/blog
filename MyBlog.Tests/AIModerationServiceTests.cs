using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Tests;

public class AIModerationServiceTests
{
    [Fact]
    public async Task EvaluateCommentAsync_NoApiKey_CleanComment_ReturnsManualReview()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = string.Empty
            })
            .Build();

        using var handler = new FakeOpenAiModerationHandler(flagged: false);
        using var client = new HttpClient(handler);
        var service = new AIModerationService(client, config, NullLogger<AIModerationService>.Instance);

        var result = await service.EvaluateCommentAsync("Thanks for sharing this post.");

        Assert.Equal(ModerationDecision.ManualReview, result.Decision);
        Assert.Equal("missing_api_key", result.ReasonCode);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task EvaluateCommentAsync_WithApiKey_Profanity_ReturnsBlock()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key"
            })
            .Build();

        using var handler = new FakeOpenAiModerationHandler(flagged: true);
        using var client = new HttpClient(handler);
        var service = new AIModerationService(client, config, NullLogger<AIModerationService>.Instance);

        var result = await service.EvaluateCommentAsync("shitty post");

        Assert.Equal(ModerationDecision.Block, result.Decision);
        Assert.Equal("openai_flagged", result.ReasonCode);
        Assert.True(result.FlaggedByModel);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(new Uri("https://api.openai.com/v1/moderations"), handler.LastRequestUri);
        Assert.Equal("Bearer", handler.LastAuthScheme);
        Assert.Equal("test-key", handler.LastAuthParameter);
        Assert.Contains("omni-moderation-latest", handler.LastRequestBody);
        Assert.Contains("shitty post", handler.LastRequestBody);
    }

    [Fact]
    public async Task EvaluateCommentAsync_WithApiKey_CleanComment_ReturnsAllow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key"
            })
            .Build();

        using var handler = new FakeOpenAiModerationHandler(flagged: false);
        using var client = new HttpClient(handler);
        var service = new AIModerationService(client, config, NullLogger<AIModerationService>.Instance);

        var result = await service.EvaluateCommentAsync("Thanks for sharing this post.");

        Assert.Equal(ModerationDecision.Allow, result.Decision);
        Assert.Equal("openai_clear", result.ReasonCode);
        Assert.False(result.FlaggedByModel);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EvaluateCommentAsync_WithApiKey_UnexpectedPayload_ReturnsManualReview()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key"
            })
            .Build();

        using var handler = new FakeOpenAiModerationHandler(@"{""results"":[{}]}");
        using var client = new HttpClient(handler);
        var service = new AIModerationService(client, config, NullLogger<AIModerationService>.Instance);

        var result = await service.EvaluateCommentAsync("edge-case payload");

        Assert.Equal(ModerationDecision.ManualReview, result.Decision);
        Assert.Equal("invalid_response_payload", result.ReasonCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EvaluateCommentAsync_WithApiKey_MissingResults_ReturnsManualReview()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key"
            })
            .Build();

        using var handler = new FakeOpenAiModerationHandler(@"{""id"":""modr_test""}");
        using var client = new HttpClient(handler);
        var service = new AIModerationService(client, config, NullLogger<AIModerationService>.Instance);

        var result = await service.EvaluateCommentAsync("another edge-case payload");

        Assert.Equal(ModerationDecision.ManualReview, result.Decision);
        Assert.Equal("invalid_response_payload", result.ReasonCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task EvaluateCommentAsync_EmptyInput_ReturnsAllowWithoutCallingOpenAi()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = "test-key"
            })
            .Build();

        using var handler = new FakeOpenAiModerationHandler(flagged: true);
        using var client = new HttpClient(handler);
        var service = new AIModerationService(client, config, NullLogger<AIModerationService>.Instance);

        var result = await service.EvaluateCommentAsync("  ");

        Assert.Equal(ModerationDecision.Allow, result.Decision);
        Assert.Equal("empty_input", result.ReasonCode);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class FakeOpenAiModerationHandler : HttpMessageHandler
    {
        private readonly string _responseJson;

        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthScheme { get; private set; }
        public string? LastAuthParameter { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        public FakeOpenAiModerationHandler(bool flagged)
            : this($@"{{""results"":[{{""flagged"":{flagged.ToString().ToLowerInvariant()}}}]}}")
        {
        }

        public FakeOpenAiModerationHandler(string responseJson)
        {
            _responseJson = responseJson;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            LastAuthScheme = request.Headers.Authorization?.Scheme;
            LastAuthParameter = request.Headers.Authorization?.Parameter;
            LastRequestBody = await (request.Content?.ReadAsStringAsync(cancellationToken) ?? Task.FromResult(string.Empty));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
