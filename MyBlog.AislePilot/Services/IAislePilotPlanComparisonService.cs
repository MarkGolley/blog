using MyBlog.Models;

namespace MyBlog.Services;

public interface IAislePilotPlanComparisonService
{
    bool HasSameMealSequence(
        AislePilotPlanResultViewModel plan,
        IReadOnlyList<string> expectedMealNames);

    int CountChangedMealDays(
        AislePilotPlanResultViewModel plan,
        IReadOnlyList<string> baselineMealNames);
}
