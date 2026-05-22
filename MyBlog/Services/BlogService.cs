using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyBlog.Models;

public class BlogService
{
    private static readonly Regex HtmlBodyRegex = new(
        "<body[^>]*>(?<body>.*)</body>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex StyleOrScriptRegex = new(
        "<(script|style)[^>]*>.*?</\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"[A-Za-z0-9#\+]+", RegexOptions.Compiled);
    private static readonly Regex CollapseWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex FirstImageSrcRegex = new(
        "<img[^>]*\\ssrc\\s*=\\s*[\"'](?<src>[^\"']+)[\"'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);
    private static readonly string[] FeaturedPostIds =
    [
        "orleans_the_mystery_revealed",
        "Balancing_Quality_And_Value_Delivery",
        "early_thoughts_on_agentic_systems"
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
    private readonly bool _isDevelopment;
    private readonly object _snapshotLock = new();
    private BlogSnapshot? _cachedSnapshot;

    public BlogService(IWebHostEnvironment env)
    {
        _env = env;
        _isDevelopment = env.IsDevelopment();
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

    public BlogPostQuiz? GetQuizBySlug(string? slug)
    {
        var decodedSlug = Uri.UnescapeDataString(slug ?? string.Empty);
        var normalizedSlug = NormalizeSlug(decodedSlug);
        if (normalizedSlug == null)
        {
            return null;
        }

        var quizzesPath = Path.Combine(_env.WebRootPath, "BlogStorage", "Quizzes");
        if (!Directory.Exists(quizzesPath))
        {
            return null;
        }

        var quizPath = Path.Combine(quizzesPath, $"{normalizedSlug}.json");
        if (!File.Exists(quizPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(quizPath);
            var payload = JsonSerializer.Deserialize<BlogPostQuizPayload>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (payload?.Questions == null || payload.Questions.Count == 0)
            {
                return null;
            }

            var questions = payload.Questions
                .Select(MapQuizQuestion)
                .Where(question => question != null)
                .Cast<BlogPostQuizQuestion>()
                .ToList();

            if (questions.Count == 0)
            {
                return null;
            }

            return new BlogPostQuiz
            {
                Title = string.IsNullOrWhiteSpace(payload.Title) ? "Quick quiz" : payload.Title.Trim(),
                Questions = questions
            };
        }
        catch
        {
            return null;
        }
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
            else if (_isDevelopment && HasPostStorageChanged(postsPath, _cachedSnapshot.FileWriteTimesUtc))
            {
                // In local development we want HTML edits to appear on the next refresh.
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
                     let content = File.ReadAllText(file)
                     let title = ParseTitle(content, id)
                     let summary = ParseSummary(content)
                     let coverImageUrl = ParseCoverImageUrl(content)
                     let published = ParsePublishedDate(content)
                     let tags = ParseTags(content, title)
                     let readingTimeMinutes = ParseReadingTimeMinutes(content)
                     select CreatePost(
                         fileName,
                         File.GetLastWriteTimeUtc(file),
                         id,
                         title,
                         content,
                         summary,
                         coverImageUrl,
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
        string summary,
        string coverImageUrl,
        DateTime published,
        List<string> tags,
        int readingTimeMinutes)
    {
        var post = new BlogPost
        {
            Id = id,
            Title = title,
            Content = content,
            Summary = summary,
            CoverImageUrl = coverImageUrl,
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

    private static string ParseTitle(string content, string fallbackId)
    {
        var metadataTitle = ParseMetadataValue(content, "Title");
        if (!string.IsNullOrWhiteSpace(metadataTitle))
        {
            return metadataTitle.Trim();
        }

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(fallbackId.Replace("_", " "));
    }

    private static string ParseSummary(string content)
    {
        const int maxSummaryLength = 155;

        var metadataSummary = ParseMetadataValue(content, "Summary");
        if (!string.IsNullOrWhiteSpace(metadataSummary))
        {
            return BuildSummaryText(metadataSummary, maxSummaryLength);
        }

        return BuildSummaryText(ToPlainText(content), maxSummaryLength);
    }

    private static string ParseCoverImageUrl(string content)
    {
        var metadataCoverImage = ParseMetadataValue(content, "CoverImage");
        if (!string.IsNullOrWhiteSpace(metadataCoverImage))
        {
            return NormalizeCoverImageUrl(metadataCoverImage);
        }

        var imageMatch = FirstImageSrcRegex.Match(content);
        if (!imageMatch.Success)
        {
            return string.Empty;
        }

        var candidate = imageMatch.Groups["src"].Value;
        return NormalizeCoverImageUrl(candidate);
    }

    private static string NormalizeCoverImageUrl(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var value = rawValue.Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (value.StartsWith("~/", StringComparison.Ordinal))
        {
            return "/" + value[2..];
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            return value;
        }

        if (value.StartsWith("../wwwroot/", StringComparison.OrdinalIgnoreCase))
        {
            return "/" + value["../wwwroot/".Length..];
        }

        if (value.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
        {
            return "/" + value["wwwroot/".Length..];
        }

        return "/" + value.TrimStart('.', '/');
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
        var content = html;
        var bodyMatch = HtmlBodyRegex.Match(content);
        if (bodyMatch.Success)
        {
            content = bodyMatch.Groups["body"].Value;
        }

        content = StyleOrScriptRegex.Replace(content, " ");
        var withoutTags = HtmlTagRegex.Replace(content, " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string CollapseWhitespace(string value)
    {
        return CollapseWhitespaceRegex.Replace(value, " ").Trim();
    }

    private static string BuildSummaryText(string rawValue, int maxLength)
    {
        var normalized = CollapseWhitespace(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var truncated = normalized[..maxLength];
        var minimumWordBreak = Math.Max(70, maxLength / 2);
        var lastBreak = truncated.LastIndexOf(' ');
        if (lastBreak >= minimumWordBreak)
        {
            truncated = truncated[..lastBreak];
        }

        return $"{truncated.TrimEnd(' ', ',', ';', ':', '.')}...";
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

    private static BlogPostQuizQuestion? MapQuizQuestion(BlogPostQuizQuestionPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Prompt) ||
            payload.Options == null ||
            payload.Options.Count < 2 ||
            payload.CorrectOptionIndex < 0 ||
            payload.CorrectOptionIndex >= payload.Options.Count)
        {
            return null;
        }

        var normalizedOptions = payload.Options
            .Select(option => option?.Trim() ?? string.Empty)
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .ToList();

        if (normalizedOptions.Count != payload.Options.Count)
        {
            return null;
        }

        return new BlogPostQuizQuestion
        {
            Id = string.IsNullOrWhiteSpace(payload.Id) ? Guid.NewGuid().ToString("N") : payload.Id.Trim(),
            Prompt = payload.Prompt.Trim(),
            Options = normalizedOptions,
            CorrectOptionIndex = payload.CorrectOptionIndex,
            Explanation = string.IsNullOrWhiteSpace(payload.Explanation)
                ? "Review the post section related to this question and try again."
                : payload.Explanation.Trim()
        };
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

    private sealed class BlogPostQuizPayload
    {
        public string? Title { get; set; }
        public List<BlogPostQuizQuestionPayload> Questions { get; set; } = new();
    }

    private sealed class BlogPostQuizQuestionPayload
    {
        public string? Id { get; set; }
        public string? Prompt { get; set; }
        public List<string?> Options { get; set; } = new();
        public int CorrectOptionIndex { get; set; }
        public string? Explanation { get; set; }
    }
}
