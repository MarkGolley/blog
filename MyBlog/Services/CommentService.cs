using Microsoft.EntityFrameworkCore;
using MyBlog.Data;
using MyBlog.Models;

namespace MyBlog.Services;

public class CommentService
{
    public const int MaxThreadDepth = 5;

    private readonly BlogDbContext _db;

    public CommentService(BlogDbContext db)
    {
        _db = db;
    }

    public async Task AddCommentAsync(Comment comment)
    {
        if (comment.ParentCommentId.HasValue)
        {
            var postComments = await _db.Comments
                .Where(c => c.PostId == comment.PostId)
                .Select(c => new { c.Id, c.ParentCommentId })
                .ToListAsync();

            var commentById = postComments.ToDictionary(c => c.Id, c => c.ParentCommentId);
            if (!commentById.ContainsKey(comment.ParentCommentId.Value))
            {
                throw new InvalidOperationException("The comment you are replying to could not be found.");
            }

            var parentDepth = GetDepth(comment.ParentCommentId.Value, commentById);
            var replyDepth = parentDepth + 1;
            if (replyDepth > MaxThreadDepth)
            {
                throw new InvalidOperationException($"Reply depth limit reached. Max depth is {MaxThreadDepth} levels.");
            }
        }

        comment.PostedAt = DateTime.UtcNow;
        _db.Comments.Add(comment);
        await _db.SaveChangesAsync();
    }

    public async Task<List<CommentThreadViewModel>> GetCommentsAsync(string postId)
    {
        if (string.IsNullOrWhiteSpace(postId))
            return new List<CommentThreadViewModel>();

        var comments = await _db.Comments
            .Where(c => c.PostId == postId)
            .OrderBy(c => c.PostedAt)
            .ToListAsync();

        var rootComments = comments
            .Where(c => !c.ParentCommentId.HasValue)
            .ToList();

        var childrenByParent = comments
            .Where(c => c.ParentCommentId.HasValue)
            .GroupBy(c => c.ParentCommentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return BuildCommentThreads(rootComments, childrenByParent, new HashSet<int>(), 1);
    }

    private static List<CommentThreadViewModel> BuildCommentThreads(
        List<Comment> comments,
        IReadOnlyDictionary<int, List<Comment>> childrenByParent,
        HashSet<int> visited,
        int depth)
    {
        var result = new List<CommentThreadViewModel>();
        foreach (var comment in comments)
        {
            if (!visited.Add(comment.Id))
            {
                continue;
            }

            childrenByParent.TryGetValue(comment.Id, out var replies);
            var builtReplies = BuildCommentThreads(replies ?? new List<Comment>(), childrenByParent, visited, depth + 1);
            var totalReplyCount = builtReplies.Count + builtReplies.Sum(r => r.TotalReplyCount);

            result.Add(new CommentThreadViewModel
            {
                Comment = comment,
                Depth = depth,
                TotalReplyCount = totalReplyCount,
                ContentHtml = CommentContentFormatter.ToSafeHtml(comment.Content),
                Replies = builtReplies
            });
        }

        return result;
    }

    private static int GetDepth(int commentId, IReadOnlyDictionary<int, int?> commentById)
    {
        var visited = new HashSet<int>();
        var depth = 1;
        var currentId = commentId;

        while (true)
        {
            if (!visited.Add(currentId))
            {
                throw new InvalidOperationException("The reply chain is invalid due to a loop.");
            }

            if (!commentById.TryGetValue(currentId, out var parentId) || !parentId.HasValue)
            {
                return depth;
            }

            depth++;
            currentId = parentId.Value;
        }
    }
}
