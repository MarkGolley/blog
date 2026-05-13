using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

public partial class AislePilotController
{
    [HttpGet("meal-images")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> MealImages([FromQuery] List<string>? mealNames, CancellationToken cancellationToken)
    {
        var canGenerateImages = aislePilotService.CanGenerateMealImages();
        if (mealNames is null || mealNames.Count == 0)
        {
            return Ok(new { images = Array.Empty<object>(), canGenerateImages });
        }

        var normalizedMealNames = mealNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
        if (normalizedMealNames.Count == 0)
        {
            return Ok(new { images = Array.Empty<object>(), canGenerateImages });
        }

        var imageUrls = await aislePilotService.GetMealImageUrlsAsync(normalizedMealNames, cancellationToken);
        var images = normalizedMealNames
            .Select(name => new
            {
                mealName = name,
                imageUrl = AislePilotMealImageUrlResolver.ResolveClientMealImageUrl(imageUrls.GetValueOrDefault(name, string.Empty))
            })
            .ToList();

        return Ok(new { images, canGenerateImages });
    }

    [HttpPost("export/plan-pack")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportPlanPack(
        AislePilotPageViewModel pageModel,
        string? exportTheme,
        List<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        ValidateRequest(request);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var result = await BuildExportResultAsync(request, currentPlanMealNames, cancellationToken);
            var useDarkTheme = string.Equals(exportTheme, "dark", StringComparison.OrdinalIgnoreCase);
            var bytes = aislePilotExportService.BuildPlanPackPdf(request, result, useDarkTheme);
            var fileName = $"aislepilot-plan-pack-{DateTime.UtcNow:yyyyMMdd}.pdf";
            Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
            return File(bytes, "application/pdf");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "AislePilot AI unavailable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot plan-pack export failed unexpectedly.");
            return Problem(
                title: "Export failed",
                detail: "Plan-pack export hit a temporary issue. Please retry.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost("export/checklist")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportChecklist(
        AislePilotPageViewModel pageModel,
        List<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        ValidateRequest(request);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var result = await BuildExportResultAsync(request, currentPlanMealNames, cancellationToken);
            var content = aislePilotExportService.BuildChecklistText(result);
            var bytes = Encoding.UTF8.GetBytes(content);
            var fileName = $"aislepilot-checklist-{DateTime.UtcNow:yyyyMMdd}.txt";
            return File(bytes, "text/plain; charset=utf-8", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "AislePilot AI unavailable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot checklist export failed unexpectedly.");
            return Problem(
                title: "Export failed",
                detail: "Checklist export hit a temporary issue. Please retry.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<AislePilotPlanResultViewModel> BuildExportResultAsync(
        AislePilotRequestModel request,
        IReadOnlyList<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var resolvedCurrentPlanMealNames = ResolveCurrentPlanMealNames(currentPlanMealNames);
        if (resolvedCurrentPlanMealNames is not null && resolvedCurrentPlanMealNames.Count > 0)
        {
            return await aislePilotService.BuildPlanFromCurrentMealsAsync(request, resolvedCurrentPlanMealNames, cancellationToken);
        }

        logger.LogWarning("AislePilot export request did not include currentPlanMealNames; regenerating plan.");
        return await aislePilotService.BuildPlanAsync(request, cancellationToken);
    }
}
