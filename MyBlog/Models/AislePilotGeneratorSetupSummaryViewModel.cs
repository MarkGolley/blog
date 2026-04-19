namespace MyBlog.Models;

public sealed class AislePilotGeneratorSetupSummaryViewModel
{
    public string PantrySummaryText { get; set; } = string.Empty;
    public string DietarySummaryText { get; set; } = string.Empty;
    public string GeneratorCoreSummaryText { get; set; } = string.Empty;
    public bool PreferQuickMeals { get; set; }
}
