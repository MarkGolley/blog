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

    private static readonly object LocalStateLock = new();
    private static readonly List<Comment> LocalComments = new();
    private static int _nextLocalCommentId = 1;

    private readonly FirestoreDb? _db;
    private readonly AIModerationService _aiModerationService;

    public CommentService(FirestoreDb? db = null, AIModerationService? aiModerationService = null)
    {
        _db = db;
        _aiModerationService = aiModerationService;
    }

    public async Task AddCommentAsync(Comment comment)
    {
        if (_db == null)
        {
            await AddCommentInMemory(comment, _aiModerationService);
            return;
        }

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

        // AI moderation: check if content is safe
        comment.IsApproved = await (_aiModerationService?.IsCommentSafeAsync(comment.Content) ?? Task.FromResult(true));

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
                ParentCommentId = comment.ParentCommentId,
                IsApproved = comment.IsApproved
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

    public async Task<List<CommentThreadViewModel>> GetCommentsAsync(string postId, bool includeUnapproved = false)
    {
        if (string.IsNullOrWhiteSpace(postId))
            return new List<CommentThreadViewModel>();

        if (_db == null)
        {
            List<Comment> localComments;
            lock (LocalStateLock)
            {
                localComments = LocalComments
                    .Where(c => string.Equals(c.PostId, postId, StringComparison.Ordinal))
                    .Where(c => includeUnapproved || c.IsApproved)
                    .OrderBy(c => c.PostedAt)
                    .Select(CloneComment)
                    .ToList();
            }

            var localRootComments = localComments
                .Where(c => !c.ParentCommentId.HasValue)
                .ToList();

            var localChildrenByParent = localComments
                .Where(c => c.ParentCommentId.HasValue)
                .GroupBy(c => c.ParentCommentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            return BuildCommentThreads(localRootComments, localChildrenByParent, new HashSet<int>(), 1);
        }

        var query = _db.Collection(CommentsCollection)
            .WhereEqualTo(nameof(FirestoreComment.PostId), postId);

        if (!includeUnapproved)
        {
            query = query.WhereEqualTo(nameof(FirestoreComment.IsApproved), true);
        }

        var snapshot = await query.GetSnapshotAsync();

        var comments = snapshot.Documents
            .Select(d => d.ConvertTo<FirestoreComment>())
            .Select(MapToComment)
            .Where(c => includeUnapproved || c.IsApproved)
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

    public async Task<bool> ApproveCommentAsync(int commentId)
    {
        if (_db == null)
        {
            return ApproveCommentInMemory(commentId);
        }

        var commentRef = _db.Collection(CommentsCollection).Document(commentId.ToString());
        var snapshot = await commentRef.GetSnapshotAsync();
        if (!snapshot.Exists)
        {
            return false;
        }

        await commentRef.UpdateAsync(new Dictionary<string, object> { { nameof(FirestoreComment.IsApproved), true } });
        return true;
    }

    public async Task<bool> DeleteCommentAsync(int commentId)
    {
        var idsToDelete = await GetCommentThreadIdsAsync(commentId);
        if (idsToDelete.Count == 0)
        {
            return false;
        }

        if (_db == null)
        {
            return DeleteCommentsInMemory(idsToDelete);
        }

        var batch = _db.StartBatch();
        foreach (var id in idsToDelete)
        {
            var commentRef = _db.Collection(CommentsCollection).Document(id.ToString());
            batch.Delete(commentRef);
        }

        await batch.CommitAsync();
        return true;
    }

    public async Task<List<Comment>> GetPendingCommentsAsync()
    {
        if (_db == null)
        {
            lock (LocalStateLock)
            {
                return LocalComments.Where(c => !c.IsApproved).OrderByDescending(c => c.PostedAt).ToList();
            }
        }

        var snapshot = await _db.Collection(CommentsCollection)
            .WhereEqualTo(nameof(FirestoreComment.IsApproved), false)
            .GetSnapshotAsync();

        return snapshot.Documents
            .Select(d => d.ConvertTo<FirestoreComment>())
            .Select(MapToComment)
            .OrderByDescending(c => c.PostedAt)
            .ToList();
    }

    private async Task AddCommentInMemory(Comment comment, AIModerationService? aiModerationService)
    {
        // AI check outside lock
        comment.IsApproved = await (aiModerationService?.IsCommentSafeAsync(comment.Content) ?? Task.FromResult(true));

        lock (LocalStateLock)
        {
            var postComments = LocalComments
                .Where(c => string.Equals(c.PostId, comment.PostId, StringComparison.Ordinal))
                .ToList();

            if (comment.ParentCommentId.HasValue)
            {
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
            comment.Id = _nextLocalCommentId++;

            LocalComments.Add(CloneComment(comment));
        }
    }

    private static Comment CloneComment(Comment source) => new()
    {
        Id = source.Id,
        Author = source.Author,
        Content = source.Content,
        PostedAt = source.PostedAt,
        PostId = source.PostId,
        ParentCommentId = source.ParentCommentId,
        IsApproved = source.IsApproved
    };

    private static Comment MapToComment(FirestoreComment doc) => new()
    {
        Id = doc.Id,
        Author = doc.Author,
        Content = doc.Content,
        PostedAt = doc.PostedAt,
        PostId = doc.PostId,
        ParentCommentId = doc.ParentCommentId,
        IsApproved = doc.IsApproved
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

    private static bool ApproveCommentInMemory(int commentId)
    {
        lock (LocalStateLock)
        {
            var comment = LocalComments.FirstOrDefault(c => c.Id == commentId);
            if (comment == null)
            {
                return false;
            }

            comment.IsApproved = true;
            return true;
        }
    }

    private async Task<List<int>> GetCommentThreadIdsAsync(int commentId)
    {
        if (_db == null)
        {
            return GetCommentThreadIdsInMemory(commentId);
        }

        var allComments = await _db.Collection(CommentsCollection).GetSnapshotAsync();
        var comments = allComments.Documents
            .Select(d => d.ConvertTo<FirestoreComment>())
            .Select(MapToComment)
            .ToList();

        return GetThreadIds(commentId, comments);
    }

    private static List<int> GetCommentThreadIdsInMemory(int commentId)
    {
        lock (LocalStateLock)
        {
            return GetThreadIds(commentId, LocalComments);
        }
    }

    private static List<int> GetThreadIds(int rootId, List<Comment> allComments)
    {
        var ids = new List<int>();
        var toProcess = new Queue<int>();
        toProcess.Enqueue(rootId);

        while (toProcess.Count > 0)
        {
            var currentId = toProcess.Dequeue();
            ids.Add(currentId);

            var replies = allComments.Where(c => c.ParentCommentId == currentId).Select(c => c.Id);
            foreach (var replyId in replies)
            {
                toProcess.Enqueue(replyId);
            }
        }

        return ids;
    }

    private static bool DeleteCommentsInMemory(List<int> idsToDelete)
    {
        lock (LocalStateLock)
        {
            LocalComments.RemoveAll(c => idsToDelete.Contains(c.Id));
            return true;
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

        [FirestoreProperty]
        public bool IsApproved { get; set; }
    }
}