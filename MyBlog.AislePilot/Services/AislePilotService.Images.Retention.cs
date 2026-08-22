using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private async Task CleanupMealImagesIfDueAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        if (_lastMealImageCleanupUtc is { } lastCleanupUtc && nowUtc - lastCleanupUtc < _mealImageCleanupInterval)
        {
            return;
        }

        if (!await MealImageCleanupLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            nowUtc = DateTime.UtcNow;
            if (_lastMealImageCleanupUtc is { } refreshedLastCleanupUtc &&
                nowUtc - refreshedLastCleanupUtc < _mealImageCleanupInterval)
            {
                return;
            }

            var firestoreRecords = await ReadMealImageRetentionRecordsAsync(cancellationToken);
            var protectedMealNames = BuildProtectedMealImageNames();
            var firestoreDeleteBeforeUtc = nowUtc - _mealImageFirestoreRetention;
            var staleRecords = firestoreRecords
                .Where(record => AislePilotMealImageRetentionPolicy.ShouldDeleteFirestoreRecord(
                    record.Image.Name,
                    record.Image.UpdatedAtUtc,
                    protectedMealNames,
                    firestoreDeleteBeforeUtc))
                .OrderBy(record => record.Image.UpdatedAtUtc)
                .Take(_mealImageCleanupMaximumDeletes)
                .ToList();

            foreach (var record in staleRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteMealImageChunksAsync(record.Document.Reference, cancellationToken);
                await record.Document.Reference.DeleteAsync(cancellationToken: cancellationToken);
                MealImagePool.TryRemove(record.Image.Name, out _);
            }

            var staleDocumentIds = staleRecords
                .Select(record => record.Document.Id)
                .ToHashSet(StringComparer.Ordinal);
            var protectedFileNames = firestoreRecords
                .Where(record => !staleDocumentIds.Contains(record.Document.Id))
                .Select(record => TryGetMealImageFileName(record.Image.ImageUrl))
                .Concat(MealImagePool.Values.Select(TryGetMealImageFileName))
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .Select(fileName => fileName!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var deletedDiskFiles = DeleteSupersededMealImageFiles(
                protectedFileNames,
                nowUtc - _mealImageDiskRetention,
                _mealImageCleanupMaximumDeletes,
                cancellationToken);

            _lastMealImageCleanupUtc = nowUtc;
            _logger?.LogInformation(
                "AislePilot meal-image retention completed. FirestoreRecordsDeleted={FirestoreRecordsDeleted}, DiskFilesDeleted={DiskFilesDeleted}",
                staleRecords.Count,
                deletedDiskFiles);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AislePilot meal-image retention failed.");
        }
        finally
        {
            MealImageCleanupLock.Release();
        }
    }

    private async Task<IReadOnlyList<MealImageRetentionRecord>> ReadMealImageRetentionRecordsAsync(
        CancellationToken cancellationToken)
    {
        if (_db is null)
        {
            return [];
        }

        var snapshot = await _db.Collection(MealImagesCollection)
            .Limit(1000)
            .GetSnapshotAsync(cancellationToken);
        var records = new List<MealImageRetentionRecord>(snapshot.Count);
        foreach (var document in snapshot.Documents)
        {
            try
            {
                records.Add(new MealImageRetentionRecord(document, document.ConvertTo<FirestoreAislePilotMealImage>()));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Skipping malformed AislePilot meal-image record '{DocumentId}' during retention.", document.Id);
            }
        }

        return records;
    }

    private static HashSet<string> BuildProtectedMealImageNames()
    {
        return MealTemplates.Select(meal => meal.Name)
            .Concat(AiMealPool.Keys)
            .Concat(DessertAddOnPool.Keys)
            .Concat(MealImageGenerationInFlight.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private int DeleteSupersededMealImageFiles(
        IReadOnlySet<string> protectedFileNames,
        DateTime deleteBeforeUtc,
        int maximumDeletes,
        CancellationToken cancellationToken)
    {
        if (_webHostEnvironment is null || string.IsNullOrWhiteSpace(_webHostEnvironment.WebRootPath))
        {
            return 0;
        }

        var directoryPath = Path.Combine(_webHostEnvironment.WebRootPath, "images", "aislepilot-meals");
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        var files = Directory.EnumerateFiles(directoryPath)
            .Select(path => new AislePilotMealImageDiskEntry(path, File.GetLastWriteTimeUtc(path)));
        var candidates = AislePilotMealImageRetentionPolicy.SelectSupersededDiskFiles(
            files,
            protectedFileNames,
            deleteBeforeUtc,
            maximumDeletes);
        var deletedCount = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(candidate.FullPath);
                deletedCount++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Could not delete superseded AislePilot meal-image file '{FileName}'.", Path.GetFileName(candidate.FullPath));
            }
        }

        return deletedCount;
    }

    private static string? TryGetMealImageFileName(string? imageUrl)
    {
        var normalized = NormalizeImageUrl(imageUrl);
        var path = Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri.AbsolutePath
            : normalized.Split('?', '#')[0];
        return path.StartsWith("/images/aislepilot-meals/", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(path)
            : null;
    }

    private sealed record MealImageRetentionRecord(
        DocumentSnapshot Document,
        FirestoreAislePilotMealImage Image);
}
