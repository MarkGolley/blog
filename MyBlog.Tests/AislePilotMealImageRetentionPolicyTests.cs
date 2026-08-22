using MyBlog.Services;

namespace MyBlog.Tests;

public sealed class AislePilotMealImageRetentionPolicyTests
{
    [Fact]
    public void SelectSupersededDiskFiles_DeletesOnlyOldUnprotectedVersionsAndKeepsNewest()
    {
        var nowUtc = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var files = new[]
        {
            Entry("meal-1111111111111111.jpg", nowUtc.AddDays(-50)),
            Entry("meal-2222222222222222.jpg", nowUtc.AddDays(-40)),
            Entry("meal-3333333333333333.jpg", nowUtc.AddDays(-2)),
            Entry("meal-1111111111111111.webp", nowUtc.AddDays(-45)),
            Entry("protected-1111111111111111.jpg", nowUtc.AddDays(-60)),
            Entry("protected-2222222222222222.jpg", nowUtc.AddDays(-35)),
            Entry("protected-3333333333333333.jpg", nowUtc.AddDays(-1)),
            Entry("legacy-meal.jpg", nowUtc.AddDays(-500))
        };
        IReadOnlySet<string> protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "protected-1111111111111111.jpg"
        };

        var selected = AislePilotMealImageRetentionPolicy.SelectSupersededDiskFiles(
            files,
            protectedFiles,
            nowUtc.AddDays(-30),
            maximumDeletes: 100);

        Assert.Equal(
            ["meal-1111111111111111.jpg", "meal-2222222222222222.jpg", "protected-2222222222222222.jpg"],
            selected.Select(entry => Path.GetFileName(entry.FullPath)).ToArray());
    }

    [Fact]
    public void SelectSupersededDiskFiles_EnforcesDeletionBatchLimit()
    {
        var nowUtc = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        var files = Enumerable.Range(0, 5)
            .Select(index => Entry($"meal-{index:x16}.jpg", nowUtc.AddDays(-50 + index)))
            .ToArray();

        var selected = AislePilotMealImageRetentionPolicy.SelectSupersededDiskFiles(
            files,
            new HashSet<string>(),
            nowUtc.AddDays(-30),
            maximumDeletes: 2);

        Assert.Equal(2, selected.Count);
    }

    [Fact]
    public void ShouldDeleteFirestoreRecord_RequiresAgeAndUnprotectedMealName()
    {
        var nowUtc = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);
        IReadOnlySet<string> protectedMealNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Known meal"
        };

        Assert.True(AislePilotMealImageRetentionPolicy.ShouldDeleteFirestoreRecord(
            "Expired meal", nowUtc.AddDays(-366), protectedMealNames, nowUtc.AddDays(-365)));
        Assert.False(AislePilotMealImageRetentionPolicy.ShouldDeleteFirestoreRecord(
            "Known meal", nowUtc.AddDays(-500), protectedMealNames, nowUtc.AddDays(-365)));
        Assert.False(AislePilotMealImageRetentionPolicy.ShouldDeleteFirestoreRecord(
            "Recent meal", nowUtc.AddDays(-10), protectedMealNames, nowUtc.AddDays(-365)));
        Assert.False(AislePilotMealImageRetentionPolicy.ShouldDeleteFirestoreRecord(
            "Expired meal", default, protectedMealNames, nowUtc.AddDays(-365)));
    }

    private static AislePilotMealImageDiskEntry Entry(string fileName, DateTime lastWriteUtc)
    {
        return new AislePilotMealImageDiskEntry(Path.Combine("images", "aislepilot-meals", fileName), lastWriteUtc);
    }
}
