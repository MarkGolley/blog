using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;
using MyBlog.Services;

namespace MyBlog.Controllers;

[Route("projects/aisle-pilot")]
public partial class AislePilotController : Controller
{
    private readonly IAislePilotService aislePilotService;
    private readonly IAislePilotExportService aislePilotExportService;
    private readonly ILogger<AislePilotController> logger;

    public AislePilotController(
        IAislePilotService aislePilotService,
        IAislePilotExportService aislePilotExportService,
        ILogger<AislePilotController> logger)
    {
        this.aislePilotService = aislePilotService;
        this.aislePilotExportService = aislePilotExportService;
        this.logger = logger;
    }
    private const string SetupStateCookieName = "aislepilot.setup.v1";
    private const string CurrentPlanStateCookieName = "aislepilot.plan.v1";
    private const string SavedWeeksStateCookieName = "aislepilot.weeks.v1";
    private const string AislePilotImagePathPrefix = "/projects/aisle-pilot/images";
    private const int DefaultMealsPerDay = 3;
    private const int MinMealsPerDay = 1;
    private const int MaxMealsPerDay = 3;
    private const int MaxSwapMealSlotIndex = 20;
    private const int MaxIgnoredMealSlotIndex = 20;
    private const int MaxSavedEnjoyedMealNames = 32;
    private const int MaxSavedMealNameLength = 90;
    private const int MaxSavedWeeks = 6;
    private const int MaxSavedWeekCookiePayloadLength = 3500;
    private static readonly string[] MealTypeSlotOrder = ["Breakfast", "Lunch", "Dinner"];
    private static readonly JsonSerializerOptions SetupStateJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [HttpGet("")]
    public IActionResult Index(string? returnUrl = null)
    {
        var request = NormalizeRequest(TryReadSavedSetupState() ?? new AislePilotRequestModel
        {
            MealsPerDay = DefaultMealsPerDay
        });
        var resolvedReturnUrl = ResolveReturnUrl(returnUrl);
        return View(BuildPageModel(request, returnUrl: resolvedReturnUrl));
    }

