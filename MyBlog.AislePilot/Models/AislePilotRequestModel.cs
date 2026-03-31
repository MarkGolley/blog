using System.ComponentModel.DataAnnotations;

namespace MyBlog.Models;

public sealed class AislePilotRequestModel
{
    [Required]
    public string Supermarket { get; set; } = "Tesco";

    [Range(typeof(decimal), "15", "600", ErrorMessage = "Set a weekly budget between 15 and 600.")]
    public decimal WeeklyBudget { get; set; } = 65m;

    [Range(1, 8, ErrorMessage = "People must be between 1 and 8.")]
    public int HouseholdSize { get; set; } = 2;

    [Range(1, 7, ErrorMessage = "Choose between 1 and 7 cook days.")]
    public int CookDays { get; set; } = 7;

    [Range(1, 7, ErrorMessage = "Choose a plan length between 1 and 7 days.")]
    public int PlanDays { get; set; } = 7;

    [Range(1, 3, ErrorMessage = "Choose between 1 and 3 meals per day.")]
    public int MealsPerDay { get; set; } = 1;

    public List<string> SelectedMealTypes { get; set; } = [];

    public string PortionSize { get; set; } = "Medium";

    public List<string> DietaryModes { get; set; } = ["Balanced"];

    [StringLength(260, ErrorMessage = "Keep dislikes/allergen notes to 260 characters or fewer.")]
    public string? DislikesOrAllergens { get; set; } = string.Empty;

    [StringLength(320, ErrorMessage = "Custom aisle order must be 320 characters or fewer.")]
    public string? CustomAisleOrder { get; set; } = string.Empty;

    [StringLength(400, ErrorMessage = "Pantry notes must be 400 characters or fewer.")]
    public string? PantryItems { get; set; } = string.Empty;

    public bool RequireCorePantryIngredients { get; set; }

    [StringLength(140, ErrorMessage = "Leftover day mapping is too long.")]
    public string? LeftoverCookDayIndexesCsv { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Swap history is too long.")]
    public string? SwapHistoryState { get; set; } = string.Empty;

    [StringLength(300, ErrorMessage = "Ignored meal list is too long.")]
    public string? IgnoredMealSlotIndexesCsv { get; set; } = string.Empty;

    [StringLength(2800, ErrorMessage = "Pantry suggestion history is too long.")]
    public string? PantrySuggestionHistoryState { get; set; } = string.Empty;

    public bool PreferQuickMeals { get; set; } = true;

    public bool IncludeSpecialTreatMeal { get; set; }

    public bool IncludeDessertAddOn { get; set; }

    [StringLength(120, ErrorMessage = "Dessert selection is too long.")]
    public string? SelectedDessertAddOnName { get; set; } = string.Empty;
}
