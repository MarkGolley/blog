using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MyBlog.Models;

public class BlogService
{
    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"[A-Za-z0-9#\+]+", RegexOptions.Compiled);

    private static readonly (string Keyword, string Tag)[] TagKeywords =
    {
        ("c#", "C#"),
        ("dotnet", ".NET"),
        (".net", ".NET"),
        ("asp.net", "ASP.NET"),
        ("xunit", "Testing"),
        ("test", "Testing"),
        ("design pattern", "Design Patterns"),
        ("solid", "OOP"),
        ("object-oriented", "OOP"),
        ("oop", "OOP"),
        ("database", "Databases"),
        ("sql", "Databases"),
        ("firestore", "Cloud"),
        ("gcp", "Cloud"),
        ("google cloud", "Cloud"),
        ("network", "Networking"),
        ("tcp", "Networking"),
        ("udp", "Networking"),
        ("ai", "AI"),
        ("agentic", "AI"),
        ("semantic kernel", "AI")
    };

    private readonly IWebHostEnvironment _env;

    public BlogService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public IEnumerable<BlogPost> GetAllPosts()
    {
        var postsPath = Path.Combine(_env.WebRootPath, "BlogStorage"); 

        var posts = (from file in Directory.GetFiles(postsPath, "*.html")
                     let id = Path.GetFileNameWithoutExtension(file)
                     let title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(id.Replace("_", " "))
                     let content = File.ReadAllText(file)
                     let published = ParsePublishedDate(content)
                     let tags = ParseTags(content, title)
                     let readingTimeMinutes = ParseReadingTimeMinutes(content)
                     select new BlogPost
                     {
                         Id = id,
                         Title = title,
                         Content = content,
                         DatePosted = published,
                         Tags = tags,
                         ReadingTimeMinutes = readingTimeMinutes
                     }).ToList();

        return posts.OrderByDescending(p => p.DatePosted);
    }

    public BlogPost? GetPostBySlug(string? slug)
    {
        slug = Uri.UnescapeDataString(slug ?? string.Empty);
        var path = ResolvePostPath(slug);
        if (path == null) return null;

        var content = File.ReadAllText(path);
        var published = ParsePublishedDate(content);
        var fileSlug = Path.GetFileNameWithoutExtension(path);
        var title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(fileSlug.Replace("_", " "));

        return new BlogPost
        {
            Id = fileSlug,
            Title = title,
            Content = content,
            DatePosted = published,
            Tags = ParseTags(content, title),
            ReadingTimeMinutes = ParseReadingTimeMinutes(content)
        };
    }

    private string? ResolvePostPath(string slug)
    {
        var postsPath = Path.Combine(_env.WebRootPath, "BlogStorage");
        var expectedFileName = slug + ".html";
        var exactPath = Path.Combine(postsPath, expectedFileName);

        if (File.Exists(exactPath))
            return exactPath;

        return Directory
            .EnumerateFiles(postsPath, "*.html")
            .FirstOrDefault(file =>
                string.Equals(
                    Path.GetFileName(file),
                    expectedFileName,
                    StringComparison.OrdinalIgnoreCase));
    }


    private static DateTime ParsePublishedDate(string content)
    {
        var dateString = ParseMetadataValue(content, "PublishedDate");
        if (string.IsNullOrWhiteSpace(dateString))
        {
            return DateTime.MinValue;
        }

        return DateTime.TryParse(dateString, out var date) ? date : DateTime.MinValue;
    }

    private static List<string> ParseTags(string content, string title)
    {
        var explicitTags = ParseMetadataValue(content, "Tags");
        if (!string.IsNullOrWhiteSpace(explicitTags))
        {
            return explicitTags
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeTag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var sourceText = $"{title} {ToPlainText(content)}";
        var inferredTags = TagKeywords
            .Where(x => sourceText.Contains(x.Keyword, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Tag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return inferredTags.Any() ? inferredTags : new List<string> { "General" };
    }

    private static int ParseReadingTimeMinutes(string content)
    {
        var explicitReadingTime = ParseMetadataValue(content, "ReadingTime");
        if (int.TryParse(explicitReadingTime, out var minutes) && minutes > 0)
        {
            return minutes;
        }

        var plainText = ToPlainText(content);
        var wordCount = WordRegex.Matches(plainText).Count;
        if (wordCount <= 0)
        {
            return 1;
        }

        const double wordsPerMinute = 220d;
        return Math.Max(1, (int)Math.Ceiling(wordCount / wordsPerMinute));
    }

    private static string ParseMetadataValue(string content, string key)
    {
        var keyword = $"<!-- {key}:";
        var startIndex = content.IndexOf(keyword, StringComparison.Ordinal);
        if (startIndex == -1)
        {
            return string.Empty;
        }

        startIndex += keyword.Length;
        var endIndex = content.IndexOf("-->", startIndex, StringComparison.Ordinal);
        if (endIndex == -1)
        {
            return string.Empty;
        }

        return content.Substring(startIndex, endIndex - startIndex).Trim();
    }

    private static string ToPlainText(string html)
    {
        var withoutTags = HtmlTagRegex.Replace(html, " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string NormalizeTag(string tag)
    {
        if (string.Equals(tag, "c#", StringComparison.OrdinalIgnoreCase))
        {
            return "C#";
        }

        if (string.Equals(tag, ".net", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return ".NET";
        }

        if (string.Equals(tag, "asp.net", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag, "aspnet", StringComparison.OrdinalIgnoreCase))
        {
            return "ASP.NET";
        }

        if (string.Equals(tag, "ai", StringComparison.OrdinalIgnoreCase))
        {
            return "AI";
        }

        if (string.Equals(tag, "gcp", StringComparison.OrdinalIgnoreCase))
        {
            return "GCP";
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tag.Trim().ToLowerInvariant());
    }
}
