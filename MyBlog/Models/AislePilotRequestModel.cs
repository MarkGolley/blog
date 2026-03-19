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

    public List<string> DietaryModes { get; set; } = ["Balanced"];

    [StringLength(260, ErrorMessage = "Keep dislikes/allergen notes to 260 characters or fewer.")]
    public string? DislikesOrAllergens { get; set; } = string.Empty;

    [StringLength(320, ErrorMessage = "Custom aisle order must be 320 characters or fewer.")]
    public string? CustomAisleOrder { get; set; } = string.Empty;

    [StringLength(400, ErrorMessage = "Pantry notes must be 400 characters or fewer.")]
    public string? PantryItems { get; set; } = string.Empty;

    [StringLength(140, ErrorMessage = "Leftover day mapping is too long.")]
    public string? LeftoverCookDayIndexesCsv { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Swap history is too long.")]
    public string? SwapHistoryState { get; set; } = string.Empty;

    public bool PreferQuickMeals { get; set; } = true;
}
