using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Services;

namespace MyBlog.Tests;

public class BlogIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly Regex AntiForgeryTokenRegexPrimary =
        new(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AntiForgeryTokenRegexFallback =
        new(@"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly TestWebApplicationFactory _factory;

    public BlogIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BlogIndex_ShowsPinnedPostsOrderedByLikeCount()
    {
        var posts = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().Take(3).ToList();
        Assert.True(posts.Count >= 3, "Expected at least three blog posts for pinned-post ordering test.");

        var topPostId = posts[0].Id;
        var middlePostId = posts[1].Id;
        var lowPostId = posts[2].Id;

        using var visitorOne = CreateClient("10.0.0.1");
        using var visitorTwo = CreateClient("10.0.0.2");
        using var visitorThree = CreateClient("10.0.0.3");

        await LikePostAsync(visitorOne, topPostId);
        await LikePostAsync(visitorOne, middlePostId);
        await LikePostAsync(visitorOne, lowPostId);

        await LikePostAsync(visitorTwo, topPostId);
        await LikePostAsync(visitorTwo, middlePostId);

        await LikePostAsync(visitorThree, topPostId);

        var html = await visitorOne.GetStringAsync("/blog");

        var topIndex = html.IndexOf($"id=\"pinned-post-{topPostId}\"", StringComparison.Ordinal);
        var middleIndex = html.IndexOf($"id=\"pinned-post-{middlePostId}\"", StringComparison.Ordinal);
        var lowIndex = html.IndexOf($"id=\"pinned-post-{lowPostId}\"", StringComparison.Ordinal);

        Assert.True(topIndex >= 0, "Top-liked post was not found in the pinned section.");
        Assert.True(middleIndex >= 0, "Second-liked post was not found in the pinned section.");
        Assert.True(lowIndex >= 0, "Third-liked post was not found in the pinned section.");

        Assert.True(topIndex < middleIndex, "Pinned posts are not sorted by like count (top before middle).");
        Assert.True(middleIndex < lowIndex, "Pinned posts are not sorted by like count (middle before low).");
    }

    [Fact]
    public async Task AddComment_EnforcesMaxThreadDepth()
    {
        var postId = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().First().Id;
        using var client = CreateClient("10.0.1.1", allowAutoRedirect: false);

        var parentId = await AddCommentAsync(client, postId, null, "Root User", "Root comment");
        parentId = await AddCommentAsync(client, postId, parentId, "Reply User 1", "Reply 1");
        parentId = await AddCommentAsync(client, postId, parentId, "Reply User 2", "Reply 2");
        parentId = await AddCommentAsync(client, postId, parentId, "Reply User 3", "Reply 3");
        parentId = await AddCommentAsync(client, postId, parentId, "Reply User 4", "Reply 4");

        var tooDeepResponse = await SubmitCommentAsync(client, postId, parentId, "Too Deep User", "Reply beyond max depth");

        Assert.Equal(HttpStatusCode.OK, tooDeepResponse.StatusCode);
        var responseBody = await tooDeepResponse.Content.ReadAsStringAsync();
        Assert.Contains("Reply depth limit reached", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TogglePostLike_TogglesStateForSameVisitor()
    {
        var postId = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().First().Id;
        using var client = CreateClient("10.0.2.1", allowAutoRedirect: false);

        var firstToggle = await ToggleLikeAjaxAsync(client, postId);
        var secondToggle = await ToggleLikeAjaxAsync(client, postId);

        Assert.True(firstToggle.Success);
        Assert.True(firstToggle.IsLiked);
        Assert.True(secondToggle.Success);
        Assert.False(secondToggle.IsLiked);
        Assert.Equal(firstToggle.Count - 1, secondToggle.Count);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOkWithExpectedChecks()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);

        var root = json.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());

        var checks = root.GetProperty("checks");
        Assert.True(checks.TryGetProperty("blogStorage", out _), "Expected blogStorage check in health payload.");
        Assert.True(checks.TryGetProperty("firestore", out _), "Expected firestore check in health payload.");
    }

    [Fact]
    public async Task BlogPost_PathTraversalSlug_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/blog/%2e%2e%5c%2e%2e%5cappsettings");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_Post_RedirectsToRequestedPath()
    {
        using var client = CreateClient("10.0.3.1", allowAutoRedirect: false);
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/blog");
        var email = $"subscribe-{Guid.NewGuid():N}@example.com";

        using var response = await client.PostAsync("/subscribe", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["ReturnPath"] = "/blog",
            ["__hp"] = string.Empty,
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/blog", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Subscription_CanConfirmThenUnsubscribe()
    {
        var email = $"confirm-{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
        var subscribeResult = await service.SubscribeAsync(email);

        Assert.Equal(SubscribeStatus.PendingConfirmation, subscribeResult.Status);
        Assert.False(string.IsNullOrWhiteSpace(subscribeResult.ConfirmationToken));

        using var client = _factory.CreateClient();

        using var confirmResponse = await client.GetAsync($"/subscribe/confirm?token={Uri.EscapeDataString(subscribeResult.ConfirmationToken!)}");
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmHtml = await confirmResponse.Content.ReadAsStringAsync();
        Assert.Contains("Subscription Confirmed", confirmHtml, StringComparison.OrdinalIgnoreCase);

        var subscriber = (await service.GetConfirmedSubscribersAsync())
            .FirstOrDefault(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(subscriber);
        Assert.False(string.IsNullOrWhiteSpace(subscriber!.UnsubscribeToken));

        using var unsubscribeResponse = await client.GetAsync($"/subscribe/unsubscribe?token={Uri.EscapeDataString(subscriber.UnsubscribeToken)}");
        Assert.Equal(HttpStatusCode.OK, unsubscribeResponse.StatusCode);
        var unsubscribeHtml = await unsubscribeResponse.Content.ReadAsStringAsync();
        Assert.Contains("Unsubscribed", unsubscribeHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotifyPost_RequiresAdminKey()
    {
        var email = $"notify-{Guid.NewGuid():N}@example.com";
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<SubscriptionService>();
        var postId = scope.ServiceProvider.GetRequiredService<BlogService>().GetAllPosts().First().Id;

        var subscribeResult = await service.SubscribeAsync(email);
        await service.ConfirmAsync(subscribeResult.ConfirmationToken ?? string.Empty);

        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var effectiveAdminKey = Environment.GetEnvironmentVariable("SUBSCRIBER_NOTIFY_KEY")
                                ?? "integration-notify-key";

        using var unauthorized = await client.PostAsync("/admin/subscribers/notify-post", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PostSlug"] = postId
        }));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Post, "/admin/subscribers/notify-post")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["PostSlug"] = postId
            })
        };
        authorizedRequest.Headers.Add("X-Admin-Key", effectiveAdminKey);

        using var authorized = await client.SendAsync(authorizedRequest);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

        await using var stream = await authorized.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(postId, root.GetProperty("postId").GetString());
    }

    private HttpClient CreateClient(string forwardedForIp, bool allowAutoRedirect = true)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
            HandleCookies = true
        });

        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedForIp);
        return client;
    }

    private static async Task LikePostAsync(HttpClient client, string postId)
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/blog");

        var response = await client.PostAsync("/Blog/TogglePostLike", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["postId"] = postId,
            ["returnTo"] = "index",
            ["returnAnchor"] = $"post-{postId}",
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Found,
            $"Expected success or redirect when liking post {postId}, got {(int)response.StatusCode}.");
    }

    private static async Task<int> AddCommentAsync(HttpClient client, string postId, int? parentCommentId, string author, string content)
    {
        var response = await SubmitCommentAsync(client, postId, parentCommentId, author, content);
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return ParseCommentIdFromRedirect(response.Headers.Location);
    }

    private static async Task<HttpResponseMessage> SubmitCommentAsync(
        HttpClient client,
        string postId,
        int? parentCommentId,
        string author,
        string content)
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, $"/blog/{Uri.EscapeDataString(postId)}");

        var formValues = new Dictionary<string, string>
        {
            ["PostId"] = postId,
            ["Author"] = author,
            ["Content"] = content,
            ["ParentCommentId"] = parentCommentId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["__hp"] = string.Empty,
            ["__RequestVerificationToken"] = antiForgeryToken
        };

        return await client.PostAsync("/Blog/AddComment", new FormUrlEncodedContent(formValues));
    }

    private static int ParseCommentIdFromRedirect(Uri? location)
    {
        Assert.NotNull(location);
        var match = Regex.Match(location!.OriginalString, @"#comment-(\d+)");
        Assert.True(match.Success, $"Redirect location did not include comment anchor. Location: {location}");
        return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    private static async Task<LikePayload> ToggleLikeAjaxAsync(HttpClient client, string postId)
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/blog");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Blog/TogglePostLike")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["postId"] = postId,
                ["__RequestVerificationToken"] = antiForgeryToken
            })
        };
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        return new LikePayload(
            root.GetProperty("success").GetBoolean(),
            root.GetProperty("count").GetInt32(),
            root.GetProperty("isLiked").GetBoolean());
    }

    private sealed record LikePayload(bool Success, int Count, bool IsLiked);

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = AntiForgeryTokenRegexPrimary.Match(html);
        if (!match.Success)
        {
            match = AntiForgeryTokenRegexFallback.Match(html);
        }

        Assert.True(match.Success, $"Anti-forgery token was not found in response for '{path}'.");
        return match.Groups[1].Value;
    }
}
