namespace MyBlog.Models;

public class BlogPost
{
    public string Id { get; set; }  // slug
    public string Title { get; set; }
    public string Content { get; set; }
    public DateTime DatePosted { get; set; }
}