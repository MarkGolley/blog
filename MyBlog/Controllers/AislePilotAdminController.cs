using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Services;

namespace MyBlog.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public class AislePilotAdminController : Controller
{
    private readonly IAislePilotService _aislePilotService;
    private readonly IAislePilotBackgroundTaskQueue _backgroundTaskQueue;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AislePilotAdminController> _logger;

    public AislePilotAdminController(
        IAislePilotService aislePilotService,
        IAislePilotBackgroundTaskQueue backgroundTaskQueue,
        IConfiguration configuration,
        ILogger<AislePilotAdminController> logger)
    {
        _aislePilotService = aislePilotService;
        _backgroundTaskQueue = backgroundTaskQueue;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("/admin/aisle-pilot/warmup")]
    [EnableRateLimiting("aislePilotAdminWarmupWrites")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Warmup(
        [FromForm] string? adminKey,
        [FromForm] int? minPerSingleMode,
        [FromForm] int? minPerKeyPair,
        [FromForm] int? maxMealsToGenerate,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = AuthorizeAdmin(adminKey);
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var warmup = await _aislePilotService.WarmupAiMealPoolAsync(
            minPerSingleMode: minPerSingleMode ?? 8,
            minPerKeyPair: minPerKeyPair ?? 6,
            maxMealsToGenerate: maxMealsToGenerate ?? 2,
            cancellationToken);

        _logger.LogInformation(
            "AislePilot warm-up completed. Generated={GeneratedCount}, MaxPerRun={MaxPerRun}.",
            warmup.GeneratedCount,
            warmup.MaxMealsToGenerate);

        return Json(new
        {
            success = true,
            warmup.MinPerSingleMode,
            warmup.MinPerKeyPair,
            warmup.MaxMealsToGenerate,
            warmup.GeneratedCount,
            warmup.GeneratedMealNames,
            warmup.CoverageBefore,
            warmup.CoverageAfter
        });
    }

    [HttpPost("/admin/aisle-pilot/refresh-caches")]
    [EnableRateLimiting("aislePilotAdminWarmupWrites")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RefreshCaches(
        [FromForm] string? adminKey,
        CancellationToken cancellationToken)
    {
        var authorizationFailure = AuthorizeAdmin(adminKey);
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        await _aislePilotService.WarmRuntimeCachesAsync(cancellationToken);
        _logger.LogInformation("AislePilot runtime cache refresh completed through the admin endpoint.");
        return Json(new { success = true });
    }

    [HttpGet("/admin/aisle-pilot/background-status")]
    [EnableRateLimiting("aislePilotAdminWarmupWrites")]
    public IActionResult BackgroundStatus()
    {
        var authorizationFailure = AuthorizeAdmin(adminKey: null);
        if (authorizationFailure is not null)
        {
            return authorizationFailure;
        }

        var snapshot = _backgroundTaskQueue.GetSnapshot();
        return Json(new
        {
            success = true,
            snapshot.Capacity,
            snapshot.Concurrency,
            snapshot.MaxAttempts,
            snapshot.QueuedDepth,
            snapshot.ActiveCount,
            snapshot.AcceptedCount,
            snapshot.RejectedCount,
            snapshot.CompletedCount,
            snapshot.CancelledCount,
            snapshot.FaultedCount,
            snapshot.QueuedByJob
        });
    }

    private IActionResult? AuthorizeAdmin(string? adminKey)
    {
        var configuredAdminKey =
            Environment.GetEnvironmentVariable("AISLEPILOT_WARMUP_KEY")
            ?? _configuration["AislePilot:WarmupAdminKey"];
        if (string.IsNullOrWhiteSpace(configuredAdminKey))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                error = "AislePilot admin key is not configured."
            });
        }

        var providedAdminKey = Request.Headers["X-Admin-Key"].FirstOrDefault() ?? adminKey ?? string.Empty;
        return KeysMatch(configuredAdminKey, providedAdminKey)
            ? null
            : Unauthorized(new { success = false, error = "Unauthorized." });
    }

    private static bool KeysMatch(string expected, string provided)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
