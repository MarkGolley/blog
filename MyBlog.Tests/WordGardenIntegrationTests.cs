using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace MyBlog.Tests;

public class WordGardenIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    public WordGardenIntegrationTests(TestWebApplicationFactory factory) => _factory = factory;

    [Theory]
    [InlineData("Combined")]
    [InlineData("BlogOnly")]
    public async Task WordGarden_IsAvailableWithAssetsAndDiscoveryLinks(string mode)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:Mode"] = mode })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });
        var html = await client.GetStringAsync("/projects/word-garden");
        Assert.Contains("WordGarden", html);
        Assert.Contains("id=\"wg-review\"", html);
        Assert.Contains("id=\"wg-match\"", html);
        Assert.Contains("id=\"wg-library\"", html);
        foreach (var path in new[] { "/css/word-garden.css", "/js/word-garden.js", "/js/word-garden-core.mjs" })
        {
            var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();
            Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.ToString() ?? "");
        }
        Assert.Contains("/projects/word-garden", await client.GetStringAsync("/projects"));
        Assert.Contains("/projects/word-garden", await client.GetStringAsync("/sitemap.xml"));
    }
}
