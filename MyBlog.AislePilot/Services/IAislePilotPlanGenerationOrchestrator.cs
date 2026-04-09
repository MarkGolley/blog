using MyBlog.Models;

namespace MyBlog.Services;

public interface IAislePilotPlanGenerationOrchestrator
{
    Task<AislePilotPlanResultViewModel> BuildPlanAsync(
        AislePilotService service,
        AislePilotRequestModel request,
        CancellationToken cancellationToken = default);
}
