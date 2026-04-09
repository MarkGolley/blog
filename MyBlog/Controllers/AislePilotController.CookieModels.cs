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
    private sealed class AislePilotSavedWeekCookieModel
    {
        public string WeekId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public DateTimeOffset SavedAtUtc { get; set; }
        public AislePilotSavedWeekRequestCookieModel RequestSnapshot { get; set; } = new();
        public List<string> CurrentPlanMealNames { get; set; } = [];

        public AislePilotRequestModel ToRequestModel()
        {
            return new AislePilotRequestModel
            {
                Supermarket = RequestSnapshot.Supermarket,
                WeeklyBudget = RequestSnapshot.WeeklyBudget,
                HouseholdSize = RequestSnapshot.HouseholdSize,
                CookDays = RequestSnapshot.CookDays,
                PlanDays = RequestSnapshot.PlanDays,
                MealsPerDay = RequestSnapshot.MealsPerDay,
                SelectedMealTypes = RequestSnapshot.SelectedMealTypes.ToList(),
                PortionSize = RequestSnapshot.PortionSize,
                DietaryModes = RequestSnapshot.DietaryModes.ToList(),
                DislikesOrAllergens = RequestSnapshot.DislikesOrAllergens,
                CustomAisleOrder = RequestSnapshot.CustomAisleOrder,
                LeftoverCookDayIndexesCsv = RequestSnapshot.LeftoverCookDayIndexesCsv,
                IgnoredMealSlotIndexesCsv = RequestSnapshot.IgnoredMealSlotIndexesCsv,
                PreferQuickMeals = RequestSnapshot.PreferQuickMeals,
                IncludeSpecialTreatMeal = RequestSnapshot.IncludeSpecialTreatMeal,
                SelectedSpecialTreatCookDayIndex = RequestSnapshot.SelectedSpecialTreatCookDayIndex,
                IncludeDessertAddOn = RequestSnapshot.IncludeDessertAddOn,
                SelectedDessertAddOnName = RequestSnapshot.SelectedDessertAddOnName
            };
        }
    }

    private sealed class AislePilotSavedWeekRequestCookieModel
    {
        public string Supermarket { get; set; } = "Tesco";
        public decimal WeeklyBudget { get; set; } = 65m;
        public int HouseholdSize { get; set; } = 2;
        public int CookDays { get; set; } = 7;
        public int PlanDays { get; set; } = 7;
        public int MealsPerDay { get; set; } = 3;
        public List<string> SelectedMealTypes { get; set; } = [];
        public string PortionSize { get; set; } = "Medium";
        public List<string> DietaryModes { get; set; } = [];
        public string DislikesOrAllergens { get; set; } = string.Empty;
        public string CustomAisleOrder { get; set; } = string.Empty;
        public string LeftoverCookDayIndexesCsv { get; set; } = string.Empty;
        public string IgnoredMealSlotIndexesCsv { get; set; } = string.Empty;
        public bool PreferQuickMeals { get; set; } = true;
        public bool IncludeSpecialTreatMeal { get; set; }
        public int? SelectedSpecialTreatCookDayIndex { get; set; }
        public bool IncludeDessertAddOn { get; set; }
        public string SelectedDessertAddOnName { get; set; } = string.Empty;
    }

    private sealed class AislePilotSetupStateCookieModel
    {
        public string? Supermarket { get; set; }
        public decimal WeeklyBudget { get; set; } = 65m;
        public int HouseholdSize { get; set; } = 2;
        public int CookDays { get; set; } = 7;
        public int PlanDays { get; set; } = 7;
        public int MealsPerDay { get; set; } = 3;
        public List<string> SelectedMealTypes { get; set; } = [];
        public string? PortionSize { get; set; }
        public List<string> DietaryModes { get; set; } = [];
        public string? DislikesOrAllergens { get; set; }
        public string? CustomAisleOrder { get; set; }
        public string? PantryItems { get; set; }
        public bool PreferQuickMeals { get; set; } = true;
        public bool EnableSavedMealRepeats { get; set; } = true;
        public int SavedMealRepeatRatePercent { get; set; } = 35;
        public string? SavedEnjoyedMealNamesState { get; set; }
        public bool RequireCorePantryIngredients { get; set; }
        public bool IncludeSpecialTreatMeal { get; set; }
        public int? SelectedSpecialTreatCookDayIndex { get; set; }
        public bool IncludeDessertAddOn { get; set; }
        public string? SelectedDessertAddOnName { get; set; }
    }
}
