namespace MyBlog.Models;

public class CommentThreadViewModel
{
    public required Comment Comment { get; init; }
    public int Depth { get; init; }
    public int TotalReplyCount { get; init; }
    public string ContentHtml { get; init; } = string.Empty;
    public List<CommentThreadViewModel> Replies { get; init; } = new();
    public int LikeCount { get; set; }
    public bool IsLikedByCurrentVisitor { get; set; }
}
