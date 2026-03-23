using System.Text;
using System.Globalization;
using Google.Cloud.Firestore;

namespace MyBlog.Services;

public class LikeService
{
    private const string PostLikesCollection = "postLikes";
    private const string CommentLikesCollection = "commentLikes";
    private const string CommentsCollection = "comments";
    private const string PostLikeStatsCollection = "postLikeStats";
    private const string CommentLikeStatsCollection = "commentLikeStats";

    private static readonly object LocalStateLock = new();
    private static readonly HashSet<string> LocalPostLikes = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LocalCommentLikes = new(StringComparer.Ordinal);

    private readonly FirestoreDb? _db;

    public LikeService(FirestoreDb? db = null)
    {
        _db = db;
    }

    public async Task<(int Count, bool IsLikedByVisitor)> GetPostLikeSummaryAsync(string postId, string visitorId)
    {
        if (_db == null)
        {
            lock (LocalStateLock)
            {
                var likeKey = BuildLocalPostLikeKey(postId, visitorId);
                var prefix = $"{postId}::";
                var localCount = LocalPostLikes.Count(x => x.StartsWith(prefix, StringComparison.Ordinal));
                return (localCount, LocalPostLikes.Contains(likeKey));
            }
        }

        var likes = await _db.Collection(PostLikesCollection)
            .Document(BuildPostLikeDocId(postId, visitorId))
            .GetSnapshotAsync();

        var count = await GetPostLikeCountAsync(postId);
        var isLiked = likes.Exists;

        return (Math.Max(0, count), isLiked);
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

        if (_db == null)
        {
            lock (LocalStateLock)
            {
                foreach (var id in ids)
                {
                    var prefix = $"{id}::";
                    var count = LocalPostLikes.Count(x => x.StartsWith(prefix, StringComparison.Ordinal));
                    var isLikedByVisitor = LocalPostLikes.Contains(BuildLocalPostLikeKey(id, visitorId));
                    result[id] = (count, isLikedByVisitor);
                }
            }

            return result;
        }

        foreach (var id in ids)
        {
            var count = await GetPostLikeCountAsync(id);
            var liked = await _db.Collection(PostLikesCollection)
                .Document(BuildPostLikeDocId(id, visitorId))
                .GetSnapshotAsync();
            result[id] = (Math.Max(0, count), liked.Exists);
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

        if (_db == null)
        {
            lock (LocalStateLock)
            {
                foreach (var id in ids)
                {
                    var prefix = $"{id}::";
                    var count = LocalCommentLikes.Count(x => x.StartsWith(prefix, StringComparison.Ordinal));
                    var isLikedByVisitor = LocalCommentLikes.Contains(BuildLocalCommentLikeKey(id, visitorId));
                    result[id] = (count, isLikedByVisitor);
                }
            }

            return result;
        }

        foreach (var id in ids)
        {
            var count = await GetCommentLikeCountAsync(id);
            var liked = await _db.Collection(CommentLikesCollection)
                .Document(BuildCommentLikeDocId(id, visitorId))
                .GetSnapshotAsync();
            result[id] = (Math.Max(0, count), liked.Exists);
        }

        return result;
    }

    public async Task TogglePostLikeAsync(string postId, string visitorId)
    {
        if (_db == null)
        {
            lock (LocalStateLock)
            {
                var key = BuildLocalPostLikeKey(postId, visitorId);
                if (!LocalPostLikes.Remove(key))
                {
                    LocalPostLikes.Add(key);
                }
            }

            return;
        }

        var docId = BuildPostLikeDocId(postId, visitorId);
        var docRef = _db.Collection(PostLikesCollection).Document(docId);
        var statsRef = _db.Collection(PostLikeStatsCollection).Document(ToIdPart(postId));

        await _db.RunTransactionAsync(async transaction =>
        {
            var likeSnapshot = await transaction.GetSnapshotAsync(docRef);
            var statsSnapshot = await transaction.GetSnapshotAsync(statsRef);
            var currentCount = GetLikeCountFromStatsSnapshot(statsSnapshot);

            if (likeSnapshot.Exists)
            {
                transaction.Delete(docRef);
                var nextCount = Math.Max(0, currentCount - 1);
                transaction.Set(statsRef, new FirestoreLikeStat
                {
                    Count = nextCount,
                    UpdatedAt = DateTime.UtcNow
                });
                return;
            }

            transaction.Create(docRef, new FirestorePostLike
            {
                PostId = postId,
                VisitorId = visitorId,
                LikedAt = DateTime.UtcNow
            });
            transaction.Set(statsRef, new FirestoreLikeStat
            {
                Count = currentCount + 1,
                UpdatedAt = DateTime.UtcNow
            });
        });
    }

    public async Task<bool> ToggleCommentLikeAsync(int commentId, string postId, string visitorId)
    {
        if (_db == null)
        {
            if (commentId <= 0 || string.IsNullOrWhiteSpace(postId))
            {
                return false;
            }

            lock (LocalStateLock)
            {
                var key = BuildLocalCommentLikeKey(commentId, visitorId);
                if (!LocalCommentLikes.Remove(key))
                {
                    LocalCommentLikes.Add(key);
                }
            }

            return true;
        }

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
        var statsRef = _db.Collection(CommentLikeStatsCollection).Document(commentId.ToString(CultureInfo.InvariantCulture));

        await _db.RunTransactionAsync(async transaction =>
        {
            var likeSnapshot = await transaction.GetSnapshotAsync(docRef);
            var statsSnapshot = await transaction.GetSnapshotAsync(statsRef);
            var currentCount = GetLikeCountFromStatsSnapshot(statsSnapshot);

            if (likeSnapshot.Exists)
            {
                transaction.Delete(docRef);
                var nextCount = Math.Max(0, currentCount - 1);
                transaction.Set(statsRef, new FirestoreLikeStat
                {
                    Count = nextCount,
                    UpdatedAt = DateTime.UtcNow
                });
                return;
            }

            transaction.Create(docRef, new FirestoreCommentLike
            {
                CommentId = commentId,
                VisitorId = visitorId,
                LikedAt = DateTime.UtcNow
            });
            transaction.Set(statsRef, new FirestoreLikeStat
            {
                Count = currentCount + 1,
                UpdatedAt = DateTime.UtcNow
            });
        });

        return true;
    }

    private async Task<int> GetPostLikeCountAsync(string postId)
    {
        if (_db is null)
        {
            return 0;
        }

        var statsRef = _db.Collection(PostLikeStatsCollection).Document(ToIdPart(postId));
        var statsSnapshot = await statsRef.GetSnapshotAsync();
        if (statsSnapshot.Exists)
        {
            return GetLikeCountFromStatsSnapshot(statsSnapshot);
        }

        var likesSnapshot = await _db.Collection(PostLikesCollection)
            .WhereEqualTo(nameof(FirestorePostLike.PostId), postId)
            .GetSnapshotAsync();
        var count = Math.Max(0, likesSnapshot.Count);

        await statsRef.SetAsync(new FirestoreLikeStat
        {
            Count = count,
            UpdatedAt = DateTime.UtcNow
        }, SetOptions.MergeAll);
        return count;
    }

    private async Task<int> GetCommentLikeCountAsync(int commentId)
    {
        if (_db is null)
        {
            return 0;
        }

        var statsRef = _db.Collection(CommentLikeStatsCollection).Document(commentId.ToString(CultureInfo.InvariantCulture));
        var statsSnapshot = await statsRef.GetSnapshotAsync();
        if (statsSnapshot.Exists)
        {
            return GetLikeCountFromStatsSnapshot(statsSnapshot);
        }

        var likesSnapshot = await _db.Collection(CommentLikesCollection)
            .WhereEqualTo(nameof(FirestoreCommentLike.CommentId), commentId)
            .GetSnapshotAsync();
        var count = Math.Max(0, likesSnapshot.Count);

        await statsRef.SetAsync(new FirestoreLikeStat
        {
            Count = count,
            UpdatedAt = DateTime.UtcNow
        }, SetOptions.MergeAll);
        return count;
    }

    private static int GetLikeCountFromStatsSnapshot(DocumentSnapshot statsSnapshot)
    {
        if (!statsSnapshot.Exists)
        {
            return 0;
        }

        if (statsSnapshot.TryGetValue<long>(nameof(FirestoreLikeStat.Count), out var asLong))
        {
            return asLong < 0 ? 0 : (int)Math.Min(int.MaxValue, asLong);
        }

        if (statsSnapshot.TryGetValue<int>(nameof(FirestoreLikeStat.Count), out var asInt))
        {
            return Math.Max(0, asInt);
        }

        return 0;
    }

    private static string BuildPostLikeDocId(string postId, string visitorId)
        => $"{ToIdPart(postId)}_{ToIdPart(visitorId)}";

    private static string BuildCommentLikeDocId(int commentId, string visitorId)
        => $"{commentId}_{ToIdPart(visitorId)}";

    private static string BuildLocalPostLikeKey(string postId, string visitorId)
        => $"{postId}::{visitorId}";

    private static string BuildLocalCommentLikeKey(int commentId, string visitorId)
        => $"{commentId}::{visitorId}";

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

    [FirestoreData]
    private sealed class FirestoreLikeStat
    {
        [FirestoreProperty]
        public int Count { get; set; }

        [FirestoreProperty]
        public DateTime UpdatedAt { get; set; }
    }
}
