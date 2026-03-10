using System.Globalization;
using MyBlog.Models;

public class BlogService
{
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
                     select new BlogPost
                     {
                         Id = id,
                         Title = title,
                         Content = content,
                         DatePosted = published
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

        return new BlogPost
        {
            Id = fileSlug,
            Title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(fileSlug.Replace("_", " ")),
            Content = content,
            DatePosted = published
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
        const string keyword = "<!-- PublishedDate:";
        var startIndex = content.IndexOf(keyword, StringComparison.Ordinal);
        if (startIndex == -1) return DateTime.MinValue;

        startIndex += keyword.Length;
        var endIndex = content.IndexOf("-->", startIndex, StringComparison.Ordinal);
        if (endIndex == -1) return DateTime.MinValue;

        var dateString = content.Substring(startIndex, endIndex - startIndex).Trim();
        return DateTime.TryParse(dateString, out var date) ? date : DateTime.MinValue;
    }
}
