using System.ComponentModel.DataAnnotations;

namespace MyBlog.Models;

public class Comment
{
    public int Id { get; set; }
    [Required]
    [StringLength(100)]
    public string Author { get; set; } = string.Empty;
    [Required]
    [StringLength(2000)]
    public string Content { get; set; } = string.Empty;
    public DateTime PostedAt { get; set; }
    [Required]
    public string PostId { get; set; } = string.Empty;
    public int? ParentCommentId { get; set; }
    public bool IsApproved { get; set; } = false;

    public List<CommentLike> Likes { get; set; } = new();
}