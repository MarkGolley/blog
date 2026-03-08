using System.Text;
using Google.Cloud.Firestore;

namespace MyBlog.Services;

public class LikeService
{
    private const string PostLikesCollection = "postLikes";
    private const string CommentLikesCollection = "commentLikes";
    private const string CommentsCollection = "comments";

    private readonly FirestoreDb _db;

    public LikeService(FirestoreDb db)
    {
        _db = db;
    }

    public async Task<(int Count, bool IsLikedByVisitor)> GetPostLikeSummaryAsync(string postId, string visitorId)
    {
        var likes = await _db.Collection(PostLikesCollection)
            .WhereEqualTo(nameof(FirestorePostLike.PostId), postId)
            .GetSnapshotAsync();

        var count = likes.Count;
        var isLiked = likes.Documents
            .Any(d => d.TryGetValue<string>(nameof(FirestorePostLike.VisitorId), out var v) && v == visitorId);

        return (count, isLiked);
    }

    public async Task<Dictionary<string, (int Count, bool IsLikedByVisitor)>> GetPostLikeSummariesAsync(
        IEnumerable<string> postIds,
        string visitorId)
    {
        var ids = postIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!ids.Any())
        {
            return new Dictionary<string, (int Count, bool IsLikedByVisitor)>();
        }

        var result = ids.ToDictionary(id => id, _ => (0, false), StringComparer.Ordinal);

        foreach (var batch in Batch(ids, 30))
        {
            var snapshot = await _db.Collection(PostLikesCollection)
                .WhereIn(nameof(FirestorePostLike.PostId), batch.Cast<object>().ToList())
                .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var like = doc.ConvertTo<FirestorePostLike>();
                if (!result.TryGetValue(like.PostId, out var existing))
                {
                    continue;
                }

                var likedByVisitor = existing.Item2 || string.Equals(like.VisitorId, visitorId, StringComparison.Ordinal);
                result[like.PostId] = (existing.Item1 + 1, likedByVisitor);
            }
        }

        return result;
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

        var result = ids.ToDictionary(id => id, _ => (0, false));

        foreach (var batch in Batch(ids, 30))
        {
            var snapshot = await _db.Collection(CommentLikesCollection)
                .WhereIn(nameof(FirestoreCommentLike.CommentId), batch.Cast<object>().ToList())
                .GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                var like = doc.ConvertTo<FirestoreCommentLike>();
                if (!result.TryGetValue(like.CommentId, out var existing))
                {
                    continue;
                }

                var likedByVisitor = existing.Item2 || string.Equals(like.VisitorId, visitorId, StringComparison.Ordinal);
                result[like.CommentId] = (existing.Item1 + 1, likedByVisitor);
            }
        }

        return result;
    }

    public async Task TogglePostLikeAsync(string postId, string visitorId)
    {
        var docId = BuildPostLikeDocId(postId, visitorId);
        var docRef = _db.Collection(PostLikesCollection).Document(docId);
        var snapshot = await docRef.GetSnapshotAsync();

        if (snapshot.Exists)
        {
            await docRef.DeleteAsync();
            return;
        }

        await docRef.CreateAsync(new FirestorePostLike
        {
            PostId = postId,
            VisitorId = visitorId,
            LikedAt = DateTime.UtcNow
        });
    }

    public async Task<bool> ToggleCommentLikeAsync(int commentId, string postId, string visitorId)
    {
        var commentSnapshot = await _db.Collection(CommentsCollection)
            .Document(commentId.ToString())
            .GetSnapshotAsync();

        if (!commentSnapshot.Exists ||
            !commentSnapshot.TryGetValue<string>("PostId", out var existingPostId) ||
            !string.Equals(existingPostId, postId, StringComparison.Ordinal))
        {
            return false;
        }

        var docId = BuildCommentLikeDocId(commentId, visitorId);
        var docRef = _db.Collection(CommentLikesCollection).Document(docId);
        var snapshot = await docRef.GetSnapshotAsync();

        if (snapshot.Exists)
        {
            await docRef.DeleteAsync();
            return true;
        }

        await docRef.CreateAsync(new FirestoreCommentLike
        {
            CommentId = commentId,
            VisitorId = visitorId,
            LikedAt = DateTime.UtcNow
        });

        return true;
    }

    private static IEnumerable<List<T>> Batch<T>(IReadOnlyList<T> items, int batchSize)
    {
        for (var i = 0; i < items.Count; i += batchSize)
        {
            var count = Math.Min(batchSize, items.Count - i);
            yield return items.Skip(i).Take(count).ToList();
        }
    }

    private static string BuildPostLikeDocId(string postId, string visitorId)
        => $"{ToIdPart(postId)}_{ToIdPart(visitorId)}";

    private static string BuildCommentLikeDocId(int commentId, string visitorId)
        => $"{commentId}_{ToIdPart(visitorId)}";

    private static string ToIdPart(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    [FirestoreData]
    private sealed class FirestorePostLike
    {
        [FirestoreProperty]
        public string PostId { get; set; } = string.Empty;

        [FirestoreProperty]
        public string VisitorId { get; set; } = string.Empty;

        [FirestoreProperty]
        public DateTime LikedAt { get; set; }
    }

    [FirestoreData]
    private sealed class FirestoreCommentLike
    {
        [FirestoreProperty]
        public int CommentId { get; set; }

        [FirestoreProperty]
        public string VisitorId { get; set; } = string.Empty;

        [FirestoreProperty]
        public DateTime LikedAt { get; set; }
    }
}
