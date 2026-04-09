using System.Globalization;
using System.Text;
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
            var shouldRecalculateCurrentPlan =
                resolvedCurrentPlanMealNames is { Count: > 0 } &&
                ShouldRecalculateCurrentPlan(previousRequest, request);

            AislePilotPlanResultViewModel result;
            if (shouldRecalculateCurrentPlan)
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

    private static bool ShouldRecalculateCurrentPlan(
        AislePilotRequestModel previousRequest,
        AislePilotRequestModel nextRequest)
    {
        if (!HasSameMealCompatibilitySettings(previousRequest, nextRequest))
        {
            return false;
        }

        return previousRequest.HouseholdSize != nextRequest.HouseholdSize ||
               previousRequest.WeeklyBudget != nextRequest.WeeklyBudget ||
               !string.Equals(
                   previousRequest.PortionSize,
                   nextRequest.PortionSize,
                   StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(
                   previousRequest.LeftoverCookDayIndexesCsv,
                   nextRequest.LeftoverCookDayIndexesCsv,
                   StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(
                   previousRequest.IgnoredMealSlotIndexesCsv,
                   nextRequest.IgnoredMealSlotIndexesCsv,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSameMealCompatibilitySettings(
        AislePilotRequestModel previousRequest,
        AislePilotRequestModel nextRequest)
    {
        return string.Equals(previousRequest.Supermarket, nextRequest.Supermarket, StringComparison.OrdinalIgnoreCase) &&
               previousRequest.PlanDays == nextRequest.PlanDays &&
               previousRequest.MealsPerDay == nextRequest.MealsPerDay &&
               AreEquivalentSelections(previousRequest.SelectedMealTypes, nextRequest.SelectedMealTypes) &&
               AreEquivalentSelections(previousRequest.DietaryModes, nextRequest.DietaryModes) &&
               string.Equals(
                   previousRequest.DislikesOrAllergens,
                   nextRequest.DislikesOrAllergens,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   previousRequest.CustomAisleOrder,
                   nextRequest.CustomAisleOrder,
                   StringComparison.OrdinalIgnoreCase) &&
               previousRequest.PreferQuickMeals == nextRequest.PreferQuickMeals &&
               previousRequest.IncludeSpecialTreatMeal == nextRequest.IncludeSpecialTreatMeal &&
               previousRequest.SelectedSpecialTreatCookDayIndex == nextRequest.SelectedSpecialTreatCookDayIndex &&
               previousRequest.IncludeDessertAddOn == nextRequest.IncludeDessertAddOn &&
               string.Equals(
                   previousRequest.SelectedDessertAddOnName,
                   nextRequest.SelectedDessertAddOnName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreEquivalentSelections(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right)
    {
        var leftValues = (left ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rightValues = (right ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return leftValues.Count == rightValues.Count &&
               leftValues.All(value => rightValues.Contains(value, StringComparer.OrdinalIgnoreCase));
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
                imageUrl = ResolveClientMealImageUrl(imageUrls.GetValueOrDefault(name, string.Empty))
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

    private AislePilotPageViewModel BuildPageModel(
        AislePilotRequestModel request,
        AislePilotPlanResultViewModel? result = null,
        IReadOnlyList<AislePilotPantrySuggestionViewModel>? pantrySuggestions = null,
        string returnUrl = "",
        IReadOnlyList<AislePilotSavedWeekSummaryViewModel>? savedWeeks = null)
    {
        return new AislePilotPageViewModel
        {
            Request = request,
            ReturnUrl = returnUrl,
            Result = result,
            SavedWeeks = savedWeeks ?? BuildSavedWeekSummaries(TryReadSavedWeekState()),
            SavedMeals = ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
            PantrySuggestions = pantrySuggestions ?? [],
            SupermarketOptions = aislePilotService.GetSupportedSupermarkets(),
            PortionSizeOptions = aislePilotService.GetSupportedPortionSizes(),
            DietaryOptions = aislePilotService.GetSupportedDietaryModes(),
            MealImagePollingEnabled = aislePilotService.CanGenerateMealImages()
        };
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

        ValidateMealTypeSelection(request);
        if (request.IncludeSpecialTreatMeal &&
            !request.SelectedMealTypes.Contains("Dinner", StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                "Request.IncludeSpecialTreatMeal",
                "Special treat requires the Dinner meal slot.");
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

    private void ValidateMealTypeSelection(AislePilotRequestModel request)
    {
        var selectedMealTypes = NormalizeSelectedMealTypes(request.SelectedMealTypes);
        request.SelectedMealTypes = selectedMealTypes;
        if (selectedMealTypes.Count > 0)
        {
            request.MealsPerDay = selectedMealTypes.Count;
        }

        if (selectedMealTypes.Count < MinMealsPerDay || selectedMealTypes.Count > MaxMealsPerDay)
        {
            ModelState.AddModelError(
                "Request.SelectedMealTypes",
                "Choose 1 to 3 meal slots (Breakfast, Lunch, Dinner).");
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

        ValidateMealTypeSelection(request);

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

    private static string ResolveClientMealImageUrl(string? imageUrl)
    {
        var normalized = imageUrl?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return $"{AislePilotImagePathPrefix}/aislepilot-icon.svg";
        }

        if (normalized.StartsWith($"{AislePilotImagePathPrefix}/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("/images/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{AislePilotImagePathPrefix}/{normalized["/images/".Length..]}";
        }

        normalized = normalized.Replace('\\', '/');
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            var trimmed = normalized.TrimStart('/');
            if (trimmed.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            {
                return $"{AislePilotImagePathPrefix}/{trimmed["images/".Length..]}";
            }

            if (trimmed.StartsWith("aislepilot-meals/", StringComparison.OrdinalIgnoreCase))
            {
                return $"{AislePilotImagePathPrefix}/{trimmed}";
            }

            var hasImageExtension =
                trimmed.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            if (hasImageExtension)
            {
                return $"{AislePilotImagePathPrefix}/aislepilot-meals/{trimmed}";
            }
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return normalized;
        }

        return normalized;
    }

}
