namespace MyBlog.Models;

public class BlogPostViewModel
{
    public BlogPost Post { get; set; } = null!;
    public List<CommentThreadViewModel> Comments { get; set; } = new();
    public List<BlogPost> RelatedPosts { get; set; } = new();
    public int PostLikeCount { get; set; }
    public bool IsPostLikedByCurrentVisitor { get; set; }
}
