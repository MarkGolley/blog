using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

[Route("ai-experiments")]
public class AIExperimentsController : Controller
{
    private readonly AIModerationService _aiModerationService;
    private readonly PromptRiskScannerService _promptRiskScannerService;
    private readonly ILogger<AIExperimentsController> _logger;

    public AIExperimentsController(
        AIModerationService aiModerationService,
        PromptRiskScannerService promptRiskScannerService,
        ILogger<AIExperimentsController> logger)
    {
        _aiModerationService = aiModerationService;
        _promptRiskScannerService = promptRiskScannerService;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return View(new ModerationPlaygroundViewModel());
    }

    [HttpPost("moderation")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("moderationChecks")]
    public async Task<IActionResult> RunModeration(ModerationPlaygroundViewModel model)
    {
        model.InputText = model.InputText?.Trim() ?? string.Empty;
        model.PromptRiskInput = model.PromptRiskInput?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(model.InputText) || model.InputText.Length < 3)
        {
            ModelState.AddModelError(nameof(model.InputText), "Enter at least 3 characters to run moderation.");
        }

        if (model.InputText.Length > 2000)
        {
            ModelState.AddModelError(nameof(model.InputText), "Text must be 2000 characters or fewer.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var timer = Stopwatch.StartNew();
        var result = await _aiModerationService.EvaluateCommentAsync(model.InputText);
        timer.Stop();

        model.HasModerationResult = true;
        model.Decision = result.Decision;
        model.FlaggedByModel = result.FlaggedByModel;
        model.ReasonCode = result.ReasonCode;
        model.LatencyMs = timer.ElapsedMilliseconds;

        switch (result.Decision)
        {
            case ModerationDecision.Allow:
                model.ResultTitle = "Allowed";
                model.ResultMessage = "This sample passes automated moderation.";
                break;
            case ModerationDecision.Block:
                model.ResultTitle = "Blocked";
                model.ResultMessage = "This sample is blocked by moderation policy.";
                break;
            default:
                model.ResultTitle = "Manual review";
                model.ResultMessage = "No automated approval decision was available for this sample.";
                break;
        }

        _logger.LogInformation(
            "Moderation playground evaluated sample. Decision={Decision} ReasonCode={ReasonCode} LatencyMs={LatencyMs}",
            result.Decision,
            result.ReasonCode,
            model.LatencyMs ?? 0);

        return View("Index", model);
    }

    [HttpPost("prompt-risk")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("promptRiskChecks")]
    public IActionResult RunPromptRisk(ModerationPlaygroundViewModel model)
    {
        model.InputText = model.InputText?.Trim() ?? string.Empty;
        model.PromptRiskInput = model.PromptRiskInput?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(model.PromptRiskInput) || model.PromptRiskInput.Length < 3)
        {
            ModelState.AddModelError(nameof(model.PromptRiskInput), "Enter at least 3 characters to scan for risk patterns.");
        }

        if (model.PromptRiskInput.Length > 2000)
        {
            ModelState.AddModelError(nameof(model.PromptRiskInput), "Prompt must be 2000 characters or fewer.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        var result = _promptRiskScannerService.Evaluate(model.PromptRiskInput);
        model.HasPromptRiskResult = true;
        model.PromptRiskScore = result.RiskScore;
        model.PromptRiskLevel = result.RiskLevel;
        model.PromptRiskMatchedRules = result.MatchedRules;
        model.PromptRiskSummary = result.Summary;
        model.PromptRiskRecommendation = result.Recommendation;

        _logger.LogInformation(
            "Prompt risk scanner evaluated sample. RiskLevel={RiskLevel} RiskScore={RiskScore}",
            result.RiskLevel,
            result.RiskScore);

        return View("Index", model);
    }
}
