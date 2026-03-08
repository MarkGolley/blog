namespace MyBlog.Models;

public class PostLike
{
    public int Id { get; set; }
    public string PostId { get; set; } = string.Empty;
    public string VisitorId { get; set; } = string.Empty;
    public DateTime LikedAt { get; set; }
}
