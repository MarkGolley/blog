using System.ComponentModel.DataAnnotations;

namespace MyBlog.Models;

public class ModerationPlaygroundViewModel
{
    [StringLength(2000, ErrorMessage = "Text must be 2000 characters or fewer.")]
    public string InputText { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Prompt must be 2000 characters or fewer.")]
    public string PromptRiskInput { get; set; } = string.Empty;

    public bool HasModerationResult { get; set; }
    public ModerationDecision? Decision { get; set; }
    public bool? FlaggedByModel { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public long? LatencyMs { get; set; }
    public string ResultTitle { get; set; } = string.Empty;
    public string ResultMessage { get; set; } = string.Empty;

    public bool HasPromptRiskResult { get; set; }
    public int PromptRiskScore { get; set; }
    public string PromptRiskLevel { get; set; } = "Low";
    public IReadOnlyList<string> PromptRiskMatchedRules { get; set; } = Array.Empty<string>();
    public string PromptRiskSummary { get; set; } = string.Empty;
    public string PromptRiskRecommendation { get; set; } = string.Empty;
}
