namespace MyBlog.Models;

public sealed class AislePilotWarmupResult
{
    public int MinPerSingleMode { get; set; }
    public int MinPerKeyPair { get; set; }
    public int MaxMealsToGenerate { get; set; }
    public int GeneratedCount { get; set; }
    public IReadOnlyList<string> GeneratedMealNames { get; set; } = Array.Empty<string>();
    public IReadOnlyList<AislePilotWarmupCoverageViewModel> CoverageBefore { get; set; } =
        Array.Empty<AislePilotWarmupCoverageViewModel>();
    public IReadOnlyList<AislePilotWarmupCoverageViewModel> CoverageAfter { get; set; } =
        Array.Empty<AislePilotWarmupCoverageViewModel>();
}

public sealed class AislePilotWarmupCoverageViewModel
{
    public string Profile { get; set; } = string.Empty;
    public IReadOnlyList<string> Modes { get; set; } = Array.Empty<string>();
    public int Target { get; set; }
    public int Count { get; set; }
    public int Deficit { get; set; }
}
