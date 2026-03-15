using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyBlog.Services;

namespace MyBlog.Tests;

public class AIModerationServiceTests
{
    [Fact]
    public async Task IsCommentSafeAsync_NoApiKey_Profanity_IsRejected()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = string.Empty
            })
            .Build();

        using var client = new HttpClient(new ThrowIfCalledHandler());
        var service = new AIModerationService(client, config, NullLogger<AIModerationService>.Instance);

        var isSafe = await service.IsCommentSafeAsync("This is fucking awful.");

        Assert.False(isSafe);
    }

    [Fact]
    public async Task IsCommentSafeAsync_NoApiKey_CleanComment_IsAllowed()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OPENAI_API_KEY"] = string.Empty
            })
            .Build();

        using var client = new HttpClient(new ThrowIfCalledHandler());
        var service = new AIModerationService(client, config, NullLogger<AIModerationService>.Instance);

        var isSafe = await service.IsCommentSafeAsync("Thanks for sharing this post.");

        Assert.True(isSafe);
    }

    private sealed class ThrowIfCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("External moderation API should not be called in this test.");
        }
    }
}
