using MyBlog.Models;

namespace MyBlog.Services;

public interface IAislePilotService
{
    IReadOnlyList<string> GetSupportedSupermarkets();
    IReadOnlyList<string> GetSupportedPortionSizes();
    IReadOnlyList<string> GetSupportedDietaryModes();
    bool CanGenerateMealImages();
    bool HasCompatibleMeals(AislePilotRequestModel request);
    IReadOnlyList<AislePilotPantrySuggestionViewModel> SuggestMealsFromPantry(
        AislePilotRequestModel request,
        int maxResults = 5,
        IReadOnlyList<string>? excludedMealNames = null,
        string? generationNonce = null);
    AislePilotPlanResultViewModel BuildPlan(AislePilotRequestModel request);
    Task<AislePilotPlanResultViewModel> BuildPlanAsync(
        AislePilotRequestModel request,
        CancellationToken cancellationToken = default);
    Task<AislePilotPlanResultViewModel> BuildPlanAvoidingMealsAsync(
        AislePilotRequestModel request,
        IReadOnlyList<string> excludedMealNames,
        CancellationToken cancellationToken = default);
    Task<AislePilotPlanResultViewModel> BuildPlanFromCurrentMealsAsync(
        AislePilotRequestModel request,
        IReadOnlyList<string> currentPlanMealNames,
        CancellationToken cancellationToken = default);
    AislePilotPlanResultViewModel BuildPlanWithBudgetRebalance(
        AislePilotRequestModel request,
        int maxAttempts = 4,
        IReadOnlyList<string>? currentPlanMealNames = null);
    Task<AislePilotPlanResultViewModel> BuildPlanWithBudgetRebalanceAsync(
        AislePilotRequestModel request,
        int maxAttempts = 4,
        IReadOnlyList<string>? currentPlanMealNames = null,
        CancellationToken cancellationToken = default);
    AislePilotPlanResultViewModel SwapMealForDay(
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames);
    Task<AislePilotPlanResultViewModel> SwapMealForDayAsync(
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames,
        CancellationToken cancellationToken = default);
    string ResolveNextDessertAddOnName(string? currentDessertAddOnName);
    Task<IReadOnlyDictionary<string, string>> GetMealImageUrlsAsync(
        IReadOnlyList<string> mealNames,
        CancellationToken cancellationToken = default);
    Task<AislePilotWarmupResult> WarmupAiMealPoolAsync(
        int minPerSingleMode = 8,
        int minPerKeyPair = 6,
        int maxMealsToGenerate = 2,
        CancellationToken cancellationToken = default);
}
