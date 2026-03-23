using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;

namespace MyBlog.Services;

public enum SubscribeStatus
{
    PendingConfirmation,
    AlreadySubscribed
}

public enum SubscriptionTokenStatus
{
    Completed,
    AlreadyApplied,
    InvalidToken
}

public sealed record SubscribeResult(
    SubscribeStatus Status,
    string Email,
    string? ConfirmationToken,
    string? UnsubscribeToken);

public sealed record SubscriptionTokenResult(
    SubscriptionTokenStatus Status,
    string? Email);

public sealed record NotificationSubscriber(
    string SubscriberId,
    string Email,
    string UnsubscribeToken);

public class SubscriptionService
{
    private const string SubscribersCollection = "subscribers";
    private const string NotificationsCollection = "subscriberNotifications";
    private const string StatusPending = "pending";
    private const string StatusConfirmed = "confirmed";
    private const string StatusUnsubscribed = "unsubscribed";

    private static readonly object LocalStateLock = new();
    private static readonly Dictionary<string, LocalSubscriber> LocalSubscribersByEmail = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> LocalEmailByConfirmTokenHash = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> LocalEmailByUnsubscribeToken = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LocalNotificationKeys = new(StringComparer.Ordinal);

    private readonly FirestoreDb? _db;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(ILogger<SubscriptionService> logger, FirestoreDb? db = null)
    {
        _logger = logger;
        _db = db;
    }

    public async Task<SubscribeResult> SubscribeAsync(string rawEmail)
    {
        var email = NormalizeEmail(rawEmail);
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("A valid email address is required.");
        }

        if (_db == null)
        {
            return SubscribeInMemory(email);
        }

        var now = DateTime.UtcNow;
        var subscriberId = BuildSubscriberDocumentId(email);
        var subscriberRef = _db.Collection(SubscribersCollection).Document(subscriberId);
        var snapshot = await subscriberRef.GetSnapshotAsync();

        if (snapshot.Exists)
        {
            var existing = snapshot.ConvertTo<FirestoreSubscriber>();
            if (string.Equals(existing.Status, StatusConfirmed, StringComparison.Ordinal))
            {
                return new SubscribeResult(SubscribeStatus.AlreadySubscribed, email, null, null);
            }

            var confirmToken = GenerateToken();
            var unsubscribeToken = string.IsNullOrWhiteSpace(existing.UnsubscribeToken)
                ? GenerateToken()
                : existing.UnsubscribeToken;

            var updated = new FirestoreSubscriber
            {
                Email = email,
                Status = StatusPending,
                ConfirmTokenHash = HashToken(confirmToken),
                UnsubscribeToken = unsubscribeToken,
                CreatedAt = existing.CreatedAt == default ? now : existing.CreatedAt,
                UpdatedAt = now,
                ConfirmedAt = null,
                UnsubscribedAt = null
            };

            await subscriberRef.SetAsync(updated);
            return new SubscribeResult(SubscribeStatus.PendingConfirmation, email, confirmToken, unsubscribeToken);
        }

        var createdConfirmToken = GenerateToken();
        var createdUnsubscribeToken = GenerateToken();
        var created = new FirestoreSubscriber
        {
            Email = email,
            Status = StatusPending,
            ConfirmTokenHash = HashToken(createdConfirmToken),
            UnsubscribeToken = createdUnsubscribeToken,
            CreatedAt = now,
            UpdatedAt = now
        };

