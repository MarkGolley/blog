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
    private readonly IConfiguration _configuration;
    private readonly ILogger<AIExperimentsController> _logger;

    public AIExperimentsController(
        AIModerationService aiModerationService,
        IConfiguration configuration,
        ILogger<AIExperimentsController> logger)
    {
        _aiModerationService = aiModerationService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        return View(new ModerationPlaygroundViewModel());
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("moderationChecks")]
    public async Task<IActionResult> Index(ModerationPlaygroundViewModel model)
    {
        model.InputText = model.InputText?.Trim() ?? string.Empty;

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!IsModerationDemoAvailable())
        {
            model.HasResult = true;
            model.IsSafe = null;
            model.ResultTitle = "Demo unavailable";
            model.ResultMessage = "The moderation demo is temporarily unavailable because the API key is not configured.";
            return View(model);
        }

        var timer = Stopwatch.StartNew();
        var isSafe = await _aiModerationService.IsCommentSafeAsync(model.InputText);
        timer.Stop();

        model.HasResult = true;
        model.IsSafe = isSafe;
        model.LatencyMs = timer.ElapsedMilliseconds;

        if (isSafe)
        {
            model.ResultTitle = "Allowed";
            model.ResultMessage = "This sample would pass automated moderation.";
        }
        else
        {
            model.ResultTitle = "Blocked or manual review";
            model.ResultMessage =
                "This sample would be blocked or held for manual review by the current moderation policy.";
        }

        _logger.LogInformation(
            "Moderation playground evaluated sample. IsSafe={IsSafe} LatencyMs={LatencyMs}",
            isSafe,
            model.LatencyMs ?? 0);

        return View(model);
    }

    private bool IsModerationDemoAvailable()
    {
        var apiKey = _configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return !string.IsNullOrWhiteSpace(apiKey);
    }
}
