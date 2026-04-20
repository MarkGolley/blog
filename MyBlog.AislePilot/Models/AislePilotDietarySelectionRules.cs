namespace MyBlog.Models;

public sealed class AislePilotDietarySelectionRules
{
    public int MaxSelections { get; set; } = 2;
    public string HelperText { get; set; } = string.Empty;
    public string MaxSelectionsMessage { get; set; } = string.Empty;
    public IReadOnlyList<string> CoreModes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> OverlayModes { get; set; } = Array.Empty<string>();
}
