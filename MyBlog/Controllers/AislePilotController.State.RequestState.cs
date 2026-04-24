using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;

namespace MyBlog.Controllers;

public partial class AislePilotController
{
    private string ResolveReturnUrl(string? returnUrl)
    {
        var fallbackReturnUrl = Url.Action("Index", "Projects") ?? "/projects";

        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            Url.IsLocalUrl(returnUrl) &&
            !IsAislePilotPath(returnUrl))
        {
            return returnUrl;
        }

        var referer = Request.Headers["Referer"].FirstOrDefault();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) &&
            string.Equals(refererUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            var candidate = $"{refererUri.AbsolutePath}{refererUri.Query}";
            if (Url.IsLocalUrl(candidate) && !IsAislePilotPath(candidate))
            {
                return candidate;
            }
        }

        return fallbackReturnUrl;
    }

    private string BuildAislePilotIndexUrl(string? returnUrl, string? hash = null, bool restoreCurrentPlan = false)
    {
        var indexUrl = Url.Action(nameof(Index), new { returnUrl, restoreCurrentPlan }) ?? "/projects/aisle-pilot";
        if (string.IsNullOrWhiteSpace(hash))
        {
            return indexUrl;
        }

        var normalizedHash = hash.StartsWith('#') ? hash : $"#{hash}";
        return $"{indexUrl}{normalizedHash}";
    }

    private static bool IsAislePilotPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var path = url.Split('?', '#')[0];
        return path.StartsWith("/projects/aisle-pilot", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string>? ResolveCurrentPlanMealNames(IReadOnlyList<string>? postedCurrentPlanMealNames)
    {
        var normalizedFromRequest = NormalizeCurrentPlanMealNames(postedCurrentPlanMealNames);
        if (normalizedFromRequest is not null && normalizedFromRequest.Count > 0)
        {
            return normalizedFromRequest;
        }

        return TryReadCurrentPlanState();
    }

    private static IReadOnlyList<string>? NormalizeCurrentPlanMealNames(IReadOnlyList<string>? mealNames)
    {
        if (mealNames is null || mealNames.Count == 0)
        {
            return null;
        }

        var normalized = mealNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        return normalized.Count > 0 ? normalized : null;
    }

    private bool IsAjaxRequest()
    {
        return string.Equals(
            Request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
    }

    private AislePilotRequestModel NormalizeRequest(AislePilotRequestModel? request)
    {
        var normalized = request ?? new AislePilotRequestModel();
        var selectedMealTypesWereSubmitted = Request.HasFormContentType &&
                                             Request.Form.ContainsKey("Request.SelectedMealTypes");
        normalized.Supermarket = normalized.Supermarket?.Trim() ?? string.Empty;
        normalized.PortionSize = normalized.PortionSize?.Trim() ?? string.Empty;
        normalized.CustomAisleOrder = normalized.CustomAisleOrder?.Trim() ?? string.Empty;
        normalized.DislikesOrAllergens = normalized.DislikesOrAllergens?.Trim() ?? string.Empty;
        normalized.PantryItems = normalized.PantryItems?.Trim() ?? string.Empty;
        normalized.SelectedDessertAddOnName = normalized.SelectedDessertAddOnName?.Trim() ?? string.Empty;
        normalized.LeftoverCookDayIndexesCsv = normalized.LeftoverCookDayIndexesCsv?.Trim() ?? string.Empty;
        normalized.SwapHistoryState = normalized.SwapHistoryState?.Trim() ?? string.Empty;
        normalized.IgnoredMealSlotIndexesCsv =
            NormalizeIgnoredMealSlotIndexesCsv(normalized.IgnoredMealSlotIndexesCsv);
        normalized.PantrySuggestionHistoryState = normalized.PantrySuggestionHistoryState?.Trim() ?? string.Empty;
        normalized.SavedEnjoyedMealNamesState = SerializeSavedEnjoyedMealNameState(
            ParseSavedEnjoyedMealNamesState(normalized.SavedEnjoyedMealNamesState));
        normalized.DietaryModes = normalized.DietaryModes?
            .Where(mode => !string.IsNullOrWhiteSpace(mode))
            .Select(mode => mode.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        normalized.SavedMealRepeatRatePercent = Math.Clamp(normalized.SavedMealRepeatRatePercent, 10, 100);
        normalized.PlanDays = Math.Clamp(normalized.PlanDays, 1, 7);
        var maxLeftoverDays = Math.Max(0, normalized.PlanDays - 1);
        var normalizedLeftoverSourceDays = ParseLeftoverSourceDayIndexes(
            normalized.LeftoverCookDayIndexesCsv,
            normalized.PlanDays,
            maxLeftoverDays);
        normalized.LeftoverCookDayIndexesCsv = string.Join(",", normalizedLeftoverSourceDays);
        normalized.CookDays = Math.Clamp(
            normalized.PlanDays - normalizedLeftoverSourceDays.Count,
            1,
            normalized.PlanDays);
        if (normalized.SelectedSpecialTreatCookDayIndex.HasValue)
        {
            normalized.SelectedSpecialTreatCookDayIndex = Math.Clamp(
                normalized.SelectedSpecialTreatCookDayIndex.Value,
                0,
                normalized.CookDays - 1);
        }

        if (normalized.IncludeSpecialTreatMeal && !normalized.SelectedSpecialTreatCookDayIndex.HasValue)
        {
            normalized.SelectedSpecialTreatCookDayIndex = 0;
        }

        var normalizedSelectedMealTypes = NormalizeSelectedMealTypes(normalized.SelectedMealTypes);
        if (normalizedSelectedMealTypes.Count == 0 && !selectedMealTypesWereSubmitted)
        {
            normalizedSelectedMealTypes = BuildMealTypeSlotsFromMealsPerDay(normalized.MealsPerDay).ToList();
        }

        normalized.SelectedMealTypes = normalizedSelectedMealTypes;
        normalized.MealsPerDay = normalizedSelectedMealTypes.Count > 0
            ? normalizedSelectedMealTypes.Count
            : Math.Clamp(normalized.MealsPerDay, MinMealsPerDay, MaxMealsPerDay);
        return normalized;
    }

    private static List<string> NormalizeSelectedMealTypes(IReadOnlyList<string>? selectedMealTypes)
    {
        if (selectedMealTypes is null || selectedMealTypes.Count == 0)
        {
            return [];
        }

        return selectedMealTypes
            .Select(NormalizeSelectedMealType)
            .Where(type => type is not null)
            .Select(type => type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => Array.IndexOf(MealTypeSlotOrder, type))
            .ToList();
    }

    private static string? NormalizeSelectedMealType(string? mealType)
    {
        var normalized = mealType?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
        {
            return "Breakfast";
        }

        if (normalized.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "Lunch";
        }

        if (normalized.Equals("Dinner", StringComparison.OrdinalIgnoreCase))
        {
            return "Dinner";
        }

        return null;
    }

    private static IReadOnlyList<string> BuildMealTypeSlotsFromMealsPerDay(int mealsPerDay)
    {
        var safeMealsPerDay = Math.Clamp(mealsPerDay, MinMealsPerDay, MaxMealsPerDay);
        return safeMealsPerDay switch
        {
            1 => ["Dinner"],
            2 => ["Lunch", "Dinner"],
            _ => ["Breakfast", "Lunch", "Dinner"]
        };
    }

    private static IReadOnlyList<string> GetSeenMealsForSwap(string? swapHistoryState, string? currentMealName)
    {
        var history = ParseSwapHistoryState(swapHistoryState);
        var seenMeals = history.Values
            .SelectMany(static meals => meals)
            .Where(meal => !string.IsNullOrWhiteSpace(meal))
            .Select(meal => meal.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(currentMealName))
        {
            return seenMeals;
        }

        if (!seenMeals.Contains(currentMealName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            seenMeals.Add(currentMealName.Trim());
        }

        return seenMeals;
    }

    private static string UpdateSwapHistoryState(
        string? currentState,
        int dayIndex,
        string? previousMealName,
        string? nextMealName)
    {
        var history = ParseSwapHistoryState(currentState);
        if (!history.TryGetValue(dayIndex, out var meals))
        {
            meals = [];
            history[dayIndex] = meals;
        }

        if (!string.IsNullOrWhiteSpace(previousMealName) &&
            !meals.Contains(previousMealName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            meals.Add(previousMealName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(nextMealName) &&
            !meals.Contains(nextMealName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            meals.Add(nextMealName.Trim());
        }

        return SerializeSwapHistoryState(history);
    }

    private static string NormalizeIgnoredMealSlotIndexesCsv(string? ignoredMealSlotIndexesCsv)
    {
        var ignoredIndexes = ParseIgnoredMealSlotIndexes(
            ignoredMealSlotIndexesCsv,
            maxAllowedIndex: MaxIgnoredMealSlotIndex);
        return SerializeIgnoredMealSlotIndexes(ignoredIndexes);
    }

    private static string ToggleIgnoredMealSlotIndex(
        string? currentState,
        int dayIndex,
        int mealSlotCount)
    {
        var maxIndex = Math.Clamp(mealSlotCount - 1, 0, MaxIgnoredMealSlotIndex);
        var ignoredIndexes = ParseIgnoredMealSlotIndexes(currentState, maxIndex);
        if (ignoredIndexes.Contains(dayIndex))
        {
            ignoredIndexes.Remove(dayIndex);
        }
        else
        {
            ignoredIndexes.Add(dayIndex);
        }

        return SerializeIgnoredMealSlotIndexes(ignoredIndexes);
    }

    private static HashSet<int> ParseIgnoredMealSlotIndexes(
        string? ignoredMealSlotIndexesCsv,
        int maxAllowedIndex)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(ignoredMealSlotIndexesCsv))
        {
            return result;
        }

        foreach (var token in ignoredMealSlotIndexesCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, out var slotIndex) ||
                slotIndex < 0 ||
                slotIndex > maxAllowedIndex)
            {
                continue;
            }

            result.Add(slotIndex);
        }

        return result;
    }

    private static List<int> ParseLeftoverSourceDayIndexes(
        string? leftoverCookDayIndexesCsv,
        int planDays,
        int maxCount)
    {
        var normalizedPlanDays = Math.Clamp(planDays, 1, 7);
        var normalizedMaxCount = Math.Max(0, maxCount);
        if (normalizedMaxCount == 0 || string.IsNullOrWhiteSpace(leftoverCookDayIndexesCsv))
        {
            return [];
        }

        var result = new List<int>(normalizedMaxCount);
        var tokens = leftoverCookDayIndexesCsv
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (result.Count >= normalizedMaxCount)
            {
                break;
            }

            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dayIndex))
            {
                continue;
            }

            if (dayIndex < 0 || dayIndex >= normalizedPlanDays)
            {
                continue;
            }

            result.Add(dayIndex);
        }

        return result;
    }

    private static string SerializeIgnoredMealSlotIndexes(IEnumerable<int> ignoredIndexes)
    {
        return string.Join(
            ",",
            ignoredIndexes
                .Distinct()
                .Where(index => index >= 0 && index <= MaxIgnoredMealSlotIndex)
                .OrderBy(index => index));
    }

    private static Dictionary<int, List<string>> ParseSwapHistoryState(string? swapHistoryState)
    {
        var result = new Dictionary<int, List<string>>();
        if (string.IsNullOrWhiteSpace(swapHistoryState))
        {
            return result;
        }

        var dayEntries = swapHistoryState.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in dayEntries)
        {
            var segments = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (segments.Length != 2 ||
                !int.TryParse(segments[0], out var dayIndex) ||
                dayIndex < 0 ||
                dayIndex > MaxSwapMealSlotIndex)
            {
                continue;
            }

            var meals = segments[1]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(meal => !string.IsNullOrWhiteSpace(meal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (meals.Count > 0)
            {
                result[dayIndex] = meals;
            }
        }

        return result;
    }

    private static string SerializeSwapHistoryState(Dictionary<int, List<string>> history)
    {
        return string.Join(
            ';',
            history
                .OrderBy(pair => pair.Key)
                .Where(pair => pair.Value.Count > 0)
                .Select(pair => $"{pair.Key}:{string.Join('|', pair.Value.Distinct(StringComparer.OrdinalIgnoreCase))}"));
    }
}
