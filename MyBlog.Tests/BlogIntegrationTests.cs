using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
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
    public async Task BlogIndex_RendersEditorialShellAndPostSummaries()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog");

        Assert.Contains("class=\"route-blog route-blog-index\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"blog-post-content-shell\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"blog-post-summary\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HomeIndex_DoesNotReferenceMissingScopedStylesheetAsset()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("/css/site.css", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/MyBlog.styles.css", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhyAiPermissionPopupsPost_RendersByronLinkedInAndPodcastLinks()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog/Why_AI_Permission_Popups_Matter");

        Assert.Contains("https://www.linkedin.com/in/byron-cook-8765205", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://softwareengineeringdaily.com/2026/05/19/formal-methods-as-agent-guardrails/", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhyAiPermissionPopupsPost_RendersArchitectureAndChecklistSections()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog/Why_AI_Permission_Popups_Matter");

        Assert.Contains("Agent -> Tool call -> Policy engine -> Approval gate -> Executor -> Audit log", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Common failure modes", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("I stopped running the agent with full access by default.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Next boundaries I am working toward", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("I can answer what ran, which path it touched, who approved it, and when it happened", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Where these policies actually live", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Policy files in source control", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A central policy service", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The runtime enforcement point", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("I do not have a full policy-test pipeline in my current setup yet.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Where developers can get involved", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("day-to-day work seems to sit mostly in the first two layers.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Control flow when some parts are missing", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tighten what you do control and fail safe on risky actions", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhyAiPermissionPopupsPost_UsesMetadataTitleForListingAndPostHeader()
    {
        using var client = _factory.CreateClient();

        var blogIndexHtml = await client.GetStringAsync("/blog");
        Assert.Contains("Agent Guardrails in Practice: From Permission Popups to Policy Verification", blogIndexHtml, StringComparison.OrdinalIgnoreCase);

        var postHtml = await client.GetStringAsync("/blog/Why_AI_Permission_Popups_Matter");
        Assert.Contains("<h2 id=\"post-title\">Agent Guardrails in Practice: From Permission Popups to Policy Verification</h2>", postHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhyAiPermissionPopupsPost_RendersInteractiveQuizSection()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog/Why_AI_Permission_Popups_Matter");

        Assert.Contains("data-post-quiz", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-post-quiz-collapsible", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-capsule post-capsule-details post-quiz-details", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-post-quiz-form", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-quiz-check", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-quiz-reset", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-quiz-score", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Optional quiz", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("what is a permission popup primarily described as?", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhyAiPermissionPopupsPost_RendersJargonSectionWithOptionalExamples()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog/Why_AI_Permission_Popups_Matter");

        Assert.Contains("I am not a formal methods expert.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The model can generate text freely", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The agent should not act freely", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jargon cheat sheet", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("map to the architecture path above", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the runtime system checks, constrains, and proves those actions are safe", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agent runtime, tool execution layer, policy engine, approval gate, executor, and audit log", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the agent plans, calls tools, executes steps", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Formal methods", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Optional: Policy examples", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Examples at prompt, rule, and verification layers.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prompt-level rule (weakest)", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/workspace/project", html, StringComparison.Ordinal);
        Assert.Contains("/workspace/project/policies/agent-policy.rego", html, StringComparison.Ordinal);
        Assert.Contains("/workspace/project/policies/tests/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users\\markg\\RiderProjects\\blog", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Delete commands can only target files inside the approved workspace.", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("these checks often start as policy tests (locally or in CI)", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostWithoutQuizSidecar_DoesNotRenderQuizSection()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog/Deploying_My_First_Blog_Site");

        Assert.DoesNotContain("data-post-quiz", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostRendering_NormalizesLegacyWwwrootStylesheetPath()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog/Deploying_My_First_Blog_Site");

        Assert.DoesNotContain("../wwwroot/css/blogs.css", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/wwwroot/css/blogs.css", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"/css/blogs.css\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SiteCss_ContainsBlogDarkContrastAndExcerptClampRules()
    {
        using var client = _factory.CreateClient();

        var css = await client.GetStringAsync("/css/site.css");

        Assert.Contains(":root[data-theme=\"dark\"] body.route-blog .blog-hero", css, StringComparison.Ordinal);
        Assert.Contains("background: rgba(14, 23, 37, 0.95);", css, StringComparison.Ordinal);
        Assert.Contains("body.route-blog .site-header", css, StringComparison.Ordinal);
        Assert.Contains("-webkit-line-clamp: 2;", css, StringComparison.Ordinal);
        Assert.Contains("-webkit-line-clamp: 3;", css, StringComparison.Ordinal);
        Assert.Contains(".post-content pre {", css, StringComparison.Ordinal);
        Assert.Contains("-webkit-overflow-scrolling: touch;", css, StringComparison.Ordinal);
        Assert.Contains("-webkit-text-size-adjust: 100%;", css, StringComparison.Ordinal);
        Assert.Contains("text-size-adjust: 100%;", css, StringComparison.Ordinal);
        Assert.Contains(".post-content :not(pre) > code {", css, StringComparison.Ordinal);
        Assert.Contains(":root[data-theme=\"dark\"] .post-content pre {", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlogIndex_SubscribeSection_RendersTrustCopyAndSubscribeButtonClass()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog");

        Assert.Contains("class=\"subscribe-button\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("class=\"subscribe-trust\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No spam. Unsubscribe anytime.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrleansPost_RendersEnhancedInteractiveDemoSkin()
    {
        using var client = _factory.CreateClient();
        var orleansPost = _factory.Services.GetRequiredService<BlogService>()
            .GetAllPosts()
            .FirstOrDefault(post => post.Id.Contains("orleans", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(orleansPost);

        var html = await client.GetStringAsync($"/blog/{Uri.EscapeDataString(orleansPost!.Id)}");

        Assert.Contains("class=\"orleans-demo\"", html, StringComparison.Ordinal);
        Assert.Contains("orleans-button-icon", html, StringComparison.Ordinal);
        Assert.Contains("orleans-sheen", html, StringComparison.Ordinal);
        Assert.Contains("orleans-empty-state", html, StringComparison.Ordinal);
        Assert.Contains(":root[data-theme=\"dark\"] .orleans-demo", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"orleansSpeed\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Playback:", html, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion: reduce", html, StringComparison.Ordinal);
        Assert.Contains("id=\"orleansTryBtn\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrleansPost_RendersMobilePortraitLockAndLandscapeDemoRules()
    {
        using var client = _factory.CreateClient();
        var orleansPost = _factory.Services.GetRequiredService<BlogService>()
            .GetAllPosts()
            .FirstOrDefault(post => post.Id.Contains("orleans", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(orleansPost);

        var html = await client.GetStringAsync($"/blog/{Uri.EscapeDataString(orleansPost!.Id)}");

        Assert.Contains("@media (max-width: 760px)", html, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 760px) and (orientation: landscape)", html, StringComparison.Ordinal);
        Assert.Contains("Rotate to enable the live demo.", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-mobile-rotate-lock", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-demo-runtime", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-demo-runtime {", html, StringComparison.Ordinal);
        Assert.Contains("display: none;", html, StringComparison.Ordinal);
        Assert.Contains("display: block;", html, StringComparison.Ordinal);
        Assert.Contains("body:has(.orleans-demo-collapsible[open]) .site-header", html, StringComparison.Ordinal);
        Assert.Contains("body:has(.orleans-demo-collapsible[open]) .site-header .nav-controls", html, StringComparison.Ordinal);
        Assert.Contains("body:has(.orleans-demo-collapsible[open]) .site-header .nav-capsule", html, StringComparison.Ordinal);
        Assert.Contains("body:has(.orleans-demo-collapsible[open]) .site-header .nav-links", html, StringComparison.Ordinal);
        Assert.Contains("display: none !important;", html, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr));", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-progress {", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-demo-status {", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-demo-intro", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-demo-helper", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-topology-silos {", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-track {", html, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr;", html, StringComparison.Ordinal);
        Assert.Contains("grid-auto-flow: column;", html, StringComparison.Ordinal);
        Assert.Contains("grid-auto-columns: minmax(9rem, 1fr);", html, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto;", html, StringComparison.Ordinal);
        Assert.Contains("scroll-snap-type: x proximity;", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-metric:last-child", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-event-feed {", html, StringComparison.Ordinal);
        Assert.Contains("max-height: 4.7rem;", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-silo-lane-col {", html, StringComparison.Ordinal);
        Assert.Contains("min-height: 7.5rem;", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-silo-lane-details", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-silo-lane-toggle", html, StringComparison.Ordinal);
        Assert.Contains("Grain logs</summary>", html, StringComparison.Ordinal);
        Assert.Contains(".orleans-silo-lane-details > .orleans-silo-lane-grid", html, StringComparison.Ordinal);
        Assert.Contains("display: none !important;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BlogPosts_Slugs_DoNotContainCommas()
    {
        var posts = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().ToList();
        Assert.DoesNotContain(
            posts,
            post => post.Id.Contains(',', StringComparison.Ordinal));
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
    public void BlogService_InDevelopment_RefreshesEditedHtmlWithoutWaitingForCacheTtl()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var blogService = scope.ServiceProvider.GetRequiredService<BlogService>();
        var postsPath = Path.Combine(env.WebRootPath, "BlogStorage");
        Directory.CreateDirectory(postsPath);

        var slug = $"dev-refresh-{Guid.NewGuid():N}";
        var postPath = Path.Combine(postsPath, $"{slug}.html");

        try
        {
            File.WriteAllText(
                postPath,
                """
                <!-- Title: Initial development refresh title -->
                <html><body><p>Initial development refresh body.</p></body></html>
                """);

            var initialPost = blogService.GetPostBySlug(slug);
            Assert.NotNull(initialPost);
            Assert.Equal("Initial development refresh title", initialPost!.Title);

            File.WriteAllText(
                postPath,
                """
                <!-- Title: Updated development refresh title -->
                <html><body><p>Updated development refresh body.</p></body></html>
                """);
            File.SetLastWriteTimeUtc(postPath, DateTime.UtcNow.AddSeconds(2));

            var updatedPost = blogService.GetPostBySlug(slug);
            Assert.NotNull(updatedPost);
            Assert.Equal("Updated development refresh title", updatedPost!.Title);
            Assert.Contains("Updated development refresh body.", updatedPost.Content, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(postPath))
            {
                File.Delete(postPath);
            }
        }
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
    public async Task ProjectsIndex_RendersObservabilityDashboardLink_WhenConfigured()
    {
        var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["Observability:PublicDashboardUrl"] = "https://grafana.example.com/d/myblog/myblog-operational-overview"
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/projects");

        Assert.Contains("View observability dashboard", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://grafana.example.com/d/myblog/myblog-operational-overview", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProjectsIndex_DoesNotRenderObservabilityDashboardLink_WhenUrlIsInvalid()
    {
        var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["Observability:PublicDashboardUrl"] = "grafana-dashboard"
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/projects");

        Assert.DoesNotContain("View observability dashboard", html, StringComparison.OrdinalIgnoreCase);
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
