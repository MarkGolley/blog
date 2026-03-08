namespace MyBlog.Models;

public class BlogPostViewModel
{
    public BlogPost Post { get; set; } = null!;
    public List<CommentThreadViewModel> Comments { get; set; } = new();
}
