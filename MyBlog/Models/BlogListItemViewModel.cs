namespace MyBlog.Models;

public class BlogListItemViewModel
{
    public required BlogPost Post { get; init; }
    public int LikeCount { get; init; }
    public bool IsLikedByCurrentVisitor { get; init; }
}
