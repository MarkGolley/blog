using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Services;

namespace MyBlog.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public class AislePilotAdminController : Controller
{
    private readonly IAislePilotService _aislePilotService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AislePilotAdminController> _logger;

    public AislePilotAdminController(
        IAislePilotService aislePilotService,
        IConfiguration configuration,
        ILogger<AislePilotAdminController> logger)
    {
        _aislePilotService = aislePilotService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("/admin/aisle-pilot/warmup")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Warmup(
        [FromForm] string? adminKey,
        [FromForm] int? minPerSingleMode,
        [FromForm] int? minPerKeyPair,
        [FromForm] int? maxMealsToGenerate,
        CancellationToken cancellationToken)
    {
        var configuredAdminKey =
            Environment.GetEnvironmentVariable("AISLEPILOT_WARMUP_KEY")
            ?? _configuration["AislePilot:WarmupAdminKey"];

        if (string.IsNullOrWhiteSpace(configuredAdminKey))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                error = "AislePilot warm-up key is not configured."
            });
        }

        var providedAdminKey = Request.Headers["X-Admin-Key"].FirstOrDefault() ?? adminKey ?? string.Empty;
        if (!KeysMatch(configuredAdminKey, providedAdminKey))
        {
            return Unauthorized(new { success = false, error = "Unauthorized." });
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
