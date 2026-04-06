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

    private IReadOnlyList<AislePilotSavedWeekCookieModel> PersistSavedWeekState(
        AislePilotRequestModel request,
        IReadOnlyList<string> currentPlanMealNames,
        string? weekLabel)
    {
        var normalizedMealNames = NormalizeCurrentPlanMealNames(currentPlanMealNames);
        if (normalizedMealNames is null || normalizedMealNames.Count == 0)
        {
            return [];
        }

        var snapshots = TryReadSavedWeekState().ToList();
        snapshots.RemoveAll(existing => AreEquivalentSelections(existing.CurrentPlanMealNames, normalizedMealNames));
        snapshots.Insert(0, new AislePilotSavedWeekCookieModel
        {
            WeekId = Guid.NewGuid().ToString("N")[..10],
            Label = BuildSavedWeekLabel(weekLabel),
            SavedAtUtc = DateTimeOffset.UtcNow,
            RequestSnapshot = BuildSavedWeekRequestSnapshot(request),
            CurrentPlanMealNames = normalizedMealNames.ToList()
        });

        if (snapshots.Count > MaxSavedWeeks)
        {
            snapshots = snapshots.Take(MaxSavedWeeks).ToList();
        }

        return PersistSavedWeekStateSnapshots(snapshots);
    }

    private IReadOnlyList<AislePilotSavedWeekCookieModel> PersistSavedWeekStateSnapshots(
        IReadOnlyList<AislePilotSavedWeekCookieModel> snapshots)
    {
        var normalizedSnapshots = snapshots
            .Select(NormalizeSavedWeekSnapshot)
            .Where(snapshot => snapshot is not null)
            .Select(snapshot => snapshot!)
            .Take(MaxSavedWeeks)
            .ToList();
        if (normalizedSnapshots.Count == 0)
        {
            Response.Cookies.Delete(SavedWeeksStateCookieName);
            return [];
        }

        while (normalizedSnapshots.Count > 0)
        {
            var payload = JsonSerializer.Serialize(normalizedSnapshots, SetupStateJsonOptions);
            if (payload.Length <= MaxSavedWeekCookiePayloadLength)
            {
                Response.Cookies.Append(
                    SavedWeeksStateCookieName,
                    payload,
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddDays(45),
                        IsEssential = true,
                        HttpOnly = true,
                        SameSite = SameSiteMode.Lax,
                        Secure = Request.IsHttps
                    });
                return normalizedSnapshots;
            }

            normalizedSnapshots.RemoveAt(normalizedSnapshots.Count - 1);
        }

        Response.Cookies.Delete(SavedWeeksStateCookieName);
        return [];
    }

    private IReadOnlyList<AislePilotSavedWeekCookieModel> TryReadSavedWeekState()
    {
        if (!Request.Cookies.TryGetValue(SavedWeeksStateCookieName, out var payload) || string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<AislePilotSavedWeekCookieModel>>(payload, SetupStateJsonOptions) ?? [];
            var normalized = parsed
                .Select(NormalizeSavedWeekSnapshot)
                .Where(snapshot => snapshot is not null)
                .Select(snapshot => snapshot!)
                .OrderByDescending(snapshot => snapshot.SavedAtUtc)
                .ToList();
            return normalized;
        }
        catch
        {
            return [];
        }
    }

    private static AislePilotSavedWeekCookieModel? NormalizeSavedWeekSnapshot(AislePilotSavedWeekCookieModel? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var normalizedMealNames = NormalizeCurrentPlanMealNames(snapshot.CurrentPlanMealNames);
        if (normalizedMealNames is null || normalizedMealNames.Count == 0)
        {
            return null;
        }

        var normalizedWeekId = string.IsNullOrWhiteSpace(snapshot.WeekId)
            ? Guid.NewGuid().ToString("N")[..10]
            : snapshot.WeekId.Trim();
        var normalizedSavedAtUtc = snapshot.SavedAtUtc == default
            ? DateTimeOffset.UtcNow
            : snapshot.SavedAtUtc;

        var requestSnapshot = NormalizeSavedWeekRequestSnapshot(snapshot.RequestSnapshot);

        return new AislePilotSavedWeekCookieModel
        {
            WeekId = normalizedWeekId,
            Label = BuildSavedWeekLabel(snapshot.Label),
            SavedAtUtc = normalizedSavedAtUtc,
            RequestSnapshot = requestSnapshot,
            CurrentPlanMealNames = normalizedMealNames.ToList()
        };
    }

    private static AislePilotSavedWeekRequestCookieModel BuildSavedWeekRequestSnapshot(AislePilotRequestModel request)
    {
        return NormalizeSavedWeekRequestSnapshot(new AislePilotSavedWeekRequestCookieModel
        {
            Supermarket = request.Supermarket,
            WeeklyBudget = request.WeeklyBudget,
            HouseholdSize = request.HouseholdSize,
            CookDays = request.CookDays,
            PlanDays = request.PlanDays,
            MealsPerDay = request.MealsPerDay,
            SelectedMealTypes = request.SelectedMealTypes.ToList(),
            PortionSize = request.PortionSize,
            DietaryModes = request.DietaryModes.ToList(),
            DislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty,
            CustomAisleOrder = request.CustomAisleOrder ?? string.Empty,
            LeftoverCookDayIndexesCsv = request.LeftoverCookDayIndexesCsv ?? string.Empty,
            IgnoredMealSlotIndexesCsv = request.IgnoredMealSlotIndexesCsv ?? string.Empty,
            PreferQuickMeals = request.PreferQuickMeals,
            IncludeSpecialTreatMeal = request.IncludeSpecialTreatMeal,
            SelectedSpecialTreatCookDayIndex = request.SelectedSpecialTreatCookDayIndex,
            IncludeDessertAddOn = request.IncludeDessertAddOn,
            SelectedDessertAddOnName = request.SelectedDessertAddOnName ?? string.Empty
        });
    }

    private static AislePilotSavedWeekRequestCookieModel NormalizeSavedWeekRequestSnapshot(
        AislePilotSavedWeekRequestCookieModel? requestSnapshot)
    {
        var snapshot = requestSnapshot ?? new AislePilotSavedWeekRequestCookieModel();
        snapshot.Supermarket = (snapshot.Supermarket ?? string.Empty).Trim();
        snapshot.PortionSize = (snapshot.PortionSize ?? string.Empty).Trim();
        snapshot.DislikesOrAllergens = Truncate((snapshot.DislikesOrAllergens ?? string.Empty).Trim(), 260);
        snapshot.CustomAisleOrder = Truncate((snapshot.CustomAisleOrder ?? string.Empty).Trim(), 320);
        snapshot.LeftoverCookDayIndexesCsv = Truncate((snapshot.LeftoverCookDayIndexesCsv ?? string.Empty).Trim(), 140);
        snapshot.IgnoredMealSlotIndexesCsv = Truncate((snapshot.IgnoredMealSlotIndexesCsv ?? string.Empty).Trim(), 300);
        snapshot.SelectedDessertAddOnName = Truncate((snapshot.SelectedDessertAddOnName ?? string.Empty).Trim(), 120);
        snapshot.WeeklyBudget = Math.Clamp(snapshot.WeeklyBudget, 15m, 600m);
        snapshot.HouseholdSize = Math.Clamp(snapshot.HouseholdSize, 1, 8);
        snapshot.PlanDays = Math.Clamp(snapshot.PlanDays, 1, 7);
        snapshot.CookDays = Math.Clamp(snapshot.CookDays, 1, snapshot.PlanDays);
        snapshot.MealsPerDay = Math.Clamp(snapshot.MealsPerDay, MinMealsPerDay, MaxMealsPerDay);
        snapshot.SelectedMealTypes = NormalizeSelectedMealTypes(snapshot.SelectedMealTypes).ToList();
        snapshot.DietaryModes = (snapshot.DietaryModes ?? [])
            .Where(mode => !string.IsNullOrWhiteSpace(mode))
            .Select(mode => mode.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        snapshot.SelectedSpecialTreatCookDayIndex = snapshot.SelectedSpecialTreatCookDayIndex.HasValue
            ? Math.Clamp(snapshot.SelectedSpecialTreatCookDayIndex.Value, 0, 6)
            : null;
        return snapshot;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static string BuildSavedWeekLabel(string? requestedLabel)
    {
        var normalized = string.IsNullOrWhiteSpace(requestedLabel)
            ? string.Empty
            : requestedLabel.Trim();
        if (normalized.Length > 0)
        {
            return Truncate(normalized, 42);
        }

        var now = DateTimeOffset.UtcNow;
        return $"Week of {now:dd MMM yyyy}";
    }

    private static IReadOnlyList<AislePilotSavedWeekSummaryViewModel> BuildSavedWeekSummaries(
        IReadOnlyList<AislePilotSavedWeekCookieModel> snapshots)
    {
        return snapshots
            .Select(snapshot => new AislePilotSavedWeekSummaryViewModel
            {
                WeekId = snapshot.WeekId,
                Label = snapshot.Label,
                SavedAtUtc = snapshot.SavedAtUtc,
                PlanDays = Math.Clamp(snapshot.RequestSnapshot.PlanDays, 1, 7),
                MealCount = snapshot.CurrentPlanMealNames.Count,
                Supermarket = snapshot.RequestSnapshot.Supermarket
            })
            .ToList();
    }

    private static void SyncRequestWithResult(AislePilotRequestModel request, AislePilotPlanResultViewModel result)
    {
        if (!request.IncludeDessertAddOn)
        {
            request.SelectedDessertAddOnName = string.Empty;
            return;
        }

        if (result.IncludeDessertAddOn && !string.IsNullOrWhiteSpace(result.DessertAddOnName))
        {
            request.SelectedDessertAddOnName = result.DessertAddOnName;
        }
    }

    private void RefreshResultModelState()
    {
        ModelState.Remove("Request.SelectedDessertAddOnName");
        ModelState.Remove("Request.SelectedSpecialTreatCookDayIndex");
        ModelState.Remove("Request.SavedEnjoyedMealNamesState");
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
        var selectedMealTypesWereSubmitted = Request.HasFormContentType &&
                                             Request.Form.ContainsKey("Request.SelectedMealTypes");
        normalized.Supermarket = normalized.Supermarket?.Trim() ?? string.Empty;
        normalized.PortionSize = normalized.PortionSize?.Trim() ?? string.Empty;
        normalized.CustomAisleOrder = normalized.CustomAisleOrder?.Trim() ?? string.Empty;
        normalized.DislikesOrAllergens = normalized.DislikesOrAllergens?.Trim() ?? string.Empty;
        normalized.PantryItems = normalized.PantryItems?.Trim() ?? string.Empty;
        normalized.SelectedDessertAddOnName = normalized.SelectedDessertAddOnName?.Trim() ?? string.Empty;
        normalized.LeftoverCookDayIndexesCsv = normalized.LeftoverCookDayIndexesCsv?.Trim() ?? string.Empty;
        normalized.SwapHistoryState = normalized.SwapHistoryState?.Trim() ?? string.Empty;
        normalized.IgnoredMealSlotIndexesCsv =
            NormalizeIgnoredMealSlotIndexesCsv(normalized.IgnoredMealSlotIndexesCsv);
        normalized.PantrySuggestionHistoryState = normalized.PantrySuggestionHistoryState?.Trim() ?? string.Empty;
        normalized.SavedEnjoyedMealNamesState = SerializeSavedEnjoyedMealNameState(
            ParseSavedEnjoyedMealNamesState(normalized.SavedEnjoyedMealNamesState));
        normalized.DietaryModes = normalized.DietaryModes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        normalized.SavedMealRepeatRatePercent = Math.Clamp(normalized.SavedMealRepeatRatePercent, 10, 100);
        normalized.PlanDays = Math.Clamp(normalized.PlanDays, 1, 7);
        var maxLeftoverDays = Math.Max(0, normalized.PlanDays - 1);
        var normalizedLeftoverSourceDays = ParseLeftoverSourceDayIndexes(
            normalized.LeftoverCookDayIndexesCsv,
            normalized.PlanDays,
            maxLeftoverDays);
        normalized.LeftoverCookDayIndexesCsv = string.Join(",", normalizedLeftoverSourceDays);
        normalized.CookDays = Math.Clamp(
            normalized.PlanDays - normalizedLeftoverSourceDays.Count,
            1,
            normalized.PlanDays);
        if (normalized.SelectedSpecialTreatCookDayIndex.HasValue)
        {
            normalized.SelectedSpecialTreatCookDayIndex = Math.Clamp(
                normalized.SelectedSpecialTreatCookDayIndex.Value,
                0,
                normalized.CookDays - 1);
        }

        if (normalized.IncludeSpecialTreatMeal && !normalized.SelectedSpecialTreatCookDayIndex.HasValue)
        {
            normalized.SelectedSpecialTreatCookDayIndex = 0;
        }

        var normalizedSelectedMealTypes = NormalizeSelectedMealTypes(normalized.SelectedMealTypes);
        if (normalizedSelectedMealTypes.Count == 0 && !selectedMealTypesWereSubmitted)
        {
            normalizedSelectedMealTypes = BuildMealTypeSlotsFromMealsPerDay(normalized.MealsPerDay).ToList();
        }

        normalized.SelectedMealTypes = normalizedSelectedMealTypes;
        normalized.MealsPerDay = normalizedSelectedMealTypes.Count > 0
            ? normalizedSelectedMealTypes.Count
            : Math.Clamp(normalized.MealsPerDay, MinMealsPerDay, MaxMealsPerDay);
        return normalized;
    }

    private static List<string> NormalizeSelectedMealTypes(IReadOnlyList<string>? selectedMealTypes)
    {
        if (selectedMealTypes is null || selectedMealTypes.Count == 0)
        {
            return [];
        }

        return selectedMealTypes
            .Select(NormalizeSelectedMealType)
            .Where(type => type is not null)
            .Select(type => type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => Array.IndexOf(MealTypeSlotOrder, type))
            .ToList();
    }

    private static string? NormalizeSelectedMealType(string? mealType)
    {
        var normalized = mealType?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Equals("Breakfast", StringComparison.OrdinalIgnoreCase))
        {
            return "Breakfast";
        }

        if (normalized.Equals("Lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "Lunch";
        }

        if (normalized.Equals("Dinner", StringComparison.OrdinalIgnoreCase))
        {
            return "Dinner";
        }

        return null;
    }

    private static IReadOnlyList<string> BuildMealTypeSlotsFromMealsPerDay(int mealsPerDay)
    {
        var safeMealsPerDay = Math.Clamp(mealsPerDay, MinMealsPerDay, MaxMealsPerDay);
        return safeMealsPerDay switch
        {
            1 => ["Dinner"],
            2 => ["Lunch", "Dinner"],
            _ => ["Breakfast", "Lunch", "Dinner"]
        };
    }

    private static IReadOnlyList<string> GetSeenMealsForSwap(string? swapHistoryState, string? currentMealName)
    {
        var history = ParseSwapHistoryState(swapHistoryState);
        var seenMeals = history.Values
            .SelectMany(static meals => meals)
            .Where(meal => !string.IsNullOrWhiteSpace(meal))
            .Select(meal => meal.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(currentMealName))
        {
            return seenMeals;
        }

        if (!seenMeals.Contains(currentMealName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            seenMeals.Add(currentMealName.Trim());
        }

        return seenMeals;
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

    private static string NormalizeIgnoredMealSlotIndexesCsv(string? ignoredMealSlotIndexesCsv)
    {
        var ignoredIndexes = ParseIgnoredMealSlotIndexes(
            ignoredMealSlotIndexesCsv,
            maxAllowedIndex: MaxIgnoredMealSlotIndex);
        return SerializeIgnoredMealSlotIndexes(ignoredIndexes);
    }

    private static string ToggleIgnoredMealSlotIndex(
        string? currentState,
        int dayIndex,
        int mealSlotCount)
    {
        var maxIndex = Math.Clamp(mealSlotCount - 1, 0, MaxIgnoredMealSlotIndex);
        var ignoredIndexes = ParseIgnoredMealSlotIndexes(currentState, maxIndex);
        if (ignoredIndexes.Contains(dayIndex))
        {
            ignoredIndexes.Remove(dayIndex);
        }
        else
        {
            ignoredIndexes.Add(dayIndex);
        }

        return SerializeIgnoredMealSlotIndexes(ignoredIndexes);
    }

    private static HashSet<int> ParseIgnoredMealSlotIndexes(
        string? ignoredMealSlotIndexesCsv,
        int maxAllowedIndex)
    {
        var result = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(ignoredMealSlotIndexesCsv))
        {
            return result;
        }

        foreach (var token in ignoredMealSlotIndexesCsv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, out var slotIndex) ||
                slotIndex < 0 ||
                slotIndex > maxAllowedIndex)
            {
                continue;
            }

            result.Add(slotIndex);
        }

        return result;
    }

    private static List<int> ParseLeftoverSourceDayIndexes(
        string? leftoverCookDayIndexesCsv,
        int planDays,
        int maxCount)
    {
        var normalizedPlanDays = Math.Clamp(planDays, 1, 7);
        var normalizedMaxCount = Math.Max(0, maxCount);
        if (normalizedMaxCount == 0 || string.IsNullOrWhiteSpace(leftoverCookDayIndexesCsv))
        {
            return [];
        }

        var result = new List<int>(normalizedMaxCount);
        var tokens = leftoverCookDayIndexesCsv
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (result.Count >= normalizedMaxCount)
            {
                break;
            }

            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dayIndex))
            {
                continue;
            }

            if (dayIndex < 0 || dayIndex >= normalizedPlanDays)
            {
                continue;
            }

            result.Add(dayIndex);
        }

        return result;
    }

    private static string SerializeIgnoredMealSlotIndexes(IEnumerable<int> ignoredIndexes)
    {
        return string.Join(
            ",",
            ignoredIndexes
                .Distinct()
                .Where(index => index >= 0 && index <= MaxIgnoredMealSlotIndex)
                .OrderBy(index => index));
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
            if (segments.Length != 2 ||
                !int.TryParse(segments[0], out var dayIndex) ||
                dayIndex < 0 ||
                dayIndex > MaxSwapMealSlotIndex)
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
            PlanDays = request.PlanDays,
            MealsPerDay = request.MealsPerDay,
            SelectedMealTypes = request.SelectedMealTypes.ToList(),
            PortionSize = request.PortionSize,
            DietaryModes = request.DietaryModes.ToList(),
            DislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty,
            CustomAisleOrder = request.CustomAisleOrder ?? string.Empty,
            PantryItems = request.PantryItems ?? string.Empty,
            PreferQuickMeals = request.PreferQuickMeals,
            EnableSavedMealRepeats = request.EnableSavedMealRepeats,
            SavedMealRepeatRatePercent = Math.Clamp(request.SavedMealRepeatRatePercent, 10, 100),
            SavedEnjoyedMealNamesState = request.SavedEnjoyedMealNamesState ?? string.Empty,
            RequireCorePantryIngredients = request.RequireCorePantryIngredients,
            IncludeSpecialTreatMeal = request.IncludeSpecialTreatMeal,
            SelectedSpecialTreatCookDayIndex = request.SelectedSpecialTreatCookDayIndex,
            IncludeDessertAddOn = request.IncludeDessertAddOn,
            SelectedDessertAddOnName = request.SelectedDessertAddOnName
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

            var planDays = Math.Clamp(state.PlanDays, 1, 7);
            var selectedMealTypes = NormalizeSelectedMealTypes(state.SelectedMealTypes);
            if (selectedMealTypes.Count == 0)
            {
                selectedMealTypes = BuildMealTypeSlotsFromMealsPerDay(state.MealsPerDay).ToList();
            }

            var mealsPerDay = selectedMealTypes.Count;

            return new AislePilotRequestModel
            {
                Supermarket = state.Supermarket ?? string.Empty,
                WeeklyBudget = Math.Clamp(state.WeeklyBudget, 15m, 600m),
                HouseholdSize = Math.Clamp(state.HouseholdSize, 1, 8),
                CookDays = Math.Clamp(state.CookDays, 1, planDays),
                PlanDays = planDays,
                MealsPerDay = mealsPerDay,
                SelectedMealTypes = selectedMealTypes,
                PortionSize = state.PortionSize ?? string.Empty,
                DietaryModes = state.DietaryModes?
                    .Where(mode => !string.IsNullOrWhiteSpace(mode))
                    .Select(mode => mode.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? [],
                DislikesOrAllergens = state.DislikesOrAllergens ?? string.Empty,
                CustomAisleOrder = state.CustomAisleOrder ?? string.Empty,
                PantryItems = state.PantryItems ?? string.Empty,
                PreferQuickMeals = state.PreferQuickMeals,
                EnableSavedMealRepeats = state.EnableSavedMealRepeats,
                SavedMealRepeatRatePercent = Math.Clamp(state.SavedMealRepeatRatePercent, 10, 100),
                SavedEnjoyedMealNamesState = SerializeSavedEnjoyedMealNameState(
                    ParseSavedEnjoyedMealNamesState(state.SavedEnjoyedMealNamesState)),
                RequireCorePantryIngredients = state.RequireCorePantryIngredients,
                IncludeSpecialTreatMeal = state.IncludeSpecialTreatMeal,
                SelectedSpecialTreatCookDayIndex = state.SelectedSpecialTreatCookDayIndex,
                IncludeDessertAddOn = state.IncludeDessertAddOn,
                SelectedDessertAddOnName = state.SelectedDessertAddOnName
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

    private IActionResult BuildPantrySuggestionResponse(
        AislePilotRequestModel request,
        string resolvedReturnUrl,
        IReadOnlyList<string>? excludedMealNames,
        string? swapCurrentMealName,
        IReadOnlyList<string>? currentSuggestionMealNames = null)
    {
        ValidateRequestForSuggestions(request);

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        var normalizedExcludedMealNames = NormalizePantryMealNames(excludedMealNames);
        var normalizedSwapCurrentMealName = string.IsNullOrWhiteSpace(swapCurrentMealName)
            ? string.Empty
            : swapCurrentMealName.Trim();
        var isSwapRequest = normalizedSwapCurrentMealName.Length > 0;
        var historyMealNames = ParsePantrySuggestionHistoryState(request.PantrySuggestionHistoryState);

        var strictExcludedMealNames = new HashSet<string>(normalizedExcludedMealNames, StringComparer.OrdinalIgnoreCase);
        foreach (var historyMealName in historyMealNames)
        {
            strictExcludedMealNames.Add(historyMealName);
        }

        if (isSwapRequest)
        {
            strictExcludedMealNames.Add(normalizedSwapCurrentMealName);
        }

        var generationNonce = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}"[..28];
        try
        {
            IReadOnlyList<AislePilotPantrySuggestionViewModel> suggestions;
            if (isSwapRequest)
            {
                suggestions = BuildSingleCardSwapSuggestions(
                    request,
                    normalizedSwapCurrentMealName,
                    currentSuggestionMealNames,
                    historyMealNames,
                    generationNonce);

                if (suggestions.Count == 0)
                {
                    var relaxedExcludedMealNames = new HashSet<string>(normalizedExcludedMealNames, StringComparer.OrdinalIgnoreCase);
                    relaxedExcludedMealNames.Add(normalizedSwapCurrentMealName);
                    suggestions = aislePilotService.SuggestMealsFromPantry(
                        request,
                        3,
                        relaxedExcludedMealNames.ToList(),
                        generationNonce);
                }
            }
            else
            {
                suggestions = aislePilotService.SuggestMealsFromPantry(
                    request,
                    3,
                    strictExcludedMealNames.ToList(),
                    generationNonce);
            }

            if (suggestions.Count == 0)
            {
                var noMatchMessage = isSwapRequest
                    ? "Could not find a different meal right now. Try showing 3 more ideas."
                    : request.RequireCorePantryIngredients
                        ? "No meals fit strict core mode. Add more core ingredients or turn strict mode off."
                        : "No close meals found from your current pantry items. Add more ingredients or generate a full weekly plan.";
                ModelState.AddModelError("Request.PantryItems", noMatchMessage);
                return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
            }

            request.PantrySuggestionHistoryState = UpdatePantrySuggestionHistoryState(
                request.PantrySuggestionHistoryState,
                suggestions.Select(suggestion => suggestion.MealName));
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

    private static IReadOnlyList<string> NormalizePantryMealNames(IReadOnlyList<string>? mealNames)
    {
        return (mealNames ?? [])
            .Where(mealName => !string.IsNullOrWhiteSpace(mealName))
            .Select(mealName => mealName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ParsePantrySuggestionHistoryState(string? pantrySuggestionHistoryState)
    {
        if (string.IsNullOrWhiteSpace(pantrySuggestionHistoryState))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(pantrySuggestionHistoryState, SetupStateJsonOptions);
            return NormalizePantryMealNames(parsed);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseSavedEnjoyedMealNamesState(string? savedEnjoyedMealNamesState)
    {
        if (string.IsNullOrWhiteSpace(savedEnjoyedMealNamesState))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(savedEnjoyedMealNamesState, SetupStateJsonOptions);
            return NormalizeSavedEnjoyedMealNames(parsed);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<string> NormalizeSavedEnjoyedMealNames(IReadOnlyList<string>? mealNames)
    {
        return (mealNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => name.Length is > 0 and <= MaxSavedMealNameLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSavedEnjoyedMealNames)
            .ToList();
    }

    private static string SerializeSavedEnjoyedMealNameState(IReadOnlyList<string>? savedMealNames)
    {
        var normalized = NormalizeSavedEnjoyedMealNames(savedMealNames);
        return normalized.Count == 0
            ? string.Empty
            : JsonSerializer.Serialize(normalized, SetupStateJsonOptions);
    }

    private static string ToggleSavedEnjoyedMealNameState(string? currentState, string mealName)
    {
        var normalizedMealName = string.IsNullOrWhiteSpace(mealName) ? string.Empty : mealName.Trim();
        if (normalizedMealName.Length == 0 || normalizedMealName.Length > MaxSavedMealNameLength)
        {
            return SerializeSavedEnjoyedMealNameState(ParseSavedEnjoyedMealNamesState(currentState));
        }

        var savedMealNames = ParseSavedEnjoyedMealNamesState(currentState).ToList();
        var existingIndex = savedMealNames.FindIndex(
            name => name.Equals(normalizedMealName, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            savedMealNames.RemoveAt(existingIndex);
        }
        else
        {
            savedMealNames.Add(normalizedMealName);
        }

        return SerializeSavedEnjoyedMealNameState(savedMealNames);
    }

    private static string RemoveSavedEnjoyedMealNameState(string? currentState, string mealName)
    {
        var normalizedMealName = string.IsNullOrWhiteSpace(mealName) ? string.Empty : mealName.Trim();
        if (normalizedMealName.Length == 0 || normalizedMealName.Length > MaxSavedMealNameLength)
        {
            return SerializeSavedEnjoyedMealNameState(ParseSavedEnjoyedMealNamesState(currentState));
        }

        var savedMealNames = ParseSavedEnjoyedMealNamesState(currentState)
            .Where(name => !name.Equals(normalizedMealName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return SerializeSavedEnjoyedMealNameState(savedMealNames);
    }

    private static string UpdatePantrySuggestionHistoryState(
        string? currentState,
        IEnumerable<string> additionalMealNames)
    {
        const int MaxMealNames = 48;

        var history = ParsePantrySuggestionHistoryState(currentState)
            .ToList();
        foreach (var mealName in additionalMealNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Select(name => name.Trim()))
        {
            if (!history.Contains(mealName, StringComparer.OrdinalIgnoreCase))
            {
                history.Add(mealName);
            }
        }

        if (history.Count > MaxMealNames)
        {
            history = history
                .Skip(history.Count - MaxMealNames)
                .ToList();
        }

        return JsonSerializer.Serialize(history, SetupStateJsonOptions);
    }

    private IReadOnlyList<AislePilotPantrySuggestionViewModel> BuildSingleCardSwapSuggestions(
        AislePilotRequestModel request,
        string currentMealName,
        IReadOnlyList<string>? currentSuggestionMealNames,
        IReadOnlyList<string> historyMealNames,
        string generationNonce)
    {
        var normalizedCurrentSuggestionMealNames = NormalizePantryMealNames(currentSuggestionMealNames);
        if (normalizedCurrentSuggestionMealNames.Count == 0 || string.IsNullOrWhiteSpace(currentMealName))
        {
            return [];
        }

        if (!normalizedCurrentSuggestionMealNames.Contains(currentMealName, StringComparer.OrdinalIgnoreCase))
        {
            normalizedCurrentSuggestionMealNames = normalizedCurrentSuggestionMealNames
                .Append(currentMealName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var unchangedMealNames = normalizedCurrentSuggestionMealNames
            .Where(mealName => !mealName.Equals(currentMealName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        var strictReplacementExclusions = new HashSet<string>(historyMealNames, StringComparer.OrdinalIgnoreCase);
        foreach (var mealName in normalizedCurrentSuggestionMealNames)
        {
            strictReplacementExclusions.Add(mealName);
        }

        var replacement = TrySelectPantrySwapReplacement(
            request,
            strictReplacementExclusions,
            generationNonce);
        if (replacement is null)
        {
            var relaxedReplacementExclusions = new HashSet<string>(normalizedCurrentSuggestionMealNames, StringComparer.OrdinalIgnoreCase);
            replacement = TrySelectPantrySwapReplacement(
                request,
                relaxedReplacementExclusions,
                generationNonce);
        }

        if (replacement is null)
        {
            var minimalReplacementExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                currentMealName
            };
            replacement = TrySelectPantrySwapReplacement(
                request,
                minimalReplacementExclusions,
                generationNonce);
        }

        if (replacement is null)
        {
            return [];
        }

        var targetMealNames = unchangedMealNames
            .Append(replacement.MealName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        if (targetMealNames.Count == 0)
        {
            return [];
        }

        var detailCandidates = aislePilotService.SuggestMealsFromPantry(
            request,
            12,
            excludedMealNames: [],
            generationNonce);
        var detailByMealName = detailCandidates.ToDictionary(
            suggestion => suggestion.MealName,
            suggestion => suggestion,
            StringComparer.OrdinalIgnoreCase);
        detailByMealName[replacement.MealName] = replacement;

        var finalSuggestions = new List<AislePilotPantrySuggestionViewModel>(targetMealNames.Count);
        foreach (var mealName in targetMealNames)
        {
            if (!detailByMealName.TryGetValue(mealName, out var suggestion))
            {
                return [];
            }

            finalSuggestions.Add(suggestion);
        }

        return OrderPantrySuggestionsForDisplay(finalSuggestions);
    }

    private AislePilotPantrySuggestionViewModel? TrySelectPantrySwapReplacement(
        AislePilotRequestModel request,
        IReadOnlyCollection<string> excludedMealNames,
        string generationNonce)
    {
        var replacements = aislePilotService.SuggestMealsFromPantry(
            request,
            12,
            excludedMealNames.ToList(),
            generationNonce);
        return replacements.FirstOrDefault();
    }

    private static IReadOnlyList<AislePilotPantrySuggestionViewModel> OrderPantrySuggestionsForDisplay(
        IReadOnlyList<AislePilotPantrySuggestionViewModel> suggestions)
    {
        if (suggestions.Count <= 1)
        {
            return suggestions;
        }

        return suggestions
            .OrderByDescending(suggestion => suggestion.MatchPercent)
            .ThenBy(suggestion => suggestion.MissingIngredientsEstimatedCost)
            .ThenBy(suggestion => suggestion.MissingCoreIngredientCount)
            .ThenBy(suggestion => suggestion.MealName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private sealed class AislePilotSavedWeekCookieModel
    {
        public string WeekId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public DateTimeOffset SavedAtUtc { get; set; }
        public AislePilotSavedWeekRequestCookieModel RequestSnapshot { get; set; } = new();
        public List<string> CurrentPlanMealNames { get; set; } = [];

        public AislePilotRequestModel ToRequestModel()
        {
            return new AislePilotRequestModel
            {
                Supermarket = RequestSnapshot.Supermarket,
                WeeklyBudget = RequestSnapshot.WeeklyBudget,
                HouseholdSize = RequestSnapshot.HouseholdSize,
                CookDays = RequestSnapshot.CookDays,
                PlanDays = RequestSnapshot.PlanDays,
                MealsPerDay = RequestSnapshot.MealsPerDay,
                SelectedMealTypes = RequestSnapshot.SelectedMealTypes.ToList(),
                PortionSize = RequestSnapshot.PortionSize,
                DietaryModes = RequestSnapshot.DietaryModes.ToList(),
                DislikesOrAllergens = RequestSnapshot.DislikesOrAllergens,
                CustomAisleOrder = RequestSnapshot.CustomAisleOrder,
                LeftoverCookDayIndexesCsv = RequestSnapshot.LeftoverCookDayIndexesCsv,
                IgnoredMealSlotIndexesCsv = RequestSnapshot.IgnoredMealSlotIndexesCsv,
                PreferQuickMeals = RequestSnapshot.PreferQuickMeals,
                IncludeSpecialTreatMeal = RequestSnapshot.IncludeSpecialTreatMeal,
                SelectedSpecialTreatCookDayIndex = RequestSnapshot.SelectedSpecialTreatCookDayIndex,
                IncludeDessertAddOn = RequestSnapshot.IncludeDessertAddOn,
                SelectedDessertAddOnName = RequestSnapshot.SelectedDessertAddOnName
            };
        }
    }

    private sealed class AislePilotSavedWeekRequestCookieModel
    {
        public string Supermarket { get; set; } = "Tesco";
        public decimal WeeklyBudget { get; set; } = 65m;
        public int HouseholdSize { get; set; } = 2;
        public int CookDays { get; set; } = 7;
        public int PlanDays { get; set; } = 7;
        public int MealsPerDay { get; set; } = 3;
        public List<string> SelectedMealTypes { get; set; } = [];
        public string PortionSize { get; set; } = "Medium";
        public List<string> DietaryModes { get; set; } = [];
        public string DislikesOrAllergens { get; set; } = string.Empty;
        public string CustomAisleOrder { get; set; } = string.Empty;
        public string LeftoverCookDayIndexesCsv { get; set; } = string.Empty;
        public string IgnoredMealSlotIndexesCsv { get; set; } = string.Empty;
        public bool PreferQuickMeals { get; set; } = true;
        public bool IncludeSpecialTreatMeal { get; set; }
        public int? SelectedSpecialTreatCookDayIndex { get; set; }
        public bool IncludeDessertAddOn { get; set; }
        public string SelectedDessertAddOnName { get; set; } = string.Empty;
    }

    private sealed class AislePilotSetupStateCookieModel
    {
        public string? Supermarket { get; set; }
        public decimal WeeklyBudget { get; set; } = 65m;
        public int HouseholdSize { get; set; } = 2;
        public int CookDays { get; set; } = 7;
        public int PlanDays { get; set; } = 7;
        public int MealsPerDay { get; set; } = 3;
        public List<string> SelectedMealTypes { get; set; } = [];
        public string? PortionSize { get; set; }
        public List<string> DietaryModes { get; set; } = [];
        public string? DislikesOrAllergens { get; set; }
        public string? CustomAisleOrder { get; set; }
        public string? PantryItems { get; set; }
        public bool PreferQuickMeals { get; set; } = true;
        public bool EnableSavedMealRepeats { get; set; } = true;
        public int SavedMealRepeatRatePercent { get; set; } = 35;
        public string? SavedEnjoyedMealNamesState { get; set; }
        public bool RequireCorePantryIngredients { get; set; }
        public bool IncludeSpecialTreatMeal { get; set; }
        public int? SelectedSpecialTreatCookDayIndex { get; set; }
        public bool IncludeDessertAddOn { get; set; }
        public string? SelectedDessertAddOnName { get; set; }
    }
}
