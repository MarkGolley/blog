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
        // NOTE: Use consistent case-sensitive path
        var postsPath = Path.Combine(_env.WebRootPath, "Posts"); 

        // Retrieve all blog post files
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
    
    public BlogPost GetPostBySlug(string id)
    {
        // Use same directory structure and consistent casing
        var path = Path.Combine(_env.WebRootPath, "Posts", id + ".html");

        if (!File.Exists(path)) return null;

        return new BlogPost
        {
            Id = id,
            Title = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(id.Replace("_", " ")),
            Content = File.ReadAllText(path),
            DatePosted = File.GetLastWriteTime(path)
        };
    }
    
    private static DateTime ParsePublishedDate(string content)
    {
        const string keyword = "<!-- PublishedDate:";
        var startIndex = content.IndexOf(keyword, StringComparison.Ordinal);
        if (startIndex == -1) return DateTime.MinValue; // Default if no date is found

        startIndex += keyword.Length;
        var endIndex = content.IndexOf("-->", startIndex, StringComparison.Ordinal);
        if (endIndex == -1) return DateTime.MinValue;

        var dateString = content.Substring(startIndex, endIndex - startIndex).Trim();
        return DateTime.TryParse(dateString, out var date) ? date : DateTime.MinValue;
    }
}