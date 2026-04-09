using MyBlog.Models;

namespace MyBlog.Services;

public sealed class AislePilotPantryRankingEngine
{
    internal IReadOnlyList<string> ParsePantryTokens(string? pantryItems)
    {
        return AislePilotService.ParsePantryTokens(pantryItems);
    }

    internal IReadOnlyList<string> MergePantryTokensWithAssumedBasics(IReadOnlyList<string> pantryTokens)
    {
        return AislePilotService.MergePantryTokensWithAssumedBasics(pantryTokens);
    }

    internal IReadOnlyList<string> ParseSpecificPantryTokens(IReadOnlyList<string> pantryTokens)
    {
        return AislePilotService.ParseSpecificPantryTokens(pantryTokens);
    }

    internal AislePilotPantrySuggestionViewModel BuildPantrySuggestion(
        AislePilotService.MealTemplate template,
        IReadOnlyList<string> pantryTokens,
        decimal householdFactor)
    {
        return AislePilotService.BuildPantrySuggestion(template, pantryTokens, householdFactor);
    }

    internal int CountMatchedPantryTokens(
        AislePilotService.MealTemplate template,
        IReadOnlyList<string> pantryTokens)
    {
        return AislePilotService.CountMatchedPantryTokens(template, pantryTokens);
    }

    internal int ComputePantrySuggestionScore(
        AislePilotPantrySuggestionViewModel suggestion,
        AislePilotPantrySuggestionViewModel userOnlySuggestion,
        int userMatchedTokenCount,
        int specificMatchedTokenCount,
        int specificTokenCount)
    {
        return AislePilotService.ComputePantrySuggestionScore(
            suggestion,
            userOnlySuggestion,
            userMatchedTokenCount,
            specificMatchedTokenCount,
            specificTokenCount);
    }

    internal IReadOnlyList<AislePilotService.PantrySuggestionCandidate> RankPantrySuggestionCandidates(
        IReadOnlyList<AislePilotService.PantrySuggestionCandidate> candidates,
        int targetCount,
        bool allowVariation)
    {
        return AislePilotService.RankPantrySuggestionCandidates(candidates, targetCount, allowVariation);
    }

    internal bool TemplateUsesCoreIngredientsFromUserPantry(
        AislePilotService.MealTemplate template,
        IReadOnlyList<string> userPantryTokens)
    {
        return AislePilotService.TemplateUsesCoreIngredientsFromUserPantry(template, userPantryTokens);
    }
}
