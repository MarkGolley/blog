using System.Text.Json;
using MyBlog.Models;

namespace MyBlog.Controllers;

public partial class AislePilotController
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
}
