namespace MyBlog.Models;

public sealed class DailyCodingCapsuleViewModel
{
    public string CapsuleType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Example { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string NextResetUtcIso { get; init; } = string.Empty;
}
