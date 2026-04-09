using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

public partial class AislePilotController : Controller
{
    private IReadOnlyList<AislePilotSavedWeekCookieModel> PersistSavedWeekState(
        AislePilotRequestModel request,
        IReadOnlyList<string> currentPlanMealNames,
        string? weekLabel)
    {
        var normalizedMealNames = NormalizeCurrentPlanMealNames(currentPlanMealNames);
        if (normalizedMealNames is null || normalizedMealNames.Count == 0)
        {
            return [];
        }

        var snapshots = TryReadSavedWeekState().ToList();
        snapshots.RemoveAll(existing => AreEquivalentSelections(existing.CurrentPlanMealNames, normalizedMealNames));
        snapshots.Insert(0, new AislePilotSavedWeekCookieModel
        {
            WeekId = Guid.NewGuid().ToString("N")[..10],
            Label = BuildSavedWeekLabel(weekLabel),
            SavedAtUtc = DateTimeOffset.UtcNow,
            RequestSnapshot = BuildSavedWeekRequestSnapshot(request),
            CurrentPlanMealNames = normalizedMealNames.ToList()
        });

        if (snapshots.Count > MaxSavedWeeks)
        {
            snapshots = snapshots.Take(MaxSavedWeeks).ToList();
        }

        return PersistSavedWeekStateSnapshots(snapshots);
    }

    private IReadOnlyList<AislePilotSavedWeekCookieModel> PersistSavedWeekStateSnapshots(
        IReadOnlyList<AislePilotSavedWeekCookieModel> snapshots)
    {
        var normalizedSnapshots = snapshots
            .Select(NormalizeSavedWeekSnapshot)
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .Take(MaxSavedWeeks)
            .ToList();
        if (normalizedSnapshots.Count == 0)
        {
            Response.Cookies.Delete(SavedWeeksStateCookieName);
            return [];
        }

        while (normalizedSnapshots.Count > 0)
        {
            var payload = JsonSerializer.Serialize(normalizedSnapshots, SetupStateJsonOptions);
            if (payload.Length <= MaxSavedWeekCookiePayloadLength)
            {
                Response.Cookies.Append(
                    SavedWeeksStateCookieName,
                    payload,
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddDays(45),
                        IsEssential = true,
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = Request.IsHttps
                    });
                return normalizedSnapshots;
            }

            normalizedSnapshots.RemoveAt(normalizedSnapshots.Count - 1);
        }

