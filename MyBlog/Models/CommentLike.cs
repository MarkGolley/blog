namespace MyBlog.Models;

public class CommentLike
{
    public int Id { get; set; }
    public int CommentId { get; set; }
    public string VisitorId { get; set; } = string.Empty;
    public DateTime LikedAt { get; set; }

    public Comment Comment { get; set; } = null!;
}
