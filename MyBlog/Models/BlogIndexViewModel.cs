namespace MyBlog.Models;

public class BlogIndexViewModel
{
    public List<BlogListItemViewModel> FeaturedPosts { get; set; } = new();
    public List<BlogListItemViewModel> Posts { get; set; } = new();
    public List<string> AvailableTags { get; set; } = new();
    public string SearchQuery { get; set; } = string.Empty;
    public string SelectedTag { get; set; } = string.Empty;
    public int TotalPostCount { get; set; }
    public int FilteredPostCount { get; set; }
}
