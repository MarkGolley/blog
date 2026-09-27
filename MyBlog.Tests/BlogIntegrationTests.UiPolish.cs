using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using MyBlog.Services;

namespace MyBlog.Tests;

public partial class BlogIntegrationTests
{
    [Fact]
    public async Task MainNavigation_ExposesCurrentPageAndAccessibleMobileBehavior()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog");
        var script = await client.GetStringAsync("/js/site.js");
        var css = await client.GetStringAsync("/css/site.css");

        Assert.Contains("aria-current=\"page\" href=\"/blog\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("event.key !== \"Escape\"", script, StringComparison.Ordinal);
        Assert.Contains("navToggle.focus();", script, StringComparison.Ordinal);
        Assert.Contains("html.js .nav-links", css, StringComparison.Ordinal);
        Assert.Contains("width: 2.75rem;", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MainSitePolish_IsScopedToTheMainLayoutAndExcludedFromAislePilot()
    {
        using var client = _factory.CreateClient();

        var homeHtml = await client.GetStringAsync("/");
        var aislePilotHtml = await client.GetStringAsync("/projects/aisle-pilot");
        var polishCss = await client.GetStringAsync("/css/main-site-polish.css");

        Assert.Contains("class=\"main-site route-home route-home-index\"", homeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/css/main-site-polish.css", homeHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/css/main-site-polish.css", aislePilotHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=\"main-site", aislePilotHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("body.main-site", polishCss, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_PrioritizesFeaturedWritingImmediatelyAfterHero()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var featuredIndex = html.IndexOf("id=\"featured-writing-title\"", StringComparison.Ordinal);
        var workingStyleIndex = html.IndexOf("id=\"quick-bits-title\"", StringComparison.Ordinal);

        Assert.True(featuredIndex > 0);
        Assert.True(workingStyleIndex > featuredIndex);
        Assert.Contains("Reliable software, explained through real delivery.", html, StringComparison.Ordinal);
        Assert.Contains("/images/me-480.jpg 480w", html, StringComparison.Ordinal);
        Assert.Contains("/images/me-768.jpg 768w", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LikeControls_ExposeStateActionAndVisibleCountInTheirAccessibleName()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/blog/Why_AI_Permission_Popups_Matter");

        Assert.Matches("aria-label=\"(?:Like|Unlike) this post\\. \\d+ likes?\"\\s+aria-pressed=\"(?:true|false)\"", html);
        Assert.Contains("class=\"like-symbol\" aria-hidden=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Projects_UsesSequentialCaseStudyHeadings()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/projects");

        Assert.Contains("<h2 class=\"case-template-title\">", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<h3 class=\"case-template-title\">", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EveryBlogPost_RendersOneAuthoritativePageHeadingAndContentFragment()
    {
        using var client = _factory.CreateClient();
        var posts = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().ToList();

        foreach (var post in posts)
        {
            var html = await client.GetStringAsync($"/blog/{Uri.EscapeDataString(post.Id)}");
            var headings = Regex.Matches(html, "<h1\\b[^>]*>(?<title>.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

            Assert.Single(headings);
            Assert.Equal(post.Title, WebUtility.HtmlDecode(headings[0].Groups["title"].Value));
            Assert.DoesNotContain("<!DOCTYPE html>", post.Content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<html", post.Content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<body", post.Content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BlogService_UsesOnlyExplicitCoverMetadata()
    {
        var posts = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().ToList();
        var explicitCover = posts.Single(post => post.Id.Equals("How_AI_Agents_Actually_Work", StringComparison.OrdinalIgnoreCase));
        var piHole = posts.Single(post => post.Id.Equals("Setting_Up_A_Pi_Hole", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("/images/blog/ai-agent-workflow-cover.svg", explicitCover.CoverImageUrl);
        Assert.Empty(piHole.CoverImageUrl);
    }

    [Fact]
    public void BlogService_PreservesTechnicalAcronymsInGeneratedTitlesAndTags()
    {
        var posts = _factory.Services.GetRequiredService<BlogService>().GetAllPosts().ToList();

        Assert.Contains(posts, post => post.Title.Contains("xUnit", StringComparison.Ordinal));
        Assert.Contains(posts, post => post.Title.Contains("ASP.NET Core", StringComparison.Ordinal));
        Assert.DoesNotContain(posts, post => post.Title.Contains(" Aspnetcore", StringComparison.Ordinal));
        Assert.DoesNotContain(posts.SelectMany(post => post.Tags), tag => tag is "Ux" or "Agentic Ai");
    }

    [Fact]
    public async Task BlogRoutes_DoNotRequestHiddenNavigationCapsules()
    {
        var provider = new FakeDailyCodingCapsuleProvider(includeStoredYesterday: true);
        using var factory = CreateFactoryWithCapsuleProvider(provider);
        using var client = factory.CreateClient();

        await client.GetStringAsync("/blog");
        await client.GetStringAsync("/blog/Why_AI_Permission_Popups_Matter");

        Assert.Equal(0, provider.RequestCount);
    }
}
