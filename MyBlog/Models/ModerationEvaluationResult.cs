namespace MyBlog.Models;

public enum ModerationDecision
{
    Allow,
    Block,
    ManualReview
}

public class ModerationEvaluationResult
{
    public ModerationDecision Decision { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public bool? FlaggedByModel { get; init; }
    public string? OpenAiRequestId { get; init; }
    public bool IsSafe => Decision == ModerationDecision.Allow;
}
