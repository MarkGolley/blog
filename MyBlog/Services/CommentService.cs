using Google.Cloud.Firestore;
using MyBlog.Models;

namespace MyBlog.Services;

public class CommentService
{
    public const int MaxThreadDepth = 5;

    private const string CommentsCollection = "comments";
    private const string MetaCollection = "meta";
    private const string CountersDocument = "counters";
    private const string NextCommentIdField = "nextCommentId";

    private readonly FirestoreDb _db;

    public CommentService(FirestoreDb db)
    {
        _db = db;
    }

    public async Task AddCommentAsync(Comment comment)
    {
        var commentsCollection = _db.Collection(CommentsCollection);

        if (comment.ParentCommentId.HasValue)
        {
            var postCommentsSnapshot = await commentsCollection
                .WhereEqualTo(nameof(FirestoreComment.PostId), comment.PostId)
                .GetSnapshotAsync();

            var commentById = postCommentsSnapshot.Documents
                .Select(d => d.ConvertTo<FirestoreComment>())
                .ToDictionary(c => c.Id, c => c.ParentCommentId);

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

        var countersRef = _db.Collection(MetaCollection).Document(CountersDocument);
        var assignedId = await _db.RunTransactionAsync(async transaction =>
        {
            var countersSnapshot = await transaction.GetSnapshotAsync(countersRef);
            var nextId = countersSnapshot.Exists &&
                         countersSnapshot.TryGetValue<long>(NextCommentIdField, out var current)
                ? (int)current
                : 1;

            var commentRef = commentsCollection.Document(nextId.ToString());

            var doc = new FirestoreComment
            {
                Id = nextId,
                Author = comment.Author,
                Content = comment.Content,
                PostedAt = comment.PostedAt,
                PostId = comment.PostId,
                ParentCommentId = comment.ParentCommentId
            };

            transaction.Create(commentRef, doc);
            transaction.Set(countersRef, new Dictionary<string, object>
            {
                [NextCommentIdField] = nextId + 1
            }, SetOptions.MergeAll);

            return nextId;
        });

        comment.Id = assignedId;
    }

    public async Task<List<CommentThreadViewModel>> GetCommentsAsync(string postId)
    {
        if (string.IsNullOrWhiteSpace(postId))
            return new List<CommentThreadViewModel>();

        var snapshot = await _db.Collection(CommentsCollection)
            .WhereEqualTo(nameof(FirestoreComment.PostId), postId)
            .GetSnapshotAsync();

        var comments = snapshot.Documents
            .Select(d => d.ConvertTo<FirestoreComment>())
            .Select(MapToComment)
            .OrderBy(c => c.PostedAt)
            .ToList();

        var rootComments = comments
            .Where(c => !c.ParentCommentId.HasValue)
            .ToList();

        var childrenByParent = comments
            .Where(c => c.ParentCommentId.HasValue)
            .GroupBy(c => c.ParentCommentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return BuildCommentThreads(rootComments, childrenByParent, new HashSet<int>(), 1);
    }

    private static Comment MapToComment(FirestoreComment doc) => new()
    {
        Id = doc.Id,
        Author = doc.Author,
        Content = doc.Content,
        PostedAt = doc.PostedAt,
        PostId = doc.PostId,
        ParentCommentId = doc.ParentCommentId
    };

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

    [FirestoreData]
    private sealed class FirestoreComment
    {
        [FirestoreProperty]
        public int Id { get; set; }

        [FirestoreProperty]
        public string Author { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Content { get; set; } = string.Empty;

        [FirestoreProperty]
        public DateTime PostedAt { get; set; }

        [FirestoreProperty]
        public string PostId { get; set; } = string.Empty;

        [FirestoreProperty]
        public int? ParentCommentId { get; set; }
    }
}
