using MyBlog.Models;

namespace MyBlog.Services;

public interface IAislePilotPlanGenerationOrchestrator
{
    Task<AislePilotPlanResultViewModel> BuildPlanAsync(
        AislePilotService service,
        AislePilotRequestModel request,
        IReadOnlyList<string>? excludedMealNames = null,
        CancellationToken cancellationToken = default);
}
