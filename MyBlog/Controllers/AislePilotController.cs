using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

[Route("projects/aisle-pilot")]
public class AislePilotController(
    IAislePilotService aislePilotService,
    IAislePilotExportService aislePilotExportService,
    ILogger<AislePilotController> logger) : Controller
{
    private const string SetupStateCookieName = "aislepilot.setup.v1";
    private const string CurrentPlanStateCookieName = "aislepilot.plan.v1";
    private static readonly JsonSerializerOptions SetupStateJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [HttpGet("")]
    public IActionResult Index(string? returnUrl = null)
    {
        var request = NormalizeRequest(TryReadSavedSetupState() ?? new AislePilotRequestModel());
        var resolvedReturnUrl = ResolveReturnUrl(returnUrl);
        return View(BuildPageModel(request, returnUrl: resolvedReturnUrl));
    }

    [HttpPost("")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(AislePilotPageViewModel pageModel, CancellationToken cancellationToken)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        ValidateRequest(request);

        if (!ModelState.IsValid)
        {
            return View(BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        try
        {
            var result = await aislePilotService.BuildPlanAsync(request, cancellationToken);
            PersistCurrentPlanState(result);
            return View(BuildPageModel(request, result, returnUrl: resolvedReturnUrl));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot regenerate failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Plan regeneration hit a temporary issue. Please retry in a few seconds.");
            return View(BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("rebalance-budget")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RebalanceBudget(
        AislePilotPageViewModel pageModel,
        List<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        ValidateRequest(request);

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        try
        {
            var resolvedCurrentPlanMealNames = ResolveCurrentPlanMealNames(currentPlanMealNames);
            var result = await aislePilotService.BuildPlanWithBudgetRebalanceAsync(
                request,
                currentPlanMealNames: resolvedCurrentPlanMealNames,
                cancellationToken: cancellationToken);
            PersistCurrentPlanState(result);
            return View("Index", BuildPageModel(request, result, returnUrl: resolvedReturnUrl));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot budget rebalance failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Budget refresh hit a temporary issue. Please retry in a few seconds.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("suggest-from-pantry")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public IActionResult SuggestFromPantry(AislePilotPageViewModel pageModel)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        ValidateRequestForSuggestions(request);

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        try
        {
            var suggestions = aislePilotService.SuggestMealsFromPantry(request, 6);
            if (suggestions.Count == 0)
            {
                ModelState.AddModelError("Request.PantryItems", "No full meals found from your current pantry items. Add more ingredients or generate a full weekly plan.");
                return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
            }

            return View("Index", BuildPageModel(request, pantrySuggestions: suggestions, returnUrl: resolvedReturnUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot pantry suggestion failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Meal generator hit a temporary issue. Please retry in a few seconds.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("swap-meal")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwapMeal(
        AislePilotPageViewModel pageModel,
        int dayIndex,
        string? currentMealName,
        List<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        ValidateRequest(request);
        var cookDays = Math.Clamp(request.CookDays, 1, 7);

        if (dayIndex < 0 || dayIndex >= cookDays)
        {
            ModelState.AddModelError(string.Empty, "Selected day was out of range. Try generating again.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        var seenMealNames = GetSeenMealsForDay(request.SwapHistoryState, dayIndex, currentMealName);

        try
        {
            var resolvedCurrentPlanMealNames = ResolveCurrentPlanMealNames(currentPlanMealNames);
            var result = await aislePilotService.SwapMealForDayAsync(
                request,
                dayIndex,
                currentMealName,
                resolvedCurrentPlanMealNames,
                seenMealNames,
                cancellationToken);
            request.SwapHistoryState = UpdateSwapHistoryState(
                request.SwapHistoryState,
                dayIndex,
                currentMealName,
                result.MealPlan.ElementAtOrDefault(dayIndex)?.MealName);
            PersistCurrentPlanState(result);
            var responseModel = BuildPageModel(request, result, returnUrl: resolvedReturnUrl);
            if (IsAjaxRequest())
            {
                return PartialView("_AislePilotResultSections", responseModel);
            }

            return View("Index", responseModel);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot swap meal failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Meal swap hit a temporary issue. Please retry.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

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
                imageUrl = imageUrls.GetValueOrDefault(name, string.Empty)
            })
            .ToList();

        return Ok(new { images, canGenerateImages });
    }

    [HttpPost("export/plan-pack")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportPlanPack(
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
            var bytes = aislePilotExportService.BuildPlanPackPdf(request, result);
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

    private AislePilotPageViewModel BuildPageModel(
        AislePilotRequestModel request,
        AislePilotPlanResultViewModel? result = null,
        IReadOnlyList<AislePilotPantrySuggestionViewModel>? pantrySuggestions = null,
        string returnUrl = "")
    {
        return new AislePilotPageViewModel
        {
            Request = request,
            ReturnUrl = returnUrl,
            Result = result,
            PantrySuggestions = pantrySuggestions ?? [],
            SupermarketOptions = aislePilotService.GetSupportedSupermarkets(),
            PortionSizeOptions = aislePilotService.GetSupportedPortionSizes(),
            DietaryOptions = aislePilotService.GetSupportedDietaryModes(),
            MealImagePollingEnabled = aislePilotService.CanGenerateMealImages()
        };
    }

    private string ResolveReturnUrl(string? returnUrl)
    {
        var fallbackReturnUrl = Url.Action("Index", "Projects") ?? "/projects";

        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            Url.IsLocalUrl(returnUrl) &&
            !IsAislePilotPath(returnUrl))
        {
            return returnUrl;
        }

        var referer = Request.Headers["Referer"].FirstOrDefault();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) &&
            string.Equals(refererUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            var candidate = $"{refererUri.AbsolutePath}{refererUri.Query}";
            if (Url.IsLocalUrl(candidate) && !IsAislePilotPath(candidate))
            {
                return candidate;
            }
        }

        return fallbackReturnUrl;
    }

    private static bool IsAislePilotPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var path = url.Split('?', '#')[0];
        return path.StartsWith("/projects/aisle-pilot", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string>? ResolveCurrentPlanMealNames(IReadOnlyList<string>? postedCurrentPlanMealNames)
    {
        var normalizedFromRequest = NormalizeCurrentPlanMealNames(postedCurrentPlanMealNames);
        if (normalizedFromRequest is not null && normalizedFromRequest.Count > 0)
        {
            return normalizedFromRequest;
        }

        return TryReadCurrentPlanState();
    }

    private static IReadOnlyList<string>? NormalizeCurrentPlanMealNames(IReadOnlyList<string>? mealNames)
    {
        if (mealNames is null || mealNames.Count == 0)
        {
            return null;
        }

        var normalized = mealNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        return normalized.Count > 0 ? normalized : null;
    }

    private bool IsAjaxRequest()
    {
        return string.Equals(
            Request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
    }

    private AislePilotRequestModel NormalizeRequest(AislePilotRequestModel? request)
    {
        var normalized = request ?? new AislePilotRequestModel();
        normalized.Supermarket = normalized.Supermarket?.Trim() ?? string.Empty;
        normalized.PortionSize = normalized.PortionSize?.Trim() ?? string.Empty;
        normalized.CustomAisleOrder = normalized.CustomAisleOrder?.Trim() ?? string.Empty;
        normalized.DislikesOrAllergens = normalized.DislikesOrAllergens?.Trim() ?? string.Empty;
        normalized.PantryItems = normalized.PantryItems?.Trim() ?? string.Empty;
        normalized.LeftoverCookDayIndexesCsv = normalized.LeftoverCookDayIndexesCsv?.Trim() ?? string.Empty;
        normalized.SwapHistoryState = normalized.SwapHistoryState?.Trim() ?? string.Empty;
        normalized.DietaryModes = normalized.DietaryModes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        return normalized;
    }

    private static IReadOnlyList<string> GetSeenMealsForDay(string? swapHistoryState, int dayIndex, string? currentMealName)
    {
        var history = ParseSwapHistoryState(swapHistoryState);
        var seenMeals = history.TryGetValue(dayIndex, out var names)
            ? names
            : [];

        if (string.IsNullOrWhiteSpace(currentMealName))
        {
            return seenMeals;
        }

        return seenMeals
            .Append(currentMealName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string UpdateSwapHistoryState(
        string? currentState,
        int dayIndex,
        string? previousMealName,
        string? nextMealName)
    {
        var history = ParseSwapHistoryState(currentState);
        if (!history.TryGetValue(dayIndex, out var meals))
        {
            meals = [];
            history[dayIndex] = meals;
        }

        if (!string.IsNullOrWhiteSpace(previousMealName) &&
            !meals.Contains(previousMealName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            meals.Add(previousMealName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(nextMealName) &&
            !meals.Contains(nextMealName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            meals.Add(nextMealName.Trim());
        }

        return SerializeSwapHistoryState(history);
    }

    private static Dictionary<int, List<string>> ParseSwapHistoryState(string? swapHistoryState)
    {
        var result = new Dictionary<int, List<string>>();
        if (string.IsNullOrWhiteSpace(swapHistoryState))
        {
            return result;
        }

        var dayEntries = swapHistoryState.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in dayEntries)
        {
            var segments = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (segments.Length != 2 || !int.TryParse(segments[0], out var dayIndex) || dayIndex < 0 || dayIndex > 6)
            {
                continue;
            }

            var meals = segments[1]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(meal => !string.IsNullOrWhiteSpace(meal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (meals.Count > 0)
            {
                result[dayIndex] = meals;
            }
        }

        return result;
    }

    private static string SerializeSwapHistoryState(Dictionary<int, List<string>> history)
    {
        return string.Join(
            ';',
            history
                .OrderBy(pair => pair.Key)
                .Where(pair => pair.Value.Count > 0)
                .Select(pair => $"{pair.Key}:{string.Join('|', pair.Value.Distinct(StringComparer.OrdinalIgnoreCase))}"));
    }

    private void PersistSetupState(AislePilotRequestModel request)
    {
        var state = new AislePilotSetupStateCookieModel
        {
            Supermarket = request.Supermarket,
            WeeklyBudget = request.WeeklyBudget,
            HouseholdSize = request.HouseholdSize,
            CookDays = request.CookDays,
            PortionSize = request.PortionSize,
            DietaryModes = request.DietaryModes.ToList(),
            DislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty,
            CustomAisleOrder = request.CustomAisleOrder ?? string.Empty,
            PantryItems = request.PantryItems ?? string.Empty,
            PreferQuickMeals = request.PreferQuickMeals
        };

        var payload = JsonSerializer.Serialize(state, SetupStateJsonOptions);
        if (payload.Length > 3500)
        {
            return;
        }

        Response.Cookies.Append(
            SetupStateCookieName,
            payload,
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(45),
                IsEssential = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
    }

    private AislePilotRequestModel? TryReadSavedSetupState()
    {
        if (!Request.Cookies.TryGetValue(SetupStateCookieName, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<AislePilotSetupStateCookieModel>(payload, SetupStateJsonOptions);
            if (state is null)
            {
                return null;
            }

            return new AislePilotRequestModel
            {
                Supermarket = state.Supermarket ?? string.Empty,
                WeeklyBudget = Math.Clamp(state.WeeklyBudget, 15m, 600m),
                HouseholdSize = Math.Clamp(state.HouseholdSize, 1, 8),
                CookDays = Math.Clamp(state.CookDays, 1, 7),
                PortionSize = state.PortionSize ?? string.Empty,
                DietaryModes = state.DietaryModes?
                    .Where(mode => !string.IsNullOrWhiteSpace(mode))
                    .Select(mode => mode.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [],
                DislikesOrAllergens = state.DislikesOrAllergens ?? string.Empty,
                CustomAisleOrder = state.CustomAisleOrder ?? string.Empty,
                PantryItems = state.PantryItems ?? string.Empty,
                PreferQuickMeals = state.PreferQuickMeals
            };
        }
        catch
        {
            return null;
        }
    }

    private void PersistCurrentPlanState(AislePilotPlanResultViewModel result)
    {
        var mealNames = result.MealPlan
            .Select(meal => meal.MealName?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();
        if (mealNames.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(mealNames, SetupStateJsonOptions);
        if (payload.Length > 3500)
        {
            return;
        }

        Response.Cookies.Append(
            CurrentPlanStateCookieName,
            payload,
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(3),
                IsEssential = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
    }

    private IReadOnlyList<string>? TryReadCurrentPlanState()
    {
        if (!Request.Cookies.TryGetValue(CurrentPlanStateCookieName, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var mealNames = JsonSerializer.Deserialize<List<string>>(payload, SetupStateJsonOptions);
            return NormalizeCurrentPlanMealNames(mealNames);
        }
        catch
        {
            return null;
        }
    }

    private void ValidateRequest(AislePilotRequestModel request)
    {
        var supermarkets = aislePilotService.GetSupportedSupermarkets();
        var portionSizes = aislePilotService.GetSupportedPortionSizes();
        var dietaryModes = aislePilotService.GetSupportedDietaryModes();

        if (!supermarkets.Contains(request.Supermarket, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Request.Supermarket", "Select a supported supermarket.");
        }

        if (!portionSizes.Contains(request.PortionSize, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Request.PortionSize", "Select a supported portion size.");
        }

        var unsupportedDietaryModes = request.DietaryModes
            .Where(x => !dietaryModes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unsupportedDietaryModes.Count > 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "One or more dietary options were not recognised.");
        }

        if (request.DietaryModes.Count == 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "Choose at least one dietary mode.");
        }

        if (string.Equals(request.Supermarket, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            var customAisles = ParseCustomAisles(request.CustomAisleOrder);
            if (customAisles.Count < 3)
            {
                ModelState.AddModelError(
                    "Request.CustomAisleOrder",
                    "Add at least 3 comma-separated aisles when using Custom supermarket.");
            }
        }

        if (ModelState.ErrorCount == 0 && !aislePilotService.HasCompatibleMeals(request))
        {
            ModelState.AddModelError(
                "Request.DietaryModes",
                "No meals match your dietary modes and dislike/allergen notes. Remove one constraint and try again.");
        }
    }

    private void ValidateRequestForSuggestions(AislePilotRequestModel request)
    {
        var supermarkets = aislePilotService.GetSupportedSupermarkets();
        var portionSizes = aislePilotService.GetSupportedPortionSizes();
        var dietaryModes = aislePilotService.GetSupportedDietaryModes();

        if (!supermarkets.Contains(request.Supermarket, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Request.Supermarket", "Select a supported supermarket.");
        }

        if (!portionSizes.Contains(request.PortionSize, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Request.PortionSize", "Select a supported portion size.");
        }

        var unsupportedDietaryModes = request.DietaryModes
            .Where(x => !dietaryModes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unsupportedDietaryModes.Count > 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "One or more dietary options were not recognised.");
        }

        if (request.DietaryModes.Count == 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "Choose at least one dietary mode.");
        }

        if (string.IsNullOrWhiteSpace(request.PantryItems))
        {
            ModelState.AddModelError("Request.PantryItems", "Add a few pantry ingredients to get meal suggestions.");
        }
    }

    private static IReadOnlyList<string> ParseCustomAisles(string? customAisleOrder)
    {
        return (customAisleOrder ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class AislePilotSetupStateCookieModel
    {
        public string? Supermarket { get; set; }
        public decimal WeeklyBudget { get; set; } = 65m;
        public int HouseholdSize { get; set; } = 2;
        public int CookDays { get; set; } = 7;
        public string? PortionSize { get; set; }
        public List<string> DietaryModes { get; set; } = [];
        public string? DislikesOrAllergens { get; set; }
        public string? CustomAisleOrder { get; set; }
        public string? PantryItems { get; set; }
        public bool PreferQuickMeals { get; set; } = true;
    }
}
