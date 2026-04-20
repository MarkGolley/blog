namespace MyBlog.Models;

public sealed class HomeIndexViewModel
{
    public int PublishedPostCount { get; init; }
    public IReadOnlyList<BlogPost> FeaturedPosts { get; init; } = Array.Empty<BlogPost>();
}