    [HttpPost("")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        AislePilotPageViewModel pageModel,
        bool forceRefreshWeek,
        List<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var previousRequest = NormalizeRequest(TryReadSavedSetupState() ?? new AislePilotRequestModel
        {
            MealsPerDay = DefaultMealsPerDay
        });
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
            var resolvedCurrentPlanMealNames = ResolveCurrentPlanMealNames(currentPlanMealNames);
            var shouldForceRefreshWeek =
                forceRefreshWeek &&
                resolvedCurrentPlanMealNames is { Count: > 0 };
            if (shouldForceRefreshWeek)
            {
                request.SwapHistoryState = string.Empty;
                request.IgnoredMealSlotIndexesCsv = string.Empty;
                request.PantrySuggestionHistoryState = string.Empty;
            }
            var shouldRecalculateCurrentPlan =
                !shouldForceRefreshWeek &&
                resolvedCurrentPlanMealNames is { Count: > 0 } &&
                ShouldRecalculateCurrentPlan(previousRequest, request);

            AislePilotPlanResultViewModel result;
            if (shouldForceRefreshWeek)
            {
                result = await aislePilotService.BuildPlanAvoidingMealsAsync(
                    request,
                    resolvedCurrentPlanMealNames!,
                    cancellationToken);
            }
            else if (shouldRecalculateCurrentPlan)
            {
                try
                {
                    result = await aislePilotService.BuildPlanFromCurrentMealsAsync(
                        request,
                        resolvedCurrentPlanMealNames!,
                        cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogInformation(
                        ex,
                        "AislePilot could not recalculate plan from current meals. Falling back to full regeneration.");
                    result = await aislePilotService.BuildPlanAsync(request, cancellationToken);
                }
            }
            else
            {
                result = await aislePilotService.BuildPlanAsync(request, cancellationToken);
            }

            SyncRequestWithResult(request, result);
            RefreshResultModelState();
            PersistSetupState(request);
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
            SyncRequestWithResult(request, result);
            RefreshResultModelState();
            PersistSetupState(request);
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
    public IActionResult SuggestFromPantry(
        AislePilotPageViewModel pageModel,
        List<string>? excludedMealNames,
        string? swapCurrentMealName = null)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        return BuildPantrySuggestionResponse(request, resolvedReturnUrl, excludedMealNames, swapCurrentMealName);
    }

    [HttpPost("swap-pantry-suggestion")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public IActionResult SwapPantrySuggestion(
        AislePilotPageViewModel pageModel,
        string? currentMealName,
        List<string>? currentSuggestionMealNames)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        return BuildPantrySuggestionResponse(
            request,
            resolvedReturnUrl,
            excludedMealNames: null,
            swapCurrentMealName: currentMealName,
            currentSuggestionMealNames: currentSuggestionMealNames);
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
        var cookDays = Math.Clamp(request.CookDays, 1, Math.Clamp(request.PlanDays, 1, 7));
        var mealSlotCount = cookDays * Math.Clamp(request.MealsPerDay, MinMealsPerDay, MaxMealsPerDay);

        if (dayIndex < 0 || dayIndex >= mealSlotCount)
        {
            ModelState.AddModelError(string.Empty, "Selected day was out of range. Try generating again.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        var seenMealNames = GetSeenMealsForSwap(request.SwapHistoryState, currentMealName);

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
            SyncRequestWithResult(request, result);
            RefreshResultModelState();
            PersistSetupState(request);
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

    [HttpPost("swap-dessert")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SwapDessert(
        AislePilotPageViewModel pageModel,
        string? currentDessertAddOnName,
        List<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        ValidateRequest(request);
        var cookDays = Math.Clamp(request.CookDays, 1, Math.Clamp(request.PlanDays, 1, 7));
        var mealSlotCount = cookDays * Math.Clamp(request.MealsPerDay, MinMealsPerDay, MaxMealsPerDay);
        if (!request.IncludeDessertAddOn)
        {
            ModelState.AddModelError(string.Empty, "Dessert add-on is not enabled for this plan.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        try
        {
            var resolvedCurrentPlanMealNames = ResolveCurrentPlanMealNames(currentPlanMealNames);
            if (resolvedCurrentPlanMealNames is null || resolvedCurrentPlanMealNames.Count != mealSlotCount)
            {
                throw new InvalidOperationException("Could not resolve the current plan for dessert swap. Generate a fresh plan and try again.");
            }

            var currentPlanResult = await aislePilotService.BuildPlanFromCurrentMealsAsync(
                request,
                resolvedCurrentPlanMealNames,
                cancellationToken);
            var currentDessertName = string.IsNullOrWhiteSpace(currentDessertAddOnName)
                ? string.IsNullOrWhiteSpace(request.SelectedDessertAddOnName)
                    ? currentPlanResult.DessertAddOnName
                    : request.SelectedDessertAddOnName
                : currentDessertAddOnName.Trim();
            request.SelectedDessertAddOnName = aislePilotService.ResolveNextDessertAddOnName(currentDessertName);
            var result = await aislePilotService.BuildPlanFromCurrentMealsAsync(
                request,
                resolvedCurrentPlanMealNames,
                cancellationToken);
            SyncRequestWithResult(request, result);
            RefreshResultModelState();
            PersistSetupState(request);
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
            logger.LogError(ex, "AislePilot swap dessert failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Dessert swap hit a temporary issue. Please retry.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("ignore-meal")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IgnoreMeal(
        AislePilotPageViewModel pageModel,
        int dayIndex,
        List<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        ValidateRequest(request);
        var cookDays = Math.Clamp(request.CookDays, 1, Math.Clamp(request.PlanDays, 1, 7));
        var mealSlotCount = cookDays * Math.Clamp(request.MealsPerDay, MinMealsPerDay, MaxMealsPerDay);

        if (dayIndex < 0 || dayIndex >= mealSlotCount)
        {
            ModelState.AddModelError(string.Empty, "Selected day was out of range. Try generating again.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        try
        {
            var resolvedCurrentPlanMealNames = ResolveCurrentPlanMealNames(currentPlanMealNames);
            if (resolvedCurrentPlanMealNames is null || resolvedCurrentPlanMealNames.Count != mealSlotCount)
            {
                throw new InvalidOperationException("Could not resolve the current plan for ignore changes. Generate a fresh plan and try again.");
            }

            request.IgnoredMealSlotIndexesCsv = ToggleIgnoredMealSlotIndex(
                request.IgnoredMealSlotIndexesCsv,
                dayIndex,
                mealSlotCount);
            var result = await aislePilotService.BuildPlanFromCurrentMealsAsync(
                request,
                resolvedCurrentPlanMealNames,
                cancellationToken);
            SyncRequestWithResult(request, result);
            RefreshResultModelState();
            PersistSetupState(request);
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
            logger.LogError(ex, "AislePilot ignore meal failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Meal update hit a temporary issue. Please retry.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("toggle-enjoyed-meal")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleEnjoyedMeal(
        AislePilotPageViewModel pageModel,
        string? mealName,
        List<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);

        var normalizedMealName = string.IsNullOrWhiteSpace(mealName)
            ? string.Empty
            : mealName.Trim();
        if (normalizedMealName.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Could not save meal right now. Regenerate and try again.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        request.SavedEnjoyedMealNamesState = ToggleSavedEnjoyedMealNameState(
            request.SavedEnjoyedMealNamesState,
            normalizedMealName);
        PersistSetupState(request);
        RefreshResultModelState();

        try
        {
            var resolvedCurrentPlanMealNames = ResolveCurrentPlanMealNames(currentPlanMealNames);
            if (resolvedCurrentPlanMealNames is null || resolvedCurrentPlanMealNames.Count == 0)
            {
                return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
            }

            var result = await aislePilotService.BuildPlanFromCurrentMealsAsync(
                request,
                resolvedCurrentPlanMealNames,
                cancellationToken);
            SyncRequestWithResult(request, result);
            RefreshResultModelState();
            PersistSetupState(request);
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
            logger.LogInformation(ex, "AislePilot could not refresh current plan after toggling enjoyed meal.");
            ModelState.AddModelError(
                string.Empty,
                "Saved meal preference was updated, but this plan needs a fresh regenerate.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot toggle enjoyed meal failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Saved meal preference was updated, but the plan could not refresh right now.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("save-week")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveWeek(
        AislePilotPageViewModel pageModel,
        string? weekLabel,
        List<string>? currentPlanMealNames,
        CancellationToken cancellationToken)
    {
        var request = NormalizeRequest(pageModel.Request);
        PersistSetupState(request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);

        try
        {
            var resolvedCurrentPlanMealNames = ResolveCurrentPlanMealNames(currentPlanMealNames);
            if (resolvedCurrentPlanMealNames is null || resolvedCurrentPlanMealNames.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Could not save this week yet. Generate a plan first.");
                return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
            }

            var savedWeekSnapshots = PersistSavedWeekState(request, resolvedCurrentPlanMealNames, weekLabel);
            var result = await aislePilotService.BuildPlanFromCurrentMealsAsync(
                request,
                resolvedCurrentPlanMealNames,
                cancellationToken);
            SyncRequestWithResult(request, result);
            RefreshResultModelState();
            PersistSetupState(request);
            PersistCurrentPlanState(result);
            var savedWeeks = BuildSavedWeekSummaries(savedWeekSnapshots);
            return View("Index", BuildPageModel(request, result, returnUrl: resolvedReturnUrl, savedWeeks: savedWeeks));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot save week failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Saving this week hit a temporary issue. Please retry.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("open-week")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpenWeek(
        string? weekId,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var resolvedReturnUrl = ResolveReturnUrl(returnUrl);
        var currentRequest = NormalizeRequest(TryReadSavedSetupState() ?? new AislePilotRequestModel
        {
            MealsPerDay = DefaultMealsPerDay
        });

        var normalizedWeekId = string.IsNullOrWhiteSpace(weekId) ? string.Empty : weekId.Trim();
        var savedWeeks = TryReadSavedWeekState();
        var selectedWeek = savedWeeks.FirstOrDefault(snapshot =>
            snapshot.WeekId.Equals(normalizedWeekId, StringComparison.OrdinalIgnoreCase));
        if (selectedWeek is null)
        {
            ModelState.AddModelError(string.Empty, "Saved week was not found. Save a new week and try again.");
            return View("Index", BuildPageModel(currentRequest, returnUrl: resolvedReturnUrl));
        }

        try
        {
            var request = NormalizeRequest(selectedWeek.ToRequestModel());
            PersistSetupState(request);
            var result = await aislePilotService.BuildPlanFromCurrentMealsAsync(
                request,
                selectedWeek.CurrentPlanMealNames,
                cancellationToken);
            SyncRequestWithResult(request, result);
            RefreshResultModelState();
            PersistSetupState(request);
            PersistCurrentPlanState(result);
            return View("Index", BuildPageModel(request, result, returnUrl: resolvedReturnUrl));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Index", BuildPageModel(currentRequest, returnUrl: resolvedReturnUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot open saved week failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Could not open that saved week right now. Please retry.");
            return View("Index", BuildPageModel(currentRequest, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("delete-week")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteWeek(string? weekId, string? returnUrl)
    {
        var resolvedReturnUrl = ResolveReturnUrl(returnUrl);
        var currentRequest = NormalizeRequest(TryReadSavedSetupState() ?? new AislePilotRequestModel
        {
            MealsPerDay = DefaultMealsPerDay
        });

        var normalizedWeekId = string.IsNullOrWhiteSpace(weekId) ? string.Empty : weekId.Trim();
        if (normalizedWeekId.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Saved week id was missing.");
            return View("Index", BuildPageModel(currentRequest, returnUrl: resolvedReturnUrl));
        }

        var snapshots = TryReadSavedWeekState().ToList();
        var removedCount = snapshots.RemoveAll(snapshot =>
            snapshot.WeekId.Equals(normalizedWeekId, StringComparison.OrdinalIgnoreCase));
        if (removedCount == 0)
        {
            ModelState.AddModelError(string.Empty, "Saved week was not found.");
        }

        var persistedSnapshots = PersistSavedWeekStateSnapshots(snapshots);
        return View(
            "Index",
            BuildPageModel(
                currentRequest,
                returnUrl: resolvedReturnUrl,
                savedWeeks: BuildSavedWeekSummaries(persistedSnapshots)));
    }

    [HttpPost("remove-saved-meal")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveSavedMeal(
        string? mealName,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var resolvedReturnUrl = ResolveReturnUrl(returnUrl);
        var request = NormalizeRequest(TryReadSavedSetupState() ?? new AislePilotRequestModel
        {
            MealsPerDay = DefaultMealsPerDay
        });

        var normalizedMealName = string.IsNullOrWhiteSpace(mealName)
            ? string.Empty
            : mealName.Trim();
        if (normalizedMealName.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Saved meal name was missing.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        request.SavedEnjoyedMealNamesState = RemoveSavedEnjoyedMealNameState(
            request.SavedEnjoyedMealNamesState,
            normalizedMealName);
        PersistSetupState(request);
        RefreshResultModelState();

        try
        {
            var resolvedCurrentPlanMealNames = ResolveCurrentPlanMealNames(null);
            if (resolvedCurrentPlanMealNames is null || resolvedCurrentPlanMealNames.Count == 0)
            {
                return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
            }

            var result = await aislePilotService.BuildPlanFromCurrentMealsAsync(
                request,
                resolvedCurrentPlanMealNames,
                cancellationToken);
            SyncRequestWithResult(request, result);
            RefreshResultModelState();
            PersistSetupState(request);
            PersistCurrentPlanState(result);
            return View("Index", BuildPageModel(request, result, returnUrl: resolvedReturnUrl));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogInformation(ex, "AislePilot could not refresh current plan after removing a saved meal.");
            ModelState.AddModelError(
                string.Empty,
                "Saved meal was removed, but this plan needs a fresh regenerate.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AislePilot remove saved meal failed unexpectedly.");
            ModelState.AddModelError(
                string.Empty,
                "Saved meal was removed, but the plan could not refresh right now.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

}
