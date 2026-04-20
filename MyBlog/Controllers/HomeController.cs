using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

public class HomeController(IConfiguration configuration, BlogService blogService) : Controller
{
    public IActionResult Index()
    {
        var configuredAppMode =
            Environment.GetEnvironmentVariable("APP_MODE")
            ?? configuration["App:Mode"];
        var aislePilotLocalEnabled =
            !string.Equals(configuredAppMode, "BlogOnly", StringComparison.OrdinalIgnoreCase);
        ViewData["AislePilotLocalEnabled"] = aislePilotLocalEnabled;

        var configuredAislePilotBaseUrl =
            Environment.GetEnvironmentVariable("AISLEPILOT_PUBLIC_BASE_URL")
            ?? configuration["AislePilot:PublicBaseUrl"];

        if (!string.IsNullOrWhiteSpace(configuredAislePilotBaseUrl)
            && Uri.TryCreate(configuredAislePilotBaseUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var configuredAislePilotUri)
            && (string.Equals(configuredAislePilotUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(configuredAislePilotUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            var normalizedBaseUrl = configuredAislePilotUri.ToString().TrimEnd('/');
            ViewData["AislePilotPublicUrl"] = $"{normalizedBaseUrl}/projects/aisle-pilot";
        }

        SetExternalProfileUrl("ResumeUrl", "PROFILE_RESUME_URL", "Profile:ResumeUrl");
        SetExternalProfileUrl("GitHubUrl", "PROFILE_GITHUB_URL", "Profile:GitHubUrl");
        SetExternalProfileUrl("LinkedInUrl", "PROFILE_LINKEDIN_URL", "Profile:LinkedInUrl");

        var viewModel = new HomeIndexViewModel
        {
            PublishedPostCount = blogService.GetAllPosts().Count(),
            FeaturedPosts = blogService.GetFeaturedPosts()
        };

        return View(viewModel);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }

    private void SetExternalProfileUrl(string viewDataKey, string environmentVariableName, string configurationKey)
    {
        var configuredUrl =
            Environment.GetEnvironmentVariable(environmentVariableName)
            ?? configuration[configurationKey];

        if (string.IsNullOrWhiteSpace(configuredUrl)
            || !Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var parsedUri)
            || (!string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        ViewData[viewDataKey] = parsedUri.ToString();
    }
}
