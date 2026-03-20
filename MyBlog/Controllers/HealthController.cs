using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Services;

namespace MyBlog.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public class HealthController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IWebHostEnvironment env,
        IServiceProvider serviceProvider,
        ILogger<HealthController> logger)
    {
        _env = env;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [HttpGet("/health")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, object>();
        var isHealthy = true;
        var minimumAislePilotCacheCount =
            int.TryParse(Environment.GetEnvironmentVariable("AISLEPILOT_MIN_CACHE_COUNT"), out var envMinCacheCount)
                ? Math.Max(0, envMinCacheCount)
                : Math.Max(0, int.TryParse(HttpContext.RequestServices.GetService<IConfiguration>()?["AislePilot:MinCacheCountForHealth"], out var configMinCacheCount) ? configMinCacheCount : 0);
        var failIfAislePilotCacheLow =
            bool.TryParse(Environment.GetEnvironmentVariable("AISLEPILOT_FAIL_HEALTH_ON_LOW_CACHE"), out var envFailOnLowCache)
                ? envFailOnLowCache
                : bool.TryParse(HttpContext.RequestServices.GetService<IConfiguration>()?["AislePilot:FailHealthOnLowCache"], out var configFailOnLowCache) && configFailOnLowCache;

        var blogStoragePath = Path.Combine(_env.WebRootPath, "BlogStorage");
        var blogStorageExists = Directory.Exists(blogStoragePath);
        var postCount = blogStorageExists
            ? Directory.EnumerateFiles(blogStoragePath, "*.html").Count()
            : 0;

        checks["blogStorage"] = new
        {
            status = blogStorageExists ? "ok" : "error",
            posts = postCount
        };

        if (!blogStorageExists)
        {
            isHealthy = false;
        }

        var firestore = _serviceProvider.GetService<FirestoreDb>();
        if (firestore == null)
        {
            var fallbackStatus = _env.IsDevelopment()
                ? "development-fallback"
                : "error";

            checks["firestore"] = new
            {
                status = fallbackStatus
            };

            if (!string.Equals(fallbackStatus, "development-fallback", StringComparison.Ordinal))
            {
                isHealthy = false;
            }

            checks["aislePilotMealCache"] = new
            {
                status = fallbackStatus,
                count = 0,
                minimumExpected = minimumAislePilotCacheCount
            };
        }
        else
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

                await firestore.Collection("meta").Limit(1).GetSnapshotAsync(timeoutCts.Token);
                checks["firestore"] = new { status = "ok" };

                var aiMealSnapshot = await firestore.Collection("aislePilotAiMeals").GetSnapshotAsync(timeoutCts.Token);
                var aiMealCount = aiMealSnapshot.Documents.Count;
                var cacheStatus = aiMealCount >= minimumAislePilotCacheCount ? "ok" : "warning";
                checks["aislePilotMealCache"] = new
                {
                    status = cacheStatus,
                    count = aiMealCount,
                    minimumExpected = minimumAislePilotCacheCount
                };

                if (failIfAislePilotCacheLow && cacheStatus == "warning")
                {
                    isHealthy = false;
                }
            }
            catch (Exception ex)
            {
                isHealthy = false;
                _logger.LogError(ex, "Firestore health check failed.");
                checks["firestore"] = new
                {
                    status = "error"
                };
                checks["aislePilotMealCache"] = new
                {
                    status = "error",
                    count = 0,
                    minimumExpected = minimumAislePilotCacheCount
                };
            }
        }

        var dailyCapsuleService = _serviceProvider.GetService<DailyCodingCapsuleService>();
        if (dailyCapsuleService == null)
        {
            checks["dailyCapsule"] = new
            {
                status = "error",
                error = "Daily capsule service unavailable."
            };
            isHealthy = false;
        }
        else
        {
            var capsuleStatus = dailyCapsuleService.GetOperationalStatus();
            checks["dailyCapsule"] = new
            {
                status = capsuleStatus.Status,
                aiGenerationEnabled = capsuleStatus.AiGenerationEnabled,
                persistence = capsuleStatus.PersistenceEnabled ? "firestore" : "memory",
                lastGenerationAttemptUtc = capsuleStatus.LastGenerationAttemptUtc,
                lastGenerationSuccessUtc = capsuleStatus.LastGenerationSuccessUtc,
                lastGenerationFailureUtc = capsuleStatus.LastGenerationFailureUtc,
                lastGenerationFailureReason = capsuleStatus.LastGenerationFailureReason,
                lastPersistFailureUtc = capsuleStatus.LastPersistFailureUtc,
                lastPersistFailureReason = capsuleStatus.LastPersistFailureReason,
                lastServedUkDate = capsuleStatus.LastServedUkDate,
                lastServedSource = capsuleStatus.LastServedSource
            };

            if (string.Equals(capsuleStatus.Status, "degraded", StringComparison.OrdinalIgnoreCase))
            {
                isHealthy = false;
            }
        }

        var payload = new
        {
            status = isHealthy ? "ok" : "degraded",
            timestampUtc = DateTime.UtcNow,
            checks
        };

        return StatusCode(isHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, payload);
    }
}
