using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MyBlog.Models;

public class BlogService
{
    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"[A-Za-z0-9#\+]+", RegexOptions.Compiled);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private static readonly string[] FeaturedPostIds =
    [
        "the_16_hour_ai_moderation_build",
        "Using_xUnit_For_Testing",
        "Migrating_To_GCP"
    ];

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
    private readonly object _snapshotLock = new();
    private BlogSnapshot? _cachedSnapshot;

    public BlogService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public IEnumerable<BlogPost> GetAllPosts()
    {
        return GetOrBuildSnapshot().Posts;
    }

    public IReadOnlyList<BlogPost> GetFeaturedPosts(int maxCount = 3)
    {
        if (maxCount <= 0)
        {
            return Array.Empty<BlogPost>();
        }

        var snapshot = GetOrBuildSnapshot();
        if (snapshot.Posts.Count == 0)
        {
            return Array.Empty<BlogPost>();
        }

        var featuredPosts = new List<BlogPost>(Math.Min(maxCount, snapshot.Posts.Count));
        var featuredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var postId in FeaturedPostIds)
        {
            if (!snapshot.PostBySlug.TryGetValue(postId, out var post) || !featuredIds.Add(post.Id))
            {
                continue;
            }

            featuredPosts.Add(post);
            if (featuredPosts.Count == maxCount)
            {
                return featuredPosts;
            }
        }

        foreach (var post in snapshot.Posts)
        {
            if (!featuredIds.Add(post.Id))
            {
                continue;
            }

            featuredPosts.Add(post);
            if (featuredPosts.Count == maxCount)
            {
                break;
            }
        }

        return featuredPosts;
    }

    public BlogPost? GetPostBySlug(string? slug)
    {
        var decodedSlug = Uri.UnescapeDataString(slug ?? string.Empty);
        var normalizedSlug = NormalizeSlug(decodedSlug);
        if (normalizedSlug == null)
        {
            return null;
        }

        var snapshot = GetOrBuildSnapshot();
        return snapshot.PostBySlug.GetValueOrDefault(normalizedSlug);
    }

    private BlogSnapshot GetOrBuildSnapshot()
    {
        var postsPath = Path.Combine(_env.WebRootPath, "BlogStorage");
        if (!Directory.Exists(postsPath))
        {
            return BlogSnapshot.Empty;
        }

        var nowUtc = DateTime.UtcNow;
        lock (_snapshotLock)
        {
            if (_cachedSnapshot is null)
            {
                // Build outside lock.
            }
            else if (nowUtc - _cachedSnapshot.BuiltAtUtc < CacheTtl)
            {
                return _cachedSnapshot;
            }
            else if (!HasPostStorageChanged(postsPath, _cachedSnapshot.FileWriteTimesUtc))
            {
                // Content is unchanged, so refresh cache age without reparsing file contents.
                _cachedSnapshot = _cachedSnapshot with { BuiltAtUtc = nowUtc };
                return _cachedSnapshot;
            }
        }

        var rebuiltSnapshot = BuildSnapshot(postsPath, nowUtc);
        lock (_snapshotLock)
        {
            _cachedSnapshot = rebuiltSnapshot;
            return rebuiltSnapshot;
        }
    }

    private static bool HasPostStorageChanged(string postsPath, IReadOnlyDictionary<string, DateTime> knownWriteTimesUtc)
    {
        var currentFiles = Directory.EnumerateFiles(postsPath, "*.html").ToList();
        if (currentFiles.Count != knownWriteTimesUtc.Count)
        {
            return true;
        }

        foreach (var file in currentFiles)
        {
            var fileName = Path.GetFileName(file);
            var writeTimeUtc = File.GetLastWriteTimeUtc(file);
            if (!knownWriteTimesUtc.TryGetValue(fileName, out var knownWriteTimeUtc) ||
                writeTimeUtc != knownWriteTimeUtc)
            {
                return true;
            }
        }

        return false;
    }

    private static BlogSnapshot BuildSnapshot(string postsPath, DateTime builtAtUtc)
    {
        var fileWriteTimesUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var posts = (from file in Directory.GetFiles(postsPath, "*.html")
                     let fileName = Path.GetFileName(file)
                     let id = Path.GetFileNameWithoutExtension(file)
                     let title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(id.Replace("_", " "))
                     let content = File.ReadAllText(file)
                     let published = ParsePublishedDate(content)
                     let tags = ParseTags(content, title)
                     let readingTimeMinutes = ParseReadingTimeMinutes(content)
                     select CreatePost(
                         fileName,
                         File.GetLastWriteTimeUtc(file),
                         id,
                         title,
                         content,
                         published,
                         tags,
                         readingTimeMinutes)).ToList();

        var orderedPosts = posts
            .Select(tuple =>
            {
                fileWriteTimesUtc[tuple.FileName] = tuple.LastWriteTimeUtc;
                return tuple.Post;
            })
            .OrderByDescending(post => post.DatePosted)
            .ToList();

        var postBySlug = orderedPosts
            .ToDictionary(post => post.Id, post => post, StringComparer.OrdinalIgnoreCase);

        return new BlogSnapshot(orderedPosts, postBySlug, fileWriteTimesUtc, builtAtUtc);
    }

    private static (string FileName, DateTime LastWriteTimeUtc, BlogPost Post) CreatePost(
        string fileName,
        DateTime lastWriteTimeUtc,
        string id,
        string title,
        string content,
        DateTime published,
        List<string> tags,
        int readingTimeMinutes)
    {
        var post = new BlogPost
        {
            Id = id,
            Title = title,
            Content = content,
            DatePosted = published,
            Tags = tags,
            ReadingTimeMinutes = readingTimeMinutes
        };

        return (fileName, lastWriteTimeUtc, post);
    }

    private static string? NormalizeSlug(string rawSlug)
    {
        if (string.IsNullOrWhiteSpace(rawSlug))
        {
            return null;
        }

        var slug = rawSlug.Trim();
        if (slug.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            slug = slug[..^5];
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        if (slug.Contains("..", StringComparison.Ordinal) ||
            slug.Contains('/') ||
            slug.Contains('\\'))
        {
            return null;
        }

        return slug.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? null
            : slug;
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

    private sealed record BlogSnapshot(
        IReadOnlyList<BlogPost> Posts,
        IReadOnlyDictionary<string, BlogPost> PostBySlug,
        IReadOnlyDictionary<string, DateTime> FileWriteTimesUtc,
        DateTime BuiltAtUtc)
    {
        public static BlogSnapshot Empty { get; } = new(
            [],
            new Dictionary<string, BlogPost>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase),
            DateTime.MinValue);
    }
}
