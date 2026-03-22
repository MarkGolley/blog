using MyBlog.Models;

namespace MyBlog.Services;

public interface IAislePilotService
{
    IReadOnlyList<string> GetSupportedSupermarkets();
    IReadOnlyList<string> GetSupportedPortionSizes();
    IReadOnlyList<string> GetSupportedDietaryModes();
    bool HasCompatibleMeals(AislePilotRequestModel request);
    IReadOnlyList<AislePilotPantrySuggestionViewModel> SuggestMealsFromPantry(
        AislePilotRequestModel request,
        int maxResults = 5);
    AislePilotPlanResultViewModel BuildPlan(AislePilotRequestModel request);
    AislePilotPlanResultViewModel SwapMealForDay(
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames);
    Task<AislePilotWarmupResult> WarmupAiMealPoolAsync(
        int minPerSingleMode = 8,
        int minPerKeyPair = 6,
        int maxMealsToGenerate = 2,
        CancellationToken cancellationToken = default);
}
