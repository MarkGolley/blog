using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyBlog.Services;

namespace MyBlog.Tests;

public class AIModerationServiceLiveTests
{
    [Fact]
    public async Task IsCommentSafeAsync_LiveOpenAiCall_UsesConfiguredApiKey()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assert.False(
            string.IsNullOrWhiteSpace(apiKey),
            "OPENAI_API_KEY must be set for live moderation verification tests.");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = apiKey
            })
            .Build();

        using var trackingHandler = new TrackingHttpHandler(new HttpClientHandler());
        using var client = new HttpClient(trackingHandler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var service = new AIModerationService(client, config, NullLogger<AIModerationService>.Instance);

        var isSafe = await service.IsCommentSafeAsync("This is a clean live moderation diagnostics comment.");

        Assert.Equal(1, trackingHandler.RequestCount);
        Assert.Equal(new Uri("https://api.openai.com/v1/moderations"), trackingHandler.LastRequestUri);
        Assert.Equal("Bearer", trackingHandler.LastAuthScheme);
        Assert.Equal(apiKey, trackingHandler.LastAuthParameter);
        Assert.Equal(HttpStatusCode.OK, trackingHandler.LastResponseStatusCode);
        Assert.True(isSafe, "Expected clean input to be approved by moderation.");
    }

    private sealed class TrackingHttpHandler : DelegatingHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastAuthScheme { get; private set; }
        public string? LastAuthParameter { get; private set; }
        public HttpStatusCode? LastResponseStatusCode { get; private set; }

        public TrackingHttpHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            LastAuthScheme = request.Headers.Authorization?.Scheme;
            LastAuthParameter = request.Headers.Authorization?.Parameter;

            var response = await base.SendAsync(request, cancellationToken);
            LastResponseStatusCode = response.StatusCode;
            return response;
        }
    }
}
