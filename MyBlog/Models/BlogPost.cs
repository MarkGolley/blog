namespace MyBlog.Models;

public class BlogPost
{
    public string Id { get; set; } = string.Empty;  // slug
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime DatePosted { get; set; }
}
