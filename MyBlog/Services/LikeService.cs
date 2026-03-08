using Microsoft.EntityFrameworkCore;
using MyBlog.Data;
using MyBlog.Models;

namespace MyBlog.Services;

public class LikeService
{
    private readonly BlogDbContext _db;

    public LikeService(BlogDbContext db)
    {
        _db = db;
    }

    public async Task<(int Count, bool IsLikedByVisitor)> GetPostLikeSummaryAsync(string postId, string visitorId)
    {
        var count = await _db.PostLikes.CountAsync(x => x.PostId == postId);
        var isLiked = await _db.PostLikes.AnyAsync(x => x.PostId == postId && x.VisitorId == visitorId);
        return (count, isLiked);
    }

    public async Task<Dictionary<string, (int Count, bool IsLikedByVisitor)>> GetPostLikeSummariesAsync(
        IEnumerable<string> postIds,
        string visitorId)
    {
        var ids = postIds.Distinct().ToList();
        if (!ids.Any())
        {
            return new Dictionary<string, (int Count, bool IsLikedByVisitor)>();
        }

        var summaries = await _db.PostLikes
            .Where(x => ids.Contains(x.PostId))
            .GroupBy(x => x.PostId)
            .Select(g => new
            {
                PostId = g.Key,
                Count = g.Count(),
                IsLikedByVisitor = g.Any(x => x.VisitorId == visitorId)
            })
            .ToListAsync();

        return summaries.ToDictionary(
            x => x.PostId,
            x => (x.Count, x.IsLikedByVisitor));
    }

    public async Task<Dictionary<int, (int Count, bool IsLikedByVisitor)>> GetCommentLikeSummariesAsync(
        IEnumerable<int> commentIds,
        string visitorId)
    {
        var ids = commentIds.Distinct().ToList();
        if (!ids.Any())
        {
            return new Dictionary<int, (int Count, bool IsLikedByVisitor)>();
        }

        var summaries = await _db.CommentLikes
            .Where(x => ids.Contains(x.CommentId))
            .GroupBy(x => x.CommentId)
            .Select(g => new
            {
                CommentId = g.Key,
                Count = g.Count(),
                IsLikedByVisitor = g.Any(x => x.VisitorId == visitorId)
            })
            .ToListAsync();

        return summaries.ToDictionary(
            x => x.CommentId,
            x => (x.Count, x.IsLikedByVisitor));
    }

    public async Task TogglePostLikeAsync(string postId, string visitorId)
    {
        var existing = await _db.PostLikes
            .FirstOrDefaultAsync(x => x.PostId == postId && x.VisitorId == visitorId);

        if (existing != null)
        {
            _db.PostLikes.Remove(existing);
        }
        else
        {
            _db.PostLikes.Add(new PostLike
            {
                PostId = postId,
                VisitorId = visitorId,
                LikedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
    }

    public async Task<bool> ToggleCommentLikeAsync(int commentId, string postId, string visitorId)
    {
        var commentExists = await _db.Comments.AnyAsync(x => x.Id == commentId && x.PostId == postId);
        if (!commentExists)
        {
            return false;
        }

        var existing = await _db.CommentLikes
            .FirstOrDefaultAsync(x => x.CommentId == commentId && x.VisitorId == visitorId);

        if (existing != null)
        {
            _db.CommentLikes.Remove(existing);
        }
        else
        {
            _db.CommentLikes.Add(new CommentLike
            {
                CommentId = commentId,
                VisitorId = visitorId,
                LikedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return true;
    }
}
