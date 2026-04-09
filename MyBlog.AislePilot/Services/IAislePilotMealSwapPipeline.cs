using MyBlog.Models;

namespace MyBlog.Services;

public interface IAislePilotMealSwapPipeline
{
    Task<AislePilotPlanResultViewModel> SwapMealForDayAsync(
        AislePilotService service,
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames,
        CancellationToken cancellationToken = default);
}