        await subscriberRef.SetAsync(created);
        return new SubscribeResult(SubscribeStatus.PendingConfirmation, email, createdConfirmToken, createdUnsubscribeToken);
    }

    public async Task<SubscriptionTokenResult> ConfirmAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new SubscriptionTokenResult(SubscriptionTokenStatus.InvalidToken, null);
        }

        var trimmedToken = token.Trim();

        if (_db == null)
        {
            return ConfirmInMemory(trimmedToken);
        }

        var tokenHash = HashToken(trimmedToken);
        var snapshot = await _db.Collection(SubscribersCollection)
            .WhereEqualTo(nameof(FirestoreSubscriber.ConfirmTokenHash), tokenHash)
            .Limit(1)
            .GetSnapshotAsync();

        var matched = snapshot.Documents.FirstOrDefault();
        if (matched == null)
        {
            return new SubscriptionTokenResult(SubscriptionTokenStatus.InvalidToken, null);
        }

        var subscriber = matched.ConvertTo<FirestoreSubscriber>();
        if (string.Equals(subscriber.Status, StatusConfirmed, StringComparison.Ordinal))
        {
            return new SubscriptionTokenResult(SubscriptionTokenStatus.AlreadyApplied, subscriber.Email);
        }

        if (!string.Equals(subscriber.Status, StatusPending, StringComparison.Ordinal))
        {
            return new SubscriptionTokenResult(SubscriptionTokenStatus.InvalidToken, null);
        }

        await matched.Reference.SetAsync(new Dictionary<string, object?>
        {
            [nameof(FirestoreSubscriber.Status)] = StatusConfirmed,
            [nameof(FirestoreSubscriber.ConfirmedAt)] = DateTime.UtcNow,
            [nameof(FirestoreSubscriber.UnsubscribedAt)] = null,
            [nameof(FirestoreSubscriber.ConfirmTokenHash)] = null,
            [nameof(FirestoreSubscriber.UpdatedAt)] = DateTime.UtcNow
        }, SetOptions.MergeAll);

        return new SubscriptionTokenResult(SubscriptionTokenStatus.Completed, subscriber.Email);
    }

    public async Task<SubscriptionTokenResult> UnsubscribeAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return new SubscriptionTokenResult(SubscriptionTokenStatus.InvalidToken, null);
        }

        var trimmedToken = token.Trim();

        if (_db == null)
        {
            return UnsubscribeInMemory(trimmedToken);
        }

        var snapshot = await _db.Collection(SubscribersCollection)
            .WhereEqualTo(nameof(FirestoreSubscriber.UnsubscribeToken), trimmedToken)
            .Limit(1)
            .GetSnapshotAsync();

        var matched = snapshot.Documents.FirstOrDefault();
        if (matched == null)
        {
            return new SubscriptionTokenResult(SubscriptionTokenStatus.InvalidToken, null);
        }

        var subscriber = matched.ConvertTo<FirestoreSubscriber>();
        if (string.Equals(subscriber.Status, StatusUnsubscribed, StringComparison.Ordinal))
        {
            return new SubscriptionTokenResult(SubscriptionTokenStatus.AlreadyApplied, subscriber.Email);
        }

        await matched.Reference.SetAsync(new Dictionary<string, object?>
        {
            [nameof(FirestoreSubscriber.Status)] = StatusUnsubscribed,
            [nameof(FirestoreSubscriber.UnsubscribedAt)] = DateTime.UtcNow,
            [nameof(FirestoreSubscriber.ConfirmTokenHash)] = null,
            [nameof(FirestoreSubscriber.UpdatedAt)] = DateTime.UtcNow
        }, SetOptions.MergeAll);

        return new SubscriptionTokenResult(SubscriptionTokenStatus.Completed, subscriber.Email);
    }

    public async Task<IReadOnlyList<NotificationSubscriber>> GetConfirmedSubscribersAsync()
    {
        if (_db == null)
        {
            lock (LocalStateLock)
            {
                return LocalSubscribersByEmail.Values
                    .Where(x => string.Equals(x.Status, StatusConfirmed, StringComparison.Ordinal))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Email) && !string.IsNullOrWhiteSpace(x.UnsubscribeToken))
                    .Select(x => new NotificationSubscriber(x.Id, x.Email, x.UnsubscribeToken))
                    .ToList();
            }
        }

        var snapshot = await _db.Collection(SubscribersCollection)
            .WhereEqualTo(nameof(FirestoreSubscriber.Status), StatusConfirmed)
            .GetSnapshotAsync();

        return snapshot.Documents
            .Select(document => new
            {
                DocumentId = document.Id,
                Data = document.ConvertTo<FirestoreSubscriber>()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Data.Email) && !string.IsNullOrWhiteSpace(x.Data.UnsubscribeToken))
            .Select(x => new NotificationSubscriber(x.DocumentId, x.Data.Email, x.Data.UnsubscribeToken))
            .ToList();
    }

    public async Task<bool> HasPostNotificationAsync(string subscriberId, string postSlug)
    {
        var normalizedSubscriberId = subscriberId?.Trim() ?? string.Empty;
        var normalizedPostSlug = postSlug?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedSubscriberId) || string.IsNullOrWhiteSpace(normalizedPostSlug))
        {
            return false;
        }

        if (_db == null)
        {
            lock (LocalStateLock)
            {
                return LocalNotificationKeys.Contains(BuildLocalNotificationKey(normalizedSubscriberId, normalizedPostSlug));
            }
        }

        var notificationRef = GetNotificationDocument(normalizedSubscriberId, normalizedPostSlug);
        var snapshot = await notificationRef.GetSnapshotAsync();
        return snapshot.Exists;
    }

    public async Task<HashSet<string>> GetNotifiedSubscriberIdsAsync(string postSlug)
    {
        var normalizedPostSlug = postSlug?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPostSlug))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        if (_db == null)
        {
            lock (LocalStateLock)
            {
                return LocalNotificationKeys
                    .Select(key => key.Split("::", 2, StringSplitOptions.None))
                    .Where(parts =>
                        parts.Length == 2 &&
                        string.Equals(parts[1], normalizedPostSlug, StringComparison.Ordinal))
                    .Select(parts => parts[0])
                    .Where(subscriberId => !string.IsNullOrWhiteSpace(subscriberId))
                    .ToHashSet(StringComparer.Ordinal);
            }
        }

        var snapshot = await _db.Collection(NotificationsCollection)
            .WhereEqualTo(nameof(FirestoreSubscriberNotification.PostSlug), normalizedPostSlug)
            .GetSnapshotAsync();

        return snapshot.Documents
            .Select(document => document.ConvertTo<FirestoreSubscriberNotification>())
            .Select(document => document.SubscriberId?.Trim() ?? string.Empty)
            .Where(subscriberId => !string.IsNullOrWhiteSpace(subscriberId))
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task MarkPostNotificationAsync(string subscriberId, string postSlug)
    {
        var normalizedSubscriberId = subscriberId?.Trim() ?? string.Empty;
        var normalizedPostSlug = postSlug?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedSubscriberId) || string.IsNullOrWhiteSpace(normalizedPostSlug))
        {
            return;
        }

        if (_db == null)
        {
            lock (LocalStateLock)
            {
                LocalNotificationKeys.Add(BuildLocalNotificationKey(normalizedSubscriberId, normalizedPostSlug));
            }

            return;
        }

        var notificationRef = GetNotificationDocument(normalizedSubscriberId, normalizedPostSlug);
        await notificationRef.SetAsync(new FirestoreSubscriberNotification
        {
            SubscriberId = normalizedSubscriberId,
            PostSlug = normalizedPostSlug,
            NotifiedAt = DateTime.UtcNow
        });
    }

    public async Task MarkPostNotificationsAsync(IReadOnlyList<string> subscriberIds, string postSlug)
    {
        var normalizedPostSlug = postSlug?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPostSlug) || subscriberIds.Count == 0)
        {
            return;
        }

        var normalizedSubscriberIds = subscriberIds
            .Where(subscriberId => !string.IsNullOrWhiteSpace(subscriberId))
            .Select(subscriberId => subscriberId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalizedSubscriberIds.Count == 0)
        {
            return;
        }

        if (_db == null)
        {
            lock (LocalStateLock)
            {
                foreach (var subscriberId in normalizedSubscriberIds)
                {
                    LocalNotificationKeys.Add(BuildLocalNotificationKey(subscriberId, normalizedPostSlug));
                }
            }

            return;
        }

        var nowUtc = DateTime.UtcNow;
        var batch = _db.StartBatch();
        foreach (var subscriberId in normalizedSubscriberIds)
        {
            var notificationRef = GetNotificationDocument(subscriberId, normalizedPostSlug);
            batch.Set(notificationRef, new FirestoreSubscriberNotification
            {
                SubscriberId = subscriberId,
                PostSlug = normalizedPostSlug,
                NotifiedAt = nowUtc
            });
        }

        await batch.CommitAsync();
    }

    private SubscribeResult SubscribeInMemory(string email)
    {
        lock (LocalStateLock)
        {
            if (LocalSubscribersByEmail.TryGetValue(email, out var existingSubscriber))
            {
                if (string.Equals(existingSubscriber.Status, StatusConfirmed, StringComparison.Ordinal))
                {
                    return new SubscribeResult(SubscribeStatus.AlreadySubscribed, email, null, null);
                }

                if (!string.IsNullOrWhiteSpace(existingSubscriber.ConfirmTokenHash))
                {
                    LocalEmailByConfirmTokenHash.Remove(existingSubscriber.ConfirmTokenHash);
                }

                var nextConfirmToken = GenerateToken();
                var nextConfirmTokenHash = HashToken(nextConfirmToken);

                existingSubscriber.Status = StatusPending;
                existingSubscriber.ConfirmTokenHash = nextConfirmTokenHash;
                existingSubscriber.UnsubscribedAt = null;
                existingSubscriber.ConfirmedAt = null;
                existingSubscriber.UpdatedAt = DateTime.UtcNow;
                existingSubscriber.UnsubscribeToken = string.IsNullOrWhiteSpace(existingSubscriber.UnsubscribeToken)
                    ? GenerateToken()
                    : existingSubscriber.UnsubscribeToken;

                LocalEmailByConfirmTokenHash[nextConfirmTokenHash] = email;
                LocalEmailByUnsubscribeToken[existingSubscriber.UnsubscribeToken] = email;

                return new SubscribeResult(
                    SubscribeStatus.PendingConfirmation,
                    email,
                    nextConfirmToken,
                    existingSubscriber.UnsubscribeToken);
            }

            var createdConfirmToken = GenerateToken();
            var createdConfirmTokenHash = HashToken(createdConfirmToken);
            var createdUnsubscribeToken = GenerateToken();

            var created = new LocalSubscriber
            {
                Id = BuildSubscriberDocumentId(email),
                Email = email,
                Status = StatusPending,
                ConfirmTokenHash = createdConfirmTokenHash,
                UnsubscribeToken = createdUnsubscribeToken,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            LocalSubscribersByEmail[email] = created;
            LocalEmailByConfirmTokenHash[createdConfirmTokenHash] = email;
            LocalEmailByUnsubscribeToken[createdUnsubscribeToken] = email;

            return new SubscribeResult(
                SubscribeStatus.PendingConfirmation,
                email,
                createdConfirmToken,
                createdUnsubscribeToken);
        }
    }

    private SubscriptionTokenResult ConfirmInMemory(string token)
    {
        lock (LocalStateLock)
        {
            var tokenHash = HashToken(token);
            if (!LocalEmailByConfirmTokenHash.TryGetValue(tokenHash, out var email) ||
                !LocalSubscribersByEmail.TryGetValue(email, out var subscriber))
            {
                return new SubscriptionTokenResult(SubscriptionTokenStatus.InvalidToken, null);
            }

            if (string.Equals(subscriber.Status, StatusConfirmed, StringComparison.Ordinal))
            {
                return new SubscriptionTokenResult(SubscriptionTokenStatus.AlreadyApplied, subscriber.Email);
            }

            if (!string.Equals(subscriber.Status, StatusPending, StringComparison.Ordinal))
            {
                return new SubscriptionTokenResult(SubscriptionTokenStatus.InvalidToken, null);
            }

            subscriber.Status = StatusConfirmed;
            subscriber.ConfirmedAt = DateTime.UtcNow;
            subscriber.UnsubscribedAt = null;
            subscriber.UpdatedAt = DateTime.UtcNow;
            subscriber.ConfirmTokenHash = null;

            LocalEmailByConfirmTokenHash.Remove(tokenHash);
            return new SubscriptionTokenResult(SubscriptionTokenStatus.Completed, subscriber.Email);
        }
    }

    private SubscriptionTokenResult UnsubscribeInMemory(string token)
    {
        lock (LocalStateLock)
        {
            if (!LocalEmailByUnsubscribeToken.TryGetValue(token, out var email) ||
                !LocalSubscribersByEmail.TryGetValue(email, out var subscriber))
            {
                return new SubscriptionTokenResult(SubscriptionTokenStatus.InvalidToken, null);
            }

            if (string.Equals(subscriber.Status, StatusUnsubscribed, StringComparison.Ordinal))
            {
                return new SubscriptionTokenResult(SubscriptionTokenStatus.AlreadyApplied, subscriber.Email);
            }

            if (!string.IsNullOrWhiteSpace(subscriber.ConfirmTokenHash))
            {
                LocalEmailByConfirmTokenHash.Remove(subscriber.ConfirmTokenHash);
            }

            subscriber.Status = StatusUnsubscribed;
            subscriber.UnsubscribedAt = DateTime.UtcNow;
            subscriber.UpdatedAt = DateTime.UtcNow;
            subscriber.ConfirmTokenHash = null;

            return new SubscriptionTokenResult(SubscriptionTokenStatus.Completed, subscriber.Email);
        }
    }

    private DocumentReference GetNotificationDocument(string subscriberId, string postSlug)
    {
        if (_db == null)
        {
            throw new InvalidOperationException("Firestore is unavailable.");
        }

        var docId = BuildNotificationDocumentId(subscriberId, postSlug);
        return _db.Collection(NotificationsCollection).Document(docId);
    }

    private static string NormalizeEmail(string rawEmail)
    {
        return rawEmail?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static string BuildSubscriberDocumentId(string email)
        => ToIdPart(email);

    private static string BuildNotificationDocumentId(string subscriberId, string postSlug)
        => $"{subscriberId}_{ToIdPart(postSlug)}";

    private static string BuildLocalNotificationKey(string subscriberId, string postSlug)
        => $"{subscriberId}::{postSlug}";

    private static string ToIdPart(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [FirestoreData]
    private sealed class FirestoreSubscriber
    {
        [FirestoreProperty]
        public string Email { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Status { get; set; } = StatusPending;

        [FirestoreProperty]
        public string? ConfirmTokenHash { get; set; }

        [FirestoreProperty]
        public string UnsubscribeToken { get; set; } = string.Empty;

        [FirestoreProperty]
        public DateTime CreatedAt { get; set; }

        [FirestoreProperty]
        public DateTime UpdatedAt { get; set; }

        [FirestoreProperty]
        public DateTime? ConfirmedAt { get; set; }

        [FirestoreProperty]
        public DateTime? UnsubscribedAt { get; set; }
    }

    [FirestoreData]
    private sealed class FirestoreSubscriberNotification
    {
        [FirestoreProperty]
        public string SubscriberId { get; set; } = string.Empty;

        [FirestoreProperty]
        public string PostSlug { get; set; } = string.Empty;

        [FirestoreProperty]
        public DateTime NotifiedAt { get; set; }
    }

    private sealed class LocalSubscriber
    {
        public string Id { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Status { get; set; } = StatusPending;

        public string? ConfirmTokenHash { get; set; }

        public string UnsubscribeToken { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public DateTime? ConfirmedAt { get; set; }

        public DateTime? UnsubscribedAt { get; set; }
    }
}
