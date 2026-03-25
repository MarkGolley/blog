namespace MyBlog.Models;

public sealed class AislePilotHiddenRequestFieldsViewModel
{
    public string ReturnUrl { get; init; } = string.Empty;
    public AislePilotRequestModel Request { get; init; } = new();
    public string LeftoverCookDayIndexesCsv { get; init; } = string.Empty;
    public IReadOnlyList<string> CurrentPlanMealNames { get; init; } = Array.Empty<string>();
    public bool IncludeCurrentPlanMealNames { get; init; }
    public bool IncludeLeftoverCsvDataAttribute { get; init; }
}