        Response.Cookies.Delete(SavedWeeksStateCookieName);
        return [];
    }

    private IReadOnlyList<AislePilotSavedWeekCookieModel> TryReadSavedWeekState()
    {
        if (!Request.Cookies.TryGetValue(SavedWeeksStateCookieName, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<AislePilotSavedWeekCookieModel>>(payload, SetupStateJsonOptions) ?? [];
            var normalized = parsed
                .Select(NormalizeSavedWeekSnapshot)
                .Where(snapshot => snapshot is not null)
                .Select(snapshot => snapshot!)
                .OrderByDescending(snapshot => snapshot.SavedAtUtc)
                .ToList();
            return normalized;
        }
        catch
        {
            return [];
        }
    }

    private static AislePilotSavedWeekCookieModel? NormalizeSavedWeekSnapshot(AislePilotSavedWeekCookieModel? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var normalizedMealNames = NormalizeCurrentPlanMealNames(snapshot.CurrentPlanMealNames);
        if (normalizedMealNames is null || normalizedMealNames.Count == 0)
        {
            return null;
        }

        var normalizedWeekId = string.IsNullOrWhiteSpace(snapshot.WeekId)
            ? Guid.NewGuid().ToString("N")[..10]
            : snapshot.WeekId.Trim();
        var normalizedSavedAtUtc = snapshot.SavedAtUtc == default
            ? DateTimeOffset.UtcNow
            : snapshot.SavedAtUtc;

        var requestSnapshot = NormalizeSavedWeekRequestSnapshot(snapshot.RequestSnapshot);

        return new AislePilotSavedWeekCookieModel
        {
            WeekId = normalizedWeekId,
            Label = BuildSavedWeekLabel(snapshot.Label),
            SavedAtUtc = normalizedSavedAtUtc,
            RequestSnapshot = requestSnapshot,
            CurrentPlanMealNames = normalizedMealNames.ToList()
        };
    }

    private static AislePilotSavedWeekRequestCookieModel BuildSavedWeekRequestSnapshot(AislePilotRequestModel request)
    {
        return NormalizeSavedWeekRequestSnapshot(new AislePilotSavedWeekRequestCookieModel
        {
            Supermarket = request.Supermarket,
            WeeklyBudget = request.WeeklyBudget,
            HouseholdSize = request.HouseholdSize,
            CookDays = request.CookDays,
            PlanDays = request.PlanDays,
            MealsPerDay = request.MealsPerDay,
            SelectedMealTypes = request.SelectedMealTypes.ToList(),
            PortionSize = request.PortionSize,
            DietaryModes = request.DietaryModes.ToList(),
            DislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty,
            CustomAisleOrder = request.CustomAisleOrder ?? string.Empty,
            LeftoverCookDayIndexesCsv = request.LeftoverCookDayIndexesCsv ?? string.Empty,
            IgnoredMealSlotIndexesCsv = request.IgnoredMealSlotIndexesCsv ?? string.Empty,
            PreferQuickMeals = request.PreferQuickMeals,
            IncludeSpecialTreatMeal = request.IncludeSpecialTreatMeal,
            SelectedSpecialTreatCookDayIndex = request.SelectedSpecialTreatCookDayIndex,
            IncludeDessertAddOn = request.IncludeDessertAddOn,
            SelectedDessertAddOnName = request.SelectedDessertAddOnName ?? string.Empty
        });
    }

    private static AislePilotSavedWeekRequestCookieModel NormalizeSavedWeekRequestSnapshot(
        AislePilotSavedWeekRequestCookieModel? requestSnapshot)
    {
        var snapshot = requestSnapshot ?? new AislePilotSavedWeekRequestCookieModel();
        snapshot.Supermarket = (snapshot.Supermarket ?? string.Empty).Trim();
        snapshot.PortionSize = (snapshot.PortionSize ?? string.Empty).Trim();
        snapshot.DislikesOrAllergens = Truncate((snapshot.DislikesOrAllergens ?? string.Empty).Trim(), 260);
        snapshot.CustomAisleOrder = Truncate((snapshot.CustomAisleOrder ?? string.Empty).Trim(), 320);
        snapshot.LeftoverCookDayIndexesCsv = Truncate((snapshot.LeftoverCookDayIndexesCsv ?? string.Empty).Trim(), 140);
        snapshot.IgnoredMealSlotIndexesCsv = Truncate((snapshot.IgnoredMealSlotIndexesCsv ?? string.Empty).Trim(), 300);
        snapshot.SelectedDessertAddOnName = Truncate((snapshot.SelectedDessertAddOnName ?? string.Empty).Trim(), 120);
        snapshot.WeeklyBudget = Math.Clamp(snapshot.WeeklyBudget, 15m, 600m);
        snapshot.HouseholdSize = Math.Clamp(snapshot.HouseholdSize, 1, 8);
        snapshot.PlanDays = Math.Clamp(snapshot.PlanDays, 1, 7);
        snapshot.CookDays = Math.Clamp(snapshot.CookDays, 1, snapshot.PlanDays);
        snapshot.MealsPerDay = Math.Clamp(snapshot.MealsPerDay, MinMealsPerDay, MaxMealsPerDay);
        snapshot.SelectedMealTypes = NormalizeSelectedMealTypes(snapshot.SelectedMealTypes).ToList();
        snapshot.DietaryModes = (snapshot.DietaryModes ?? [])
            .Where(mode => !string.IsNullOrWhiteSpace(mode))
            .Select(mode => mode.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        snapshot.SelectedSpecialTreatCookDayIndex = snapshot.SelectedSpecialTreatCookDayIndex.HasValue
            ? Math.Clamp(snapshot.SelectedSpecialTreatCookDayIndex.Value, 0, 6)
            : null;
        return snapshot;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static string BuildSavedWeekLabel(string? requestedLabel)
    {
        var normalized = string.IsNullOrWhiteSpace(requestedLabel)
            ? string.Empty
            : requestedLabel.Trim();
        if (normalized.Length > 0)
        {
            return Truncate(normalized, 42);
        }

        var now = DateTimeOffset.UtcNow;
        return $"Week of {now:dd MMM yyyy}";
    }

    private static IReadOnlyList<AislePilotSavedWeekSummaryViewModel> BuildSavedWeekSummaries(
        IReadOnlyList<AislePilotSavedWeekCookieModel> snapshots)
    {
        return snapshots
            .Select(snapshot => new AislePilotSavedWeekSummaryViewModel
            {
                WeekId = snapshot.WeekId,
                Label = snapshot.Label,
                SavedAtUtc = snapshot.SavedAtUtc,
                PlanDays = Math.Clamp(snapshot.RequestSnapshot.PlanDays, 1, 7),
                MealCount = snapshot.CurrentPlanMealNames.Count,
                Supermarket = snapshot.RequestSnapshot.Supermarket
            })
            .ToList();
    }

    private static void SyncRequestWithResult(AislePilotRequestModel request, AislePilotPlanResultViewModel result)
    {
        if (!request.IncludeDessertAddOn)
        {
            request.SelectedDessertAddOnName = string.Empty;
            return;
        }

        if (result.IncludeDessertAddOn && !string.IsNullOrWhiteSpace(result.DessertAddOnName))
        {
            request.SelectedDessertAddOnName = result.DessertAddOnName;
        }
    }

    private void RefreshResultModelState()
    {
        ModelState.Remove("Request.SelectedDessertAddOnName");
        ModelState.Remove("Request.SelectedSpecialTreatCookDayIndex");
        ModelState.Remove("Request.SavedEnjoyedMealNamesState");
    }

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
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
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

    private void PersistSetupState(AislePilotRequestModel request)
    {
        var state = new AislePilotSetupStateCookieModel
        {
            Supermarket = request.Supermarket,
            WeeklyBudget = request.WeeklyBudget,
            HouseholdSize = request.HouseholdSize,
            CookDays = request.CookDays,
            PlanDays = request.PlanDays,
            MealsPerDay = request.MealsPerDay,
            SelectedMealTypes = request.SelectedMealTypes.ToList(),
            PortionSize = request.PortionSize,
            DietaryModes = request.DietaryModes.ToList(),
            DislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty,
            CustomAisleOrder = request.CustomAisleOrder ?? string.Empty,
            PantryItems = request.PantryItems ?? string.Empty,
            PreferQuickMeals = request.PreferQuickMeals,
            EnableSavedMealRepeats = request.EnableSavedMealRepeats,
            SavedMealRepeatRatePercent = Math.Clamp(request.SavedMealRepeatRatePercent, 10, 100),
            SavedEnjoyedMealNamesState = request.SavedEnjoyedMealNamesState ?? string.Empty,
            RequireCorePantryIngredients = request.RequireCorePantryIngredients,
            IncludeSpecialTreatMeal = request.IncludeSpecialTreatMeal,
            SelectedSpecialTreatCookDayIndex = request.SelectedSpecialTreatCookDayIndex,
            IncludeDessertAddOn = request.IncludeDessertAddOn,
            SelectedDessertAddOnName = request.SelectedDessertAddOnName
        };

        var payload = JsonSerializer.Serialize(state, SetupStateJsonOptions);
        if (payload.Length > 3500)
        {
            return;
        }

        Response.Cookies.Append(
            SetupStateCookieName,
            payload,
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(45),
                IsEssential = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
    }

    private AislePilotRequestModel? TryReadSavedSetupState()
    {
        if (!Request.Cookies.TryGetValue(SetupStateCookieName, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<AislePilotSetupStateCookieModel>(payload, SetupStateJsonOptions);
            if (state is null)
            {
                return null;
            }

            var planDays = Math.Clamp(state.PlanDays, 1, 7);
            var selectedMealTypes = NormalizeSelectedMealTypes(state.SelectedMealTypes);
            if (selectedMealTypes.Count == 0)
            {
                selectedMealTypes = BuildMealTypeSlotsFromMealsPerDay(state.MealsPerDay).ToList();
            }

            var mealsPerDay = selectedMealTypes.Count;

            return new AislePilotRequestModel
            {
                Supermarket = state.Supermarket ?? string.Empty,
                WeeklyBudget = Math.Clamp(state.WeeklyBudget, 15m, 600m),
                HouseholdSize = Math.Clamp(state.HouseholdSize, 1, 8),
                CookDays = Math.Clamp(state.CookDays, 1, planDays),
                PlanDays = planDays,
                MealsPerDay = mealsPerDay,
                SelectedMealTypes = selectedMealTypes,
                PortionSize = state.PortionSize ?? string.Empty,
                DietaryModes = state.DietaryModes?
                    .Where(mode => !string.IsNullOrWhiteSpace(mode))
                    .Select(mode => mode.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [],
                DislikesOrAllergens = state.DislikesOrAllergens ?? string.Empty,
                CustomAisleOrder = state.CustomAisleOrder ?? string.Empty,
                PantryItems = state.PantryItems ?? string.Empty,
                PreferQuickMeals = state.PreferQuickMeals,
                EnableSavedMealRepeats = state.EnableSavedMealRepeats,
                SavedMealRepeatRatePercent = Math.Clamp(state.SavedMealRepeatRatePercent, 10, 100),
                SavedEnjoyedMealNamesState = SerializeSavedEnjoyedMealNameState(
                    ParseSavedEnjoyedMealNamesState(state.SavedEnjoyedMealNamesState)),
                RequireCorePantryIngredients = state.RequireCorePantryIngredients,
                IncludeSpecialTreatMeal = state.IncludeSpecialTreatMeal,
                SelectedSpecialTreatCookDayIndex = state.SelectedSpecialTreatCookDayIndex,
                IncludeDessertAddOn = state.IncludeDessertAddOn,
                SelectedDessertAddOnName = state.SelectedDessertAddOnName
            };
        }
        catch
        {
            return null;
        }
    }

    private void PersistCurrentPlanState(AislePilotPlanResultViewModel result)
    {
        var mealNames = result.MealPlan
            .Select(meal => meal.MealName?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();
        if (mealNames.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(mealNames, SetupStateJsonOptions);
        if (payload.Length > 3500)
        {
            return;
        }

        Response.Cookies.Append(
            CurrentPlanStateCookieName,
            payload,
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(3),
                IsEssential = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
    }

    private IReadOnlyList<string>? TryReadCurrentPlanState()
    {
        if (!Request.Cookies.TryGetValue(CurrentPlanStateCookieName, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var mealNames = JsonSerializer.Deserialize<List<string>>(payload, SetupStateJsonOptions);
            return NormalizeCurrentPlanMealNames(mealNames);
        }
        catch
        {
            return null;
        }
    }

}
