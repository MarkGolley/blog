using MyBlog.Models;

namespace MyBlog.Services;

public interface IDailyCodingCapsuleProvider
{
    Task<DailyCodingCapsuleViewModel> GetCapsuleForCurrentDayAsync(CancellationToken cancellationToken = default);
    Task<DailyCodingCapsuleViewModel> GetCapsuleForOffsetDaysAsync(int offsetDays, CancellationToken cancellationToken = default);
    Task<DailyCodingCapsuleViewModel?> TryGetStoredCapsuleForOffsetDaysAsync(int offsetDays, CancellationToken cancellationToken = default);
}
