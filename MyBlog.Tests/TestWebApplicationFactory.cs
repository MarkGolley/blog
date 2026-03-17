using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MyBlog.Tests;

public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Subscriptions:NotifyAdminKey"] = "integration-notify-key",
                ["DailyCapsule:EnableAiGeneration"] = "false",
                ["DailyCapsule:WarmupAdminKey"] = "integration-daily-capsule-key"
            });
        });

        // Set OPENAI_API_KEY for moderation tests
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            builder.UseSetting("OPENAI_API_KEY", apiKey);
        }
    }
}
