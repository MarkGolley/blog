using Microsoft.AspNetCore.Mvc;

namespace MyBlog.Controllers;

[Route("projects")]
public class ProjectsController(IConfiguration configuration) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        var configuredAppMode =
            Environment.GetEnvironmentVariable("APP_MODE")
            ?? configuration["App:Mode"];
        var localAislePilotEnabled =
            !string.Equals(configuredAppMode, "BlogOnly", StringComparison.OrdinalIgnoreCase);
        ViewData["AislePilotLocalEnabled"] = localAislePilotEnabled;

        var configuredBaseUrl =
            Environment.GetEnvironmentVariable("AISLEPILOT_PUBLIC_BASE_URL")
            ?? configuration["AislePilot:PublicBaseUrl"];

        if (!string.IsNullOrWhiteSpace(configuredBaseUrl)
            && Uri.TryCreate(configuredBaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var configuredUri)
            && (string.Equals(configuredUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(configuredUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            ViewData["AislePilotPublicUrl"] = $"{configuredUri}/projects/aisle-pilot";
        }

        return View();
    }
}
