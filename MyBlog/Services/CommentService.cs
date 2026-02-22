using Microsoft.EntityFrameworkCore;
using MyBlog.Data;
using MyBlog.Models;

namespace MyBlog.Services;

public class CommentService
{
    private readonly BlogDbContext _db;

    public CommentService(BlogDbContext db)
    {
        _db = db;
    }

    public async Task AddCommentAsync(Comment comment)
    {
        comment.PostedAt = DateTime.UtcNow;
        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();
    }

    public async Task<List<Comment>> GetCommentsAsync(string postId)
    {
        if (string.IsNullOrWhiteSpace(postId))
            return new List<Comment>();

        return await _db.Comments
            .Where(c => c.PostId == postId)
            .OrderBy(c => c.PostedAt)
            .ToListAsync();
    }
}