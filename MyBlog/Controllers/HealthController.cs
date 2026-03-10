using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;

namespace MyBlog.Controllers;

[ApiExplorerSettings(IgnoreApi = true)]
public class HealthController : Controller
{
    private readonly IWebHostEnvironment _env;
    private readonly IServiceProvider _serviceProvider;

    public HealthController(IWebHostEnvironment env, IServiceProvider serviceProvider)
    {
        _env = env;
        _serviceProvider = serviceProvider;
    }

    [HttpGet("/health")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, object>();
        var isHealthy = true;

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
        }
        else
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

                await firestore.Collection("meta").Limit(1).GetSnapshotAsync(timeoutCts.Token);
                checks["firestore"] = new { status = "ok" };
            }
            catch (Exception ex)
            {
                isHealthy = false;
                checks["firestore"] = new
                {
                    status = "error",
                    error = ex.Message
                };
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
