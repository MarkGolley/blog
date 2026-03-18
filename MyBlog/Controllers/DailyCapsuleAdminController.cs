using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Services;

namespace MyBlog.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public class DailyCapsuleAdminController : Controller
{
    private readonly IDailyCodingCapsuleProvider _dailyCapsuleProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DailyCapsuleAdminController> _logger;

    public DailyCapsuleAdminController(
        IDailyCodingCapsuleProvider dailyCapsuleProvider,
        IConfiguration configuration,
        ILogger<DailyCapsuleAdminController> logger)
    {
        _dailyCapsuleProvider = dailyCapsuleProvider;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("/admin/daily-capsule/warmup")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Warmup([FromForm] string? adminKey, CancellationToken cancellationToken)
    {
        var configuredAdminKey = Environment.GetEnvironmentVariable("DAILY_CAPSULE_WARMUP_KEY")
                                 ?? _configuration["DailyCapsule:WarmupAdminKey"];

        if (string.IsNullOrWhiteSpace(configuredAdminKey))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                error = "Daily capsule warm-up key is not configured."
            });
        }

        var providedAdminKey = Request.Headers["X-Admin-Key"].FirstOrDefault() ?? adminKey ?? string.Empty;
        if (!KeysMatch(configuredAdminKey, providedAdminKey))
        {
            return Unauthorized(new { success = false, error = "Unauthorized." });
        }

        var capsule = await _dailyCapsuleProvider.GetCapsuleForCurrentDayAsync(cancellationToken);
        _logger.LogInformation(
            "Daily capsule warm-up completed. Source={Source}, Type={CapsuleType}, Title={Title}",
            capsule.Source,
            capsule.CapsuleType,
            capsule.Title);

        return Json(new
        {
            success = true,
            source = capsule.Source,
            capsuleType = capsule.CapsuleType,
            title = capsule.Title,
            nextResetUtc = capsule.NextResetUtcIso
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
