using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace MyBlog.Services;

public sealed class FirestoreDataProtectionKeyRepository : IXmlRepository
{
    private const string DataProtectionKeysCollection = "dataProtectionKeys";

    private readonly FirestoreDb _db;
    private readonly ILogger<FirestoreDataProtectionKeyRepository> _logger;

    public FirestoreDataProtectionKeyRepository(
        FirestoreDb db,
        ILogger<FirestoreDataProtectionKeyRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        try
        {
            var snapshot = _db.Collection(DataProtectionKeysCollection)
                .OrderBy(nameof(FirestoreDataProtectionKeyDocument.CreatedAt))
                .GetSnapshotAsync()
                .GetAwaiter()
                .GetResult();

            var elements = new List<XElement>(snapshot.Count);
            foreach (var document in snapshot.Documents)
            {
                if (!document.ContainsField(nameof(FirestoreDataProtectionKeyDocument.Xml)))
                {
                    continue;
                }

                var xml = document.GetValue<string>(nameof(FirestoreDataProtectionKeyDocument.Xml));
                if (string.IsNullOrWhiteSpace(xml))
                {
                    continue;
                }

                try
                {
                    elements.Add(XElement.Parse(xml, LoadOptions.PreserveWhitespace));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping invalid Data Protection key document {DocumentId}.", document.Id);
                }
            }

            return elements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Data Protection keys from Firestore.");
            return Array.Empty<XElement>();
        }
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);

        var xml = element.ToString(SaveOptions.DisableFormatting);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))).ToLowerInvariant();
        var docId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{hash[..12]}";

        var payload = new FirestoreDataProtectionKeyDocument
        {
            Xml = xml,
            FriendlyName = friendlyName ?? string.Empty,
            CreatedAt = Timestamp.GetCurrentTimestamp()
        };

        try
        {
            _db.Collection(DataProtectionKeysCollection)
                .Document(docId)
                .SetAsync(payload)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist Data Protection key {FriendlyName}.", friendlyName);
            throw;
        }
    }

    [FirestoreData]
    private sealed class FirestoreDataProtectionKeyDocument
    {
        [FirestoreProperty]
        public string Xml { get; set; } = string.Empty;

        [FirestoreProperty]
        public string FriendlyName { get; set; } = string.Empty;

        [FirestoreProperty]
        public Timestamp CreatedAt { get; set; }
    }
}
