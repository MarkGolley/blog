using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBlog.Models;
using MyBlog.Utilities;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private static IReadOnlyList<string> BuildBudgetTips(bool isOverBudget, decimal budgetDelta, int leftoverDays)
    {
        var ukCulture = CultureInfo.GetCultureInfo("en-GB");
        var tips = new List<string>();

        if (leftoverDays > 0)
        {
            tips.Add($"{leftoverDays} day(s) are allocated to leftovers this week.");
        }

        if (isOverBudget)
        {
            var overspend = Math.Abs(budgetDelta);
            tips.Add($"Current plan is about {overspend.ToString("C", ukCulture)} over your target.");
            tips.Add("Swap 1-2 high-cost fish/meat meals for lentil or chickpea meals.");
            tips.Add("Batch-cook one recipe and reuse leftovers for lunch.");
            return tips;
        }

        if (budgetDelta >= 10m)
        {
            tips.Add($"You still have about {budgetDelta.ToString("C", ukCulture)} available.");
            tips.Add("Consider adding breakfast staples or healthy snacks.");
            tips.Add("Use the spare budget for higher-quality proteins or produce.");
            return tips;
        }

        tips.Add("Budget is on target.");
        tips.Add("If prices shift, swap one meal to keep the weekly total stable.");
        return tips;
    }

    internal static int NormalizePlanDays(int planDays)
    {
        return Math.Clamp(planDays, 1, 7);
    }

    internal static int NormalizeCookDays(int cookDays)
    {
        return NormalizeCookDays(cookDays, 7);
    }

    internal static int NormalizeCookDays(int cookDays, int planDays)
    {
        var normalizedPlanDays = NormalizePlanDays(planDays);
        return Math.Clamp(cookDays, 1, normalizedPlanDays);
    }

    private static int NormalizeMealsPerDay(int mealsPerDay)
    {
        return Math.Clamp(mealsPerDay, MinMealsPerDay, MaxMealsPerDay);
    }

    internal static int NormalizeRequestedMealCount(int requestedMealCount)
    {
        return Math.Clamp(requestedMealCount, 1, MaxPlanMealSlots);
    }

    private static IReadOnlySet<int> ParseIgnoredMealSlotIndexes(
        string? ignoredMealSlotIndexesCsv,
        int expectedMealCount)
    {
        if (string.IsNullOrWhiteSpace(ignoredMealSlotIndexesCsv))
        {
            return new HashSet<int>();
        }

        var normalizedExpectedCount = NormalizeRequestedMealCount(expectedMealCount);
        var parsed = ignoredMealSlotIndexesCsv
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                ? index
                : -1)
            .Where(index => index >= 0 && index < normalizedExpectedCount)
            .Distinct()
            .ToHashSet();

        return parsed;
    }

    internal static IReadOnlyList<string> BuildMealTypeSlots(AislePilotRequestModel request)
    {
        return NormalizeMealTypeSlots(request.SelectedMealTypes, request.MealsPerDay);
    }

    private static IReadOnlyList<string> BuildMealTypeSlots(int mealsPerDay)
    {
        var safeMealsPerDay = NormalizeMealsPerDay(mealsPerDay);
        return safeMealsPerDay switch
        {
            1 => ["Dinner"],
            2 => ["Lunch", "Dinner"],
            _ => ["Breakfast", "Lunch", "Dinner"]
        };
    }

    internal static IReadOnlyList<string> NormalizeMealTypeSlots(
        IReadOnlyList<string>? mealTypeSlots,
        int fallbackMealsPerDay)
    {
        if (mealTypeSlots is null || mealTypeSlots.Count == 0)
        {
            return BuildMealTypeSlots(fallbackMealsPerDay);
        }

        var normalized = mealTypeSlots
            .Select(NormalizeSelectedMealTypeSlot)
            .Where(type => type is not null)
            .Select(type => type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type.Equals("Breakfast", StringComparison.OrdinalIgnoreCase)
                ? 0
                : type.Equals("Lunch", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 2)
            .ToList();

        return normalized.Count == 0 ? BuildMealTypeSlots(fallbackMealsPerDay) : normalized;
    }

    private static string? NormalizeSelectedMealTypeSlot(string? mealType)
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

    private static string NormalizeMealType(string? mealType)
    {
        var normalized = (mealType ?? string.Empty).Trim();
        if (normalized.Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
        {
            return "Breakfast";
        }

        if (normalized.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "Lunch";
        }

        return "Dinner";
    }

    private static IReadOnlyList<string> NormalizeMealTypes(IReadOnlyList<string>? mealTypes)
    {
        if (mealTypes is null || mealTypes.Count == 0)
        {
            return ["Dinner"];
        }

        var normalized = mealTypes
            .Select(NormalizeMealType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? ["Dinner"] : normalized;
    }

    private static IReadOnlyList<string> InferSuitableMealTypesFromMealName(string mealName)
    {
        var normalizedName = mealName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return ["Dinner"];
        }

        var isBreakfastLike = IsBreakfastLikeMealName(normalizedName);
        var isLunchLike = IsLunchLikeMealName(normalizedName);

        var mealTypes = new List<string>(3);
        if (isBreakfastLike)
        {
            mealTypes.Add("Breakfast");
            mealTypes.Add("Lunch");
        }

        if (isLunchLike)
        {
            mealTypes.Add("Lunch");
        }

        if (!isBreakfastLike && !isLunchLike)
        {
            mealTypes.Add("Dinner");
        }

        return mealTypes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ResolveSuitableMealTypes(MealTemplate meal)
    {
        if (meal.SuitableMealTypes is { Count: > 0 })
        {
            return NormalizeMealTypes(meal.SuitableMealTypes);
        }

        return InferSuitableMealTypesFromMealName(meal.Name);
    }

    internal static MealTemplate EnsureMealTypeSuitability(
        MealTemplate meal,
        IReadOnlyList<string>? preferredMealTypes = null)
    {
        var normalizedPreferredMealTypes = preferredMealTypes is { Count: > 0 }
            ? NormalizeMealTypes(preferredMealTypes)
            : ResolveSuitableMealTypes(meal);
        var currentMealTypes = NormalizeMealTypes(meal.SuitableMealTypes);

        return currentMealTypes.SequenceEqual(normalizedPreferredMealTypes, StringComparer.OrdinalIgnoreCase)
            ? meal
            : meal with { SuitableMealTypes = normalizedPreferredMealTypes };
    }

    internal static bool SupportsMealType(MealTemplate meal, string mealType)
    {
        var normalizedMealType = NormalizeMealType(mealType);
        return ResolveSuitableMealTypes(meal).Contains(normalizedMealType, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBreakfastLikeMealName(string mealName)
    {
        var normalizedName = mealName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        return BreakfastNameKeywords.Any(keyword =>
            normalizedName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLunchLikeMealName(string mealName)
    {
        var normalizedName = mealName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        return LunchNameKeywords.Any(keyword =>
            normalizedName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMealNameAppropriateForSlot(string mealName, string mealType)
    {
        var normalizedMealType = NormalizeMealType(mealType);
        var isBreakfastLike = IsBreakfastLikeMealName(mealName);
        var isLunchLike = IsLunchLikeMealName(mealName);

        return normalizedMealType switch
        {
            "Breakfast" => isBreakfastLike,
            "Lunch" => isLunchLike || isBreakfastLike,
            _ => true
        };
    }

    private static IReadOnlyList<int> BuildPerMealPortionMultipliers(
        IReadOnlyList<int> dayPortionMultipliers,
        int mealsPerDay)
    {
        if (dayPortionMultipliers.Count == 0)
        {
            return [];
        }

        var safeMealsPerDay = NormalizeMealsPerDay(mealsPerDay);
        var perMeal = new List<int>(dayPortionMultipliers.Count * safeMealsPerDay);
        foreach (var dayMultiplier in dayPortionMultipliers)
        {
            var normalizedDayMultiplier = Math.Max(1, dayMultiplier);
            for (var i = 0; i < safeMealsPerDay; i++)
            {
                perMeal.Add(normalizedDayMultiplier);
            }
        }

        return perMeal;
    }

    private static IReadOnlyList<MealTemplate> NormalizeSelectedMealsForCount(
        IReadOnlyList<MealTemplate> selectedMeals,
        int expectedMealCount)
    {
        var normalizedExpectedCount = NormalizeRequestedMealCount(expectedMealCount);
        if (selectedMeals.Count == normalizedExpectedCount)
        {
            return selectedMeals;
        }

        if (selectedMeals.Count > normalizedExpectedCount)
        {
            return selectedMeals.Take(normalizedExpectedCount).ToList();
        }

        if (selectedMeals.Count == 0)
        {
            return [];
        }

        var normalized = new List<MealTemplate>(normalizedExpectedCount);
        for (var i = 0; i < normalizedExpectedCount; i++)
        {
            normalized.Add(selectedMeals[i % selectedMeals.Count]);
        }

        return normalized;
    }

    private static int RoundToNearestFiveMinutes(int minutes)
    {
        var safeMinutes = Math.Max(5, minutes);
        return (int)(Math.Round(safeMinutes / 5m, MidpointRounding.AwayFromZero) * 5m);
    }

}
