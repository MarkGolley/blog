using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyBlog.Models;
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
    public async Task BlogIndex_ShowsConfiguredFeaturedPostsAndUpdatedCopy()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog");
        var featuredPosts = _factory.Services.GetRequiredService<BlogService>().GetFeaturedPosts();

        Assert.Contains("<h1 id=\"blogs-title\">Articles</h1>", html, StringComparison.Ordinal);
        Assert.Contains("Featured articles", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Pinned posts", html, StringComparison.OrdinalIgnoreCase);

        foreach (var post in featuredPosts)
        {
            Assert.Contains($"id=\"featured-post-{post.Id}\"", html, StringComparison.Ordinal);
        }
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
    public async Task AddComment_SafeContent_IsApprovedImmediately()
    {
        using var scope = _factory.Services.CreateScope();
        var commentService = scope.ServiceProvider.GetRequiredService<CommentService>();
        var blogService = scope.ServiceProvider.GetRequiredService<BlogService>();
        var postId = blogService.GetAllPosts().First().Id;

        var comment = new Comment
        {
            PostId = postId,
            Author = "Test User",
            Content = "This is a great article, thanks for sharing!"
        };
        await commentService.AddCommentAsync(comment);

        Assert.True(comment.IsApproved, "Safe comment should be approved immediately");
        Assert.NotEqual(0, comment.Id);
    }

    [Fact]
    public async Task AddComment_UnsafeContent_RequiresManualReview()
    {
        using var scope = _factory.Services.CreateScope();
        var commentService = scope.ServiceProvider.GetRequiredService<CommentService>();
        var blogService = scope.ServiceProvider.GetRequiredService<BlogService>();
        var postId = blogService.GetAllPosts().First().Id;

        var comment = new Comment
        {
            PostId = postId,
            Author = "Spammer",
            Content = "kill yourself you deserve to die"
        };
        await commentService.AddCommentAsync(comment);

        Assert.False(comment.IsApproved, "Unsafe content should be flagged and require manual review");
    }

    [Fact]
    public async Task AddComment_UnsafeContent_RedirectsWithModerationStatusAndShowsBanner()
    {
        var postId = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().First().Id;
        using var client = CreateClient("10.0.1.3", allowAutoRedirect: false);

        var response = await SubmitCommentAsync(
            client,
            postId,
            parentCommentId: null,
            author: "Banner Test",
            content: "kill yourself you deserve to die");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Contains("commentStatus=moderated", location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#add-comment-title", location.OriginalString, StringComparison.Ordinal);

        var redirectedPath = location.OriginalString.Split('#')[0];
        var postHtml = await client.GetStringAsync(redirectedPath);

        Assert.Contains(
            "Your comment was not published because it did not meet our moderation standards.",
            postHtml,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddComment_HoneypotTriggered_RedirectsWithBlockedStatusAndShowsBanner()
    {
        var postId = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().First().Id;
        using var client = CreateClient("10.0.1.4", allowAutoRedirect: false);

        var response = await SubmitCommentAsync(
            client,
            postId,
            parentCommentId: null,
            author: "Honeypot Banner Test",
            content: "This should be blocked by honeypot.",
            honeypotValue: "autofilled");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Contains("commentStatus=blocked", location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#add-comment-title", location.OriginalString, StringComparison.Ordinal);

        var redirectedPath = location.OriginalString.Split('#')[0];
        var postHtml = await client.GetStringAsync(redirectedPath);

        Assert.Contains(
            "Your comment could not be submitted. Please try again and make sure auto-fill is off for hidden fields.",
            postHtml,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetComments_NonAdmin_ExcludesUnapprovedComments()
    {
        using var scope = _factory.Services.CreateScope();
        var commentService = scope.ServiceProvider.GetRequiredService<CommentService>();
        var blogService = scope.ServiceProvider.GetRequiredService<BlogService>();
        var postId = blogService.GetAllPosts().First().Id;

        var safeComment = new Comment
        {
            PostId = postId,
            Author = "Good User",
            Content = "Great post!"
        };
        await commentService.AddCommentAsync(safeComment);

        var unsafeComment = new Comment
        {
            PostId = postId,
            Author = "Bad User",
            Content = "kill yourself you deserve to die"
        };
        await commentService.AddCommentAsync(unsafeComment);

        var visibleComments = await commentService.GetCommentsAsync(postId, includeUnapproved: false);

        Assert.Contains(visibleComments, c => c.Comment.Id == safeComment.Id);
        Assert.DoesNotContain(visibleComments, c => c.Comment.Id == unsafeComment.Id);
    }

    [Fact]
    public async Task GetComments_Admin_IncludesUnapprovedComments()
    {
        using var scope = _factory.Services.CreateScope();
        var commentService = scope.ServiceProvider.GetRequiredService<CommentService>();
        var blogService = scope.ServiceProvider.GetRequiredService<BlogService>();
        var postId = blogService.GetAllPosts().First().Id;

        var safeComment = new Comment
        {
            PostId = postId,
            Author = "Good User",
            Content = "Great post!"
        };
        await commentService.AddCommentAsync(safeComment);

        var unsafeComment = new Comment
        {
            PostId = postId,
            Author = "Bad User",
            Content = "kill yourself you deserve to die"
        };
        await commentService.AddCommentAsync(unsafeComment);

        var allComments = await commentService.GetCommentsAsync(postId, includeUnapproved: true);

        Assert.Contains(allComments, c => c.Comment.Id == safeComment.Id);
        Assert.Contains(allComments, c => c.Comment.Id == unsafeComment.Id);
    }

    [Fact]
    public async Task BlogPost_AdminSession_DoesNotRenderPendingComments()
    {
        using var scope = _factory.Services.CreateScope();
        var commentService = scope.ServiceProvider.GetRequiredService<CommentService>();
        var blogService = scope.ServiceProvider.GetRequiredService<BlogService>();
        var postId = blogService.GetAllPosts().First().Id;

        var pendingComment = new Comment
        {
            PostId = postId,
            Author = "Pending Visibility Test",
            Content = "kill yourself you deserve to die"
        };
        await commentService.AddCommentAsync(pendingComment);
        Assert.False(pendingComment.IsApproved, "Expected test comment to require moderation.");

        using var client = CreateClient("10.0.1.2");
        await LoginAsAdminAsync(client);

        var postHtml = await client.GetStringAsync($"/blog/{Uri.EscapeDataString(postId)}");
        Assert.DoesNotContain($"id=\"comment-{pendingComment.Id}\"", postHtml, StringComparison.Ordinal);

        var adminHtml = await client.GetStringAsync("/Admin");
        Assert.Contains("Pending Comments", adminHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pending Visibility Test", adminHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApproveComment_SetsFlagAndMakesVisible()
    {
        using var scope = _factory.Services.CreateScope();
        var commentService = scope.ServiceProvider.GetRequiredService<CommentService>();
        var blogService = scope.ServiceProvider.GetRequiredService<BlogService>();
        var postId = blogService.GetAllPosts().First().Id;

        var flaggedComment = new Comment
        {
            PostId = postId,
            Author = "Flagged User",
            Content = "kill yourself you deserve to die"
        };
        await commentService.AddCommentAsync(flaggedComment);

        Assert.False(flaggedComment.IsApproved);

        await commentService.ApproveCommentAsync(flaggedComment.Id);

        var visibleComments = await commentService.GetCommentsAsync(postId, includeUnapproved: false);
        Assert.Contains(visibleComments, c => c.Comment.Id == flaggedComment.Id && c.Comment.IsApproved);
    }

    [Fact]
    public async Task GetPendingComments_ReturnsOnlyUnapprovedComments()
    {
        using var scope = _factory.Services.CreateScope();
        var commentService = scope.ServiceProvider.GetRequiredService<CommentService>();
        var blogService = scope.ServiceProvider.GetRequiredService<BlogService>();
        var postId = blogService.GetAllPosts().First().Id;

        var approvedComment = new Comment
        {
            PostId = postId,
            Author = "Good User",
            Content = "Great post!"
        };
        await commentService.AddCommentAsync(approvedComment);

        var pendingComment = new Comment
        {
            PostId = postId,
            Author = "Pending User",
            Content = "kill yourself you deserve to die"
        };
        await commentService.AddCommentAsync(pendingComment);

        var pending = (await commentService.GetPendingCommentsAsync()).ToList();

        Assert.Contains(pending, c => c.Id == pendingComment.Id && !c.IsApproved);
        Assert.DoesNotContain(pending, c => c.Id == approvedComment.Id);
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
        Assert.True(checks.TryGetProperty("aislePilotMealCache", out _), "Expected aislePilotMealCache check in health payload.");
        Assert.True(checks.TryGetProperty("dailyCapsule", out _), "Expected dailyCapsule check in health payload.");
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

    [Fact]
    public async Task DailyCapsuleWarmup_RequiresAdminKey()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var unauthorized = await client.PostAsync(
            "/admin/daily-capsule/warmup",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task DailyCapsuleWarmup_WithValidAdminKey_ReturnsSuccessPayload()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/daily-capsule/warmup")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>())
        };
        request.Headers.Add("X-Admin-Key", "integration-daily-capsule-key");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("source").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("nextResetUtc").GetString()));
    }

    [Fact]
    public async Task AdminLogin_CanLoginLogoutAndLoginAgain()
    {
        using var client = CreateClient("10.0.3.2", allowAutoRedirect: false);

        await LoginAsAdminAsync(client);
        await LogoutAsAdminAsync(client);
        await LoginAsAdminAsync(client);

        using var adminResponse = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task AdminIndex_Post_IsMethodNotAllowed()
    {
        using var client = CreateClient("10.0.3.11", allowAutoRedirect: false);
        await LoginAsAdminAsync(client);

        using var response = await client.PostAsync("/Admin", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task DynamicPages_ReturnNoStoreCacheHeaders()
    {
        using var client = CreateClient("10.0.3.9", allowAutoRedirect: false);
        var postId = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().First().Id;

        using var adminLoginResponse = await client.GetAsync("/Admin/Login");
        Assert.True(adminLoginResponse.Headers.CacheControl?.NoStore ?? false);

        using var postResponse = await client.GetAsync($"/blog/{Uri.EscapeDataString(postId)}");
        Assert.True(postResponse.Headers.CacheControl?.NoStore ?? false);
    }

    [Fact]
    public async Task HomeIndex_RendersDailyCapsuleAndCountdown()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Today in code", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-daily-capsule-countdown=\"true\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-next-reset-utc=", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HomeIndex_RendersPortfolioNavigationAndFeaturedWritingWithoutRecruiterLanguage()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<title>Mark Golley | C#/.NET Engineer</title>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-controller=\"Learning\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("View selected work", html, StringComparison.Ordinal);
        Assert.Contains("Read featured writing", html, StringComparison.Ordinal);
        Assert.Contains("Featured writing", html, StringComparison.Ordinal);
        Assert.Contains("Clear portfolio proof and stronger case studies.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Recruiter-ready", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tighter recruiter paths", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectsIndex_RendersSelectedWorkWithoutPlaceholderCards()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/projects");

        Assert.Contains("Selected work", html, StringComparison.Ordinal);
        Assert.Contains("Portfolio &#x2B; Publishing Platform", html, StringComparison.Ordinal);
        Assert.DoesNotContain("TBC", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AboutIndex_RendersConciseSummaryWithoutRecruiterLanguage()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/about");

        Assert.Contains("focused on reliable delivery", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("View selected work", html, StringComparison.Ordinal);
        Assert.Contains("Working style", html, StringComparison.Ordinal);
        Assert.Contains("the concise version is", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("short recruiter version", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HomeIndex_RendersProfileActions_WhenProfileLinksAreConfigured()
    {
        var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["Profile:ResumeUrl"] = "https://example.com/resume.pdf",
            ["Profile:GitHubUrl"] = "https://github.com/example",
            ["Profile:LinkedInUrl"] = "https://www.linkedin.com/in/example/",
            ["AislePilot:PublicBaseUrl"] = "https://aislepilot.example.com"
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Try AislePilot", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-profile-link=\"aislepilot\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://aislepilot.example.com/projects/aisle-pilot", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-profile-link=\"resume\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.com/resume.pdf", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-profile-link=\"github\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://github.com/example", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-profile-link=\"linkedin\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://www.linkedin.com/in/example/", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectsIndex_RendersAislePilotAsLiveBetaWithoutPrototypeLanguage()
    {
        var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["AislePilot:PublicBaseUrl"] = "https://aislepilot.example.com"
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/projects");

        Assert.Contains("Live beta", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Live end-to-end planner with generated meals, swaps, shopping, and exports.", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("In progress", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("End-to-end prototype page", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectsIndex_RendersAislePilotBuildNotesAndSnapshots()
    {
        var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["AislePilot:PublicBaseUrl"] = "https://aislepilot.example.com"
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/projects");

        Assert.Contains("Build notes", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Built as an ongoing product exercise alongside day-to-day engineering work", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the next scaling step would be to keep the web tier stateless", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("case-screenshot-grid", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/images/portfolio/aislepilot-home.png", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/images/portfolio/projects-overview.png", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Planner setup focused on weekly constraints and a low-friction path to generation.", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HomeIndex_NavCapsule_DoesNotShowYesterdayArrow_WhenNoStoredHistory()
    {
        var factory = CreateFactoryWithCapsuleProvider(new FakeDailyCodingCapsuleProvider(includeStoredYesterday: false));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Today in code", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-nav-capsule-prev", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-nav-capsule-next", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HomeIndex_NavCapsule_ShowsYesterdayArrow_WhenStoredHistoryExists()
    {
        var factory = CreateFactoryWithCapsuleProvider(new FakeDailyCodingCapsuleProvider(includeStoredYesterday: true));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("data-nav-capsule-prev", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-nav-capsule-next", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_IncludeAppVersionHeader()
    {
        using var client = CreateClient("10.0.3.10", allowAutoRedirect: false);

        using var response = await client.GetAsync("/admin/login");
        Assert.True(response.Headers.TryGetValues("X-App-Version", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values.FirstOrDefault()));
    }

    [Fact]
    public async Task CommentTokenEndpoint_ReturnsTokenPayload()
    {
        using var client = CreateClient("10.0.3.12", allowAutoRedirect: false);

        using var response = await client.GetAsync("/blog/comment-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        var root = json.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task AdminLogin_MissingAntiForgeryToken_StillAuthenticates()
    {
        using var client = CreateClient("10.0.3.3", allowAutoRedirect: false);

        using var response = await client.PostAsync("/Admin/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "password"
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Pending Comments", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddComment_MissingAntiForgeryToken_RedirectsToPostWithExpiredFormFlag()
    {
        var postId = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().First().Id;
        using var client = CreateClient("10.0.3.4", allowAutoRedirect: false);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/Blog/AddComment")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["PostId"] = postId,
                ["Author"] = "No Token User",
                ["Content"] = "Testing anti-forgery recovery path.",
                ["ParentCommentId"] = string.Empty,
                ["__hp"] = string.Empty
            })
        };
        request.Headers.Referrer = new Uri($"http://localhost/blog/{Uri.EscapeDataString(postId)}");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        Assert.StartsWith($"/blog/{Uri.EscapeDataString(postId)}?form=expired", location, StringComparison.OrdinalIgnoreCase);
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

    private WebApplicationFactory<Program> CreateFactoryWithCapsuleProvider(IDailyCodingCapsuleProvider provider)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDailyCodingCapsuleProvider>();
                services.AddSingleton(provider);
            });
        });
    }

    private WebApplicationFactory<Program> CreateFactoryWithConfiguration(IReadOnlyDictionary<string, string?> configurationValues)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(configurationValues);
            });
        });
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
        string content,
        string? honeypotValue = "")
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, $"/blog/{Uri.EscapeDataString(postId)}");

        var formValues = new Dictionary<string, string>
        {
            ["PostId"] = postId,
            ["Author"] = author,
            ["Content"] = content,
            ["ParentCommentId"] = parentCommentId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["__hp"] = honeypotValue ?? string.Empty,
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

    private static async Task LoginAsAdminAsync(HttpClient client)
    {
        var username = Environment.GetEnvironmentVariable("ADMIN_USERNAME") ?? "admin";
        var password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "password";

        using var response = await client.PostAsync("/Admin/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password
        }));

        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Found,
            $"Expected success or redirect when logging in as admin, got {(int)response.StatusCode}.");
    }

    private static async Task LogoutAsAdminAsync(HttpClient client)
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/Admin");

        using var response = await client.PostAsync("/Admin/Logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken
        }));

        Assert.True(
            response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Found,
            $"Expected success or redirect when logging out admin, got {(int)response.StatusCode}.");
    }

    private sealed class FakeDailyCodingCapsuleProvider : IDailyCodingCapsuleProvider
    {
        private readonly bool _includeStoredYesterday;
        private readonly DailyCodingCapsuleViewModel _today;
        private readonly DailyCodingCapsuleViewModel _yesterday;

        public FakeDailyCodingCapsuleProvider(bool includeStoredYesterday)
        {
            _includeStoredYesterday = includeStoredYesterday;
            _today = new DailyCodingCapsuleViewModel
            {
                CapsuleType = "Tip",
                Title = "Write clear commit messages",
                Body = "Use concise, descriptive commit messages so teammates can scan intent quickly.",
                Example = "feat(auth): validate token expiry in reset flow",
                Source = "test",
                NextResetUtcIso = DateTimeOffset.UtcNow.AddHours(3).ToString("O", CultureInfo.InvariantCulture)
            };
            _yesterday = new DailyCodingCapsuleViewModel
            {
                CapsuleType = "Fact",
                Title = "Latency compounds quickly",
                Body = "Sequential network calls add up quickly; parallelise independent IO.",
                Example = "Task.WhenAll(profileTask, settingsTask, alertsTask)",
                Source = "test",
                NextResetUtcIso = DateTimeOffset.UtcNow.AddHours(3).ToString("O", CultureInfo.InvariantCulture)
            };
        }

        public Task<DailyCodingCapsuleViewModel> GetCapsuleForCurrentDayAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_today);
        }

        public Task<DailyCodingCapsuleViewModel> GetCapsuleForOffsetDaysAsync(int offsetDays, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(offsetDays < 0 ? _yesterday : _today);
        }

        public Task<DailyCodingCapsuleViewModel?> TryGetStoredCapsuleForOffsetDaysAsync(int offsetDays, CancellationToken cancellationToken = default)
        {
            DailyCodingCapsuleViewModel? result =
                offsetDays == -1 && _includeStoredYesterday
                    ? _yesterday
                    : null;
            return Task.FromResult(result);
        }
    }
}
