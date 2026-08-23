namespace MyBlog.Models;

public enum AislePilotAlertCategory
{
    Validation,
    Constraint,
    Operation
}

public sealed record AislePilotAlertItemViewModel(string Message, string? TargetId, string? FieldLabel);

public sealed class AislePilotAlertViewModel
{
    public AislePilotAlertCategory Category { get; init; }
    public IReadOnlyList<AislePilotAlertItemViewModel> Items { get; init; } = [];
    public bool HasResult { get; init; }
}
