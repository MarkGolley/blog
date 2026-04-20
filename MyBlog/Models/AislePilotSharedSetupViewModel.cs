namespace MyBlog.Models;

public sealed class AislePilotSharedSetupViewModel
{
    public bool ServingDetailsOpen { get; set; }
    public string ServingSummaryText { get; set; } = string.Empty;
    public int HouseholdSize { get; set; }
    public string[] PortionSizeOptions { get; set; } = [];
    public int SelectedPortionSizeIndex { get; set; }
    public string PortionSizeSliderValues { get; set; } = string.Empty;
    public string SelectedPortionSize { get; set; } = string.Empty;
    public bool DietaryDetailsOpen { get; set; }
    public string DietarySummaryText { get; set; } = string.Empty;
    public string[] DietaryOptions { get; set; } = [];
    public AislePilotDietarySelectionRules DietarySelectionRules { get; set; } = new();
    public string[] SelectedDietaryModes { get; set; } = [];
    public string CookingSummaryText { get; set; } = string.Empty;
    public bool PreferQuickMeals { get; set; }
    public bool EnableSavedMealRepeats { get; set; }
    public int SavedMealRepeatRatePercent { get; set; }
    public bool ExclusionsDetailsOpen { get; set; }
    public string ExclusionsSummaryText { get; set; } = string.Empty;
    public string DislikesOrAllergens { get; set; } = string.Empty;
}
