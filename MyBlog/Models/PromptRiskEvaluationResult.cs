namespace MyBlog.Models;

public class PromptRiskEvaluationResult
{
    public int RiskScore { get; init; }
    public string RiskLevel { get; init; } = "Low";
    public IReadOnlyList<string> MatchedRules { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
}
