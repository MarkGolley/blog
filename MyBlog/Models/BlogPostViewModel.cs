namespace MyBlog.Models;

public class BlogPostViewModel
{
    public BlogPost Post { get; set; }
    public List<Comment> Comments { get; set; }
}