using System.Text.Json;
using MyBlog.Models;

namespace MyBlog.Controllers;

public partial class AislePilotController
{
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
