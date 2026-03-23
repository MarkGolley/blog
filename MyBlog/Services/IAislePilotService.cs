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
    AislePilotPlanResultViewModel BuildPlanWithBudgetRebalance(
        AislePilotRequestModel request,
        int maxAttempts = 4,
        IReadOnlyList<string>? currentPlanMealNames = null);
    AislePilotPlanResultViewModel SwapMealForDay(
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames);
    Task<IReadOnlyDictionary<string, string>> GetMealImageUrlsAsync(
        IReadOnlyList<string> mealNames,
        CancellationToken cancellationToken = default);
    Task<AislePilotWarmupResult> WarmupAiMealPoolAsync(
        int minPerSingleMode = 8,
        int minPerKeyPair = 6,
        int maxMealsToGenerate = 2,
        CancellationToken cancellationToken = default);
}
