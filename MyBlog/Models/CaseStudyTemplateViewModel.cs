namespace MyBlog.Models;

public class CaseStudyTemplateViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = "Planned";
    public string OneLiner { get; init; } = string.Empty;
    public string WhyItMatters { get; init; } = string.Empty;
    public IReadOnlyList<string> TechnicalScope { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ProofChecklist { get; init; } = Array.Empty<string>();
    public string RecruiterSignal { get; init; } = string.Empty;
    public string NextMilestone { get; init; } = string.Empty;
}
