namespace MyBlog.Models;

public class BlogIndexViewModel
{
    public List<BlogListItemViewModel> PinnedPosts { get; set; } = new();
    public List<BlogListItemViewModel> Posts { get; set; } = new();
}
