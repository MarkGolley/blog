using MyBlog.Models;

namespace MyBlog.Services;

public interface IAislePilotBudgetRebalancePipeline
{
    Task<AislePilotPlanResultViewModel> BuildPlanWithBudgetRebalanceAsync(
        AislePilotService service,
        AislePilotRequestModel request,
        int maxAttempts = 4,
        IReadOnlyList<string>? currentPlanMealNames = null,
        CancellationToken cancellationToken = default);
}
