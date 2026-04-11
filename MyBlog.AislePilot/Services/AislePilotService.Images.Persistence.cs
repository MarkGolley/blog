using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBlog.Models;
using MyBlog.Utilities;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private async Task<bool> TryRestoreMealImageFromBase64Async(
        string imageUrl,
        string imageBase64,
        CancellationToken cancellationToken)
    {
        if (!TryResolveMealImageDiskPath(imageUrl, out var fullPath))
        {
            return false;
        }

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(imageBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (imageBytes.Length == 0)
        {
            return false;
        }

        try
        {
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return false;
            }

            Directory.CreateDirectory(directoryPath);
            await File.WriteAllBytesAsync(fullPath, imageBytes, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to restore AislePilot meal image file for url '{ImageUrl}'.", imageUrl);
            return false;
        }
    }

    private async Task<byte[]?> TryReadMealImageBytesFromDiskAsync(
        string imageUrl,
        CancellationToken cancellationToken)
    {
        if (!TryResolveMealImageDiskPath(imageUrl, out var fullPath) || !File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            return bytes.Length == 0 ? null : bytes;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> TryReadMealImageBase64FromChunksAsync(
        string docId,
        int expectedChunkCount,
        CancellationToken cancellationToken)
    {
        if (_db is null || string.IsNullOrWhiteSpace(docId) || expectedChunkCount <= 0)
        {
            return null;
        }

        try
        {
            var chunkSnapshot = await _db.Collection(MealImagesCollection)
                .Document(docId)
                .Collection(MealImageChunksSubcollection)
                .GetSnapshotAsync(cancellationToken);
            if (chunkSnapshot.Documents.Count == 0)
            {
                return null;
            }

            var chunks = chunkSnapshot.Documents
                .Select(doc =>
                {
                    try
                    {
                        return doc.ConvertTo<FirestoreAislePilotMealImageChunk>();
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(chunk => chunk is not null && !string.IsNullOrWhiteSpace(chunk.Data))
                .OrderBy(chunk => chunk!.Index)
                .ToList();
            if (chunks.Count == 0)
            {
                return null;
            }

            var joined = string.Concat(chunks.Select(chunk => chunk!.Data));
            return string.IsNullOrWhiteSpace(joined) ? null : joined;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to read meal image backup chunks for doc '{DocId}'.", docId);
            return null;
        }
    }

    private async Task PersistMealImageChunksAsync(
        DocumentReference docRef,
        IReadOnlyList<string> chunks,
        CancellationToken cancellationToken)
    {
        if (_db is null)
        {
            return;
        }

        var chunkCollection = docRef.Collection(MealImageChunksSubcollection);
        const int batchSize = 450;
        for (var offset = 0; offset < chunks.Count; offset += batchSize)
        {
            var batch = _db.StartBatch();
            var count = Math.Min(batchSize, chunks.Count - offset);
            for (var i = 0; i < count; i++)
            {
                var chunkIndex = offset + i;
                var chunkDocId = chunkIndex.ToString("D4", CultureInfo.InvariantCulture);
                var chunkRef = chunkCollection.Document(chunkDocId);
                batch.Set(
                    chunkRef,
                    new FirestoreAislePilotMealImageChunk
                    {
                        Index = chunkIndex,
                        Data = chunks[chunkIndex]
                    });
            }

            await batch.CommitAsync(cancellationToken);
        }
    }

    private async Task DeleteMealImageChunksAsync(DocumentReference docRef, CancellationToken cancellationToken)
    {
        if (_db is null)
        {
            return;
        }

        try
        {
            var snapshot = await docRef.Collection(MealImageChunksSubcollection).GetSnapshotAsync(cancellationToken);
            if (snapshot.Documents.Count == 0)
            {
                return;
            }

            const int batchSize = 450;
            for (var offset = 0; offset < snapshot.Documents.Count; offset += batchSize)
            {
                var batch = _db.StartBatch();
                var count = Math.Min(batchSize, snapshot.Documents.Count - offset);
                for (var i = 0; i < count; i++)
                {
                    batch.Delete(snapshot.Documents[offset + i].Reference);
                }

                await batch.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed deleting meal image backup chunks for '{DocId}'.", docRef.Id);
        }
    }

    private async Task<string?> SaveMealImageToDiskAsync(
        string mealName,
        byte[] imageBytes,
        CancellationToken cancellationToken)
    {
        if (_webHostEnvironment is null || string.IsNullOrWhiteSpace(_webHostEnvironment.WebRootPath))
        {
            return null;
        }

        var directoryPath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "aislepilot-meals");
        Directory.CreateDirectory(directoryPath);

        var fileName = $"{ToAiMealDocumentId(mealName)}.png";
        var filePath = Path.Combine(directoryPath, fileName);
        await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken);
        return $"/images/aislepilot-meals/{fileName}";
    }

    private async Task PersistMealImageAsync(
        string mealName,
        string imageUrl,
        byte[]? imageBytes,
        CancellationToken cancellationToken)
    {
        if (_db is null || string.IsNullOrWhiteSpace(mealName) || string.IsNullOrWhiteSpace(imageUrl))
        {
            return;
        }

        try
        {
            var docRef = _db.Collection(MealImagesCollection).Document(ToAiMealDocumentId(mealName));
            var imageBase64 = string.Empty;
            var imageChunkCount = 0;
            if (imageBytes is { Length: > 0 and <= MaxMealImageBytesForFirestore })
            {
                imageBase64 = Convert.ToBase64String(imageBytes);
                await DeleteMealImageChunksAsync(docRef, cancellationToken);
            }
            else if (imageBytes is { Length: > MaxMealImageBytesForFirestore })
            {
                var oversizedBase64 = Convert.ToBase64String(imageBytes);
                var chunks = SplitBase64IntoChunks(oversizedBase64, MealImageChunkCharLength);
                imageChunkCount = chunks.Count;
                await DeleteMealImageChunksAsync(docRef, cancellationToken);
                await PersistMealImageChunksAsync(docRef, chunks, cancellationToken);
                _logger?.LogInformation(
                    "Persisted oversized meal image backup for '{MealName}' as {ChunkCount} Firestore chunks.",
                    mealName,
                    imageChunkCount);
            }

            await docRef.SetAsync(
                new FirestoreAislePilotMealImage
                {
                    Name = mealName,
                    ImageUrl = imageUrl,
                    ImageBase64 = imageBase64,
                    ImageChunkCount = imageChunkCount,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Source = "openai-image"
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "AislePilot failed to persist meal image for '{MealName}'.",
                mealName);
        }
    }

    private static string NormalizeImageUrl(string? imageUrl)
    {
        var normalized = imageUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        normalized = normalized.Replace('\\', '/');

        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        if (normalized.StartsWith("/projects/aisle-pilot/images/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/images/{normalized["/projects/aisle-pilot/images/".Length..]}";
        }

        if (normalized.StartsWith("/images/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/{normalized}";
        }

        if (normalized.StartsWith("aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
        {
            return $"/images/{normalized}";
        }

        var trimmed = normalized.TrimStart('/');
        var hasImageExtension =
            trimmed.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
        if (hasImageExtension && !trimmed.Contains('/'))
        {
            return $"/images/aislepilot-meals/{trimmed}";
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        return normalized;
    }

    private static IReadOnlyList<string> SplitBase64IntoChunks(string base64, int chunkLength)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return [];
        }

        var normalizedChunkLength = Math.Max(1, chunkLength);
        var chunks = new List<string>((base64.Length / normalizedChunkLength) + 1);
        for (var offset = 0; offset < base64.Length; offset += normalizedChunkLength)
        {
            var length = Math.Min(normalizedChunkLength, base64.Length - offset);
            chunks.Add(base64.Substring(offset, length));
        }

        return chunks;
    }

}
