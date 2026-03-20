using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;

namespace MyBlog.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public class SeoController : Controller
{
    private readonly BlogService _blogService;

    public SeoController(BlogService blogService)
    {
        _blogService = blogService;
    }

    [HttpGet("/sitemap.xml")]
    [Produces("application/xml")]
    public IActionResult Sitemap()
    {
        var baseUrl = GetBaseUrl();
        var posts = _blogService.GetAllPosts().ToList();
        var now = DateTime.UtcNow;

        var urls = new List<XElement>
        {
            BuildSitemapUrlElement($"{baseUrl}/", now, "weekly", "1.0"),
            BuildSitemapUrlElement($"{baseUrl}/blog", now, "daily", "0.9"),
            BuildSitemapUrlElement($"{baseUrl}/projects", now, "weekly", "0.9"),
            BuildSitemapUrlElement($"{baseUrl}/ai-experiments", now, "weekly", "0.9"),
            BuildSitemapUrlElement($"{baseUrl}/about", now, "monthly", "0.7"),
            BuildSitemapUrlElement($"{baseUrl}/learning", now, "weekly", "0.7"),
            BuildSitemapUrlElement($"{baseUrl}/contact", now, "monthly", "0.6")
        };

        urls.AddRange(posts.Select(post =>
            BuildSitemapUrlElement(
                $"{baseUrl}/blog/{Uri.EscapeDataString(post.Id)}",
                post.DatePosted == DateTime.MinValue ? now : post.DatePosted.ToUniversalTime(),
                "monthly",
                "0.8")));

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                XName.Get("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9"),
                urls));

        return Content(document.ToString(SaveOptions.DisableFormatting), "application/xml; charset=utf-8");
    }

    [HttpGet("/rss.xml")]
    [Produces("application/rss+xml")]
    public IActionResult Rss()
    {
        var baseUrl = GetBaseUrl();
        var posts = _blogService.GetAllPosts()
            .Where(p => p.DatePosted != DateTime.MinValue)
            .OrderByDescending(p => p.DatePosted)
            .Take(25)
            .ToList();

        var lastBuild = posts.FirstOrDefault()?.DatePosted.ToUniversalTime() ?? DateTime.UtcNow;

        var channel = new XElement("channel",
            new XElement("title", "MyBlog"),
            new XElement("link", $"{baseUrl}/"),
            new XElement("description", "Engineering posts on C#, .NET, Python, testing, and software design."),
            new XElement("language", "en-gb"),
            new XElement("lastBuildDate", lastBuild.ToString("r")),
            new XElement("ttl", "60"),
            posts.Select(BuildRssItem));

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("rss", new XAttribute("version", "2.0"), channel));

        return Content(document.ToString(SaveOptions.DisableFormatting), "application/rss+xml; charset=utf-8");
    }

    [HttpGet("/robots.txt")]
    [Produces("text/plain")]
    public IActionResult Robots()
    {
        var baseUrl = GetBaseUrl();
        var robots = string.Join('\n', new[]
        {
            "User-agent: *",
            "Allow: /",
            $"Sitemap: {baseUrl}/sitemap.xml"
        });

        return Content(robots, "text/plain; charset=utf-8");
    }

    private XElement BuildRssItem(BlogPost post)
    {
        var baseUrl = GetBaseUrl();
        var postUrl = $"{baseUrl}/blog/{Uri.EscapeDataString(post.Id)}";
        var published = post.DatePosted.ToUniversalTime();

        return new XElement("item",
            new XElement("title", post.Title),
            new XElement("link", postUrl),
            new XElement("guid", postUrl),
            new XElement("pubDate", published.ToString("r")),
            new XElement("description", $"Read \"{post.Title}\" on MyBlog."));
    }

    private static XElement BuildSitemapUrlElement(string loc, DateTime lastModUtc, string changeFreq, string priority)
    {
        return new XElement(XName.Get("url", "http://www.sitemaps.org/schemas/sitemap/0.9"),
            new XElement(XName.Get("loc", "http://www.sitemaps.org/schemas/sitemap/0.9"), loc),
            new XElement(XName.Get("lastmod", "http://www.sitemaps.org/schemas/sitemap/0.9"), lastModUtc.ToString("yyyy-MM-dd")),
            new XElement(XName.Get("changefreq", "http://www.sitemaps.org/schemas/sitemap/0.9"), changeFreq),
            new XElement(XName.Get("priority", "http://www.sitemaps.org/schemas/sitemap/0.9"), priority));
    }

    private string GetBaseUrl() => $"{Request.Scheme}://{Request.Host}".TrimEnd('/');
}
