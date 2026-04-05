using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyBlog.Services;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MyBlog.Tests;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        var allowLiveOpenAi = string.Equals(
            Environment.GetEnvironmentVariable("MYBLOG_TESTS_USE_LIVE_OPENAI"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var resolvedOpenAiKey = allowLiveOpenAi && !string.IsNullOrWhiteSpace(apiKey)
            ? apiKey
            : "integration-openai-key";

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscriptions:NotifyAdminKey"] = "integration-notify-key",
                ["DailyCapsule:EnableAiGeneration"] = "false",
                ["DailyCapsule:WarmupAdminKey"] = "integration-daily-capsule-key",
                ["AislePilot:EnableAiGeneration"] = "false",
                ["AislePilot:AllowTemplateFallback"] = "true",
                ["OPENAI_API_KEY"] = resolvedOpenAiKey
            });
        });

        if (!allowLiveOpenAi)
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient<AIModerationService>(sp =>
                {
                    var configuration = sp.GetRequiredService<IConfiguration>();
                    var logger = sp.GetRequiredService<ILogger<AIModerationService>>();
                    var httpClient = new HttpClient(new StubModerationMessageHandler())
                    {
                        Timeout = TimeSpan.FromSeconds(3)
                    };
                    return new AIModerationService(httpClient, configuration, logger);
                });
            });
        }
    }

    private sealed class StubModerationMessageHandler : HttpMessageHandler
    {
        private static readonly string[] UnsafeKeywords =
        [
            "kill yourself",
            "deserve to die",
            "die"
        ];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is null ||
                !request.RequestUri.AbsoluteUri.Contains("/v1/moderations", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var content = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var normalized = content.ToLowerInvariant();
            var isFlagged = UnsafeKeywords.Any(keyword => normalized.Contains(keyword, StringComparison.Ordinal));

            var payload = JsonSerializer.Serialize(new
            {
                results = new[]
                {
                    new { flagged = isFlagged }
                }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
