using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBlog.Models;
using MyBlog.Utilities;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    public string ResolveNextDessertAddOnName(string? currentDessertAddOnName)
    {
        EnsureDessertAddOnPoolHydrated();
        var availableDessertTemplates = GetAvailableDessertAddOnTemplatesSnapshot();
        if (availableDessertTemplates.Count == 0)
        {
            return string.Empty;
        }

        var normalizedCurrentDessertName = currentDessertAddOnName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCurrentDessertName))
        {
            return availableDessertTemplates[0].Name;
        }

        var currentIndex = availableDessertTemplates
            .Select((template, index) => new { template, index })
            .Where(entry =>
                entry.template.Name.Equals(normalizedCurrentDessertName, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.index)
            .DefaultIfEmpty(-1)
            .First();
        if (currentIndex < 0)
        {
            return availableDessertTemplates[0].Name;
        }

        var nextIndex = (currentIndex + 1) % availableDessertTemplates.Count;
        return availableDessertTemplates[nextIndex].Name;
    }

    private async Task<DessertAddOnTemplate> ResolveDessertAddOnTemplateAsync(
        string? selectedDessertAddOnName,
        CancellationToken cancellationToken = default)
    {
        await EnsureDessertAddOnPoolHydratedAsync(cancellationToken);
        var availableDessertTemplates = GetAvailableDessertAddOnTemplatesSnapshot();
        if (availableDessertTemplates.Count == 0)
        {
            throw new InvalidOperationException("No dessert add-on templates are configured.");
        }

        if (!string.IsNullOrWhiteSpace(selectedDessertAddOnName))
        {
            var normalizedSelectedDessertName = selectedDessertAddOnName.Trim();
            var selectedTemplate = availableDessertTemplates.FirstOrDefault(template =>
                template.Name.Equals(normalizedSelectedDessertName, StringComparison.OrdinalIgnoreCase));
            if (selectedTemplate is not null)
            {
                return selectedTemplate;
            }
        }

        return availableDessertTemplates[0];
    }

    private async Task<DessertAddOnTemplate?> TryResolveDessertAddOnTemplateForPlanAsync(
        string? selectedDessertAddOnName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolved = await ResolveDessertAddOnTemplateAsync(selectedDessertAddOnName, cancellationToken);
            await PersistDessertAddOnTemplateAsync(resolved, cancellationToken);
            return resolved;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "AislePilot dessert add-on resolution failed; continuing without blocking main meal plan.");
            QueueDessertAddOnRecovery(selectedDessertAddOnName);
        }

        try
        {
            var fallbackTemplate = ResolveDessertAddOnTemplate(selectedDessertAddOnName);
            await PersistDessertAddOnTemplateAsync(fallbackTemplate, cancellationToken);
            return fallbackTemplate;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "AislePilot dessert add-on fallback selection failed; main meal plan will continue without dessert.");
            return null;
        }
    }

    internal static List<MealTemplate>? BuildSelectedMealsFromCurrentPlanNames(
        IReadOnlyList<string>? currentPlanMealNames,
        int expectedMealCount)
    {
        if (currentPlanMealNames is null || currentPlanMealNames.Count < expectedMealCount)
        {
            return null;
        }

        var selectedMeals = new List<MealTemplate>(expectedMealCount);
        for (var mealIndex = 0; mealIndex < expectedMealCount; mealIndex++)
        {
            var mealName = currentPlanMealNames[mealIndex];
            if (string.IsNullOrWhiteSpace(mealName))
            {
                return null;
            }

            var normalizedName = mealName.Trim();
            if (!AiMealPool.TryGetValue(normalizedName, out var meal))
            {
                meal = MealTemplates.FirstOrDefault(template =>
                    template.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            }

            if (meal is null)
            {
                return null;
            }

            selectedMeals.Add(meal);
        }

        return selectedMeals;
    }

    private PlanContext BuildPlanContext(AislePilotRequestModel request)
    {
        var supermarket = NormalizeSupermarket(request.Supermarket);
        var dietaryModes = NormalizeDietaryModes(request.DietaryModes);
        if (!TryValidateNormalizedDietaryModes(dietaryModes, out var dietaryValidationMessage))
        {
            throw new InvalidOperationException(dietaryValidationMessage);
        }

        var customAisleOrder = request.CustomAisleOrder ?? string.Empty;
        var dislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty;
        var portionSize = NormalizePortionSize(request.PortionSize);
        var portionSizeFactor = ResolvePortionSizeFactor(portionSize);
        var aisleOrder = ResolveAisleOrder(supermarket, customAisleOrder);
        var householdFactor = Math.Max(0.5m, request.HouseholdSize / 2m) * portionSizeFactor;

        return new PlanContext(
            supermarket,
            dietaryModes,
            aisleOrder,
            householdFactor,
            dislikesOrAllergens,
            portionSize);
    }

    internal async Task<PlanContext> BuildPlanContextAsync(
        AislePilotRequestModel request,
        CancellationToken cancellationToken = default)
    {
        var supermarket = NormalizeSupermarket(request.Supermarket);
        var dietaryModes = NormalizeDietaryModes(request.DietaryModes);
        if (!TryValidateNormalizedDietaryModes(dietaryModes, out var dietaryValidationMessage))
        {
            throw new InvalidOperationException(dietaryValidationMessage);
        }

        var customAisleOrder = request.CustomAisleOrder ?? string.Empty;
        var dislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty;
        var portionSize = NormalizePortionSize(request.PortionSize);
        var portionSizeFactor = ResolvePortionSizeFactor(portionSize);
        var aisleOrder = await ResolveAisleOrderAsync(supermarket, customAisleOrder, cancellationToken);
        var householdFactor = Math.Max(0.5m, request.HouseholdSize / 2m) * portionSizeFactor;

        return new PlanContext(
            supermarket,
            dietaryModes,
            aisleOrder,
            householdFactor,
            dislikesOrAllergens,
            portionSize);
    }

    internal static AislePilotRequestModel CloneRequest(AislePilotRequestModel request)
    {
        return new AislePilotRequestModel
        {
            Supermarket = request.Supermarket,
            WeeklyBudget = request.WeeklyBudget,
            HouseholdSize = request.HouseholdSize,
            CookDays = request.CookDays,
            PlanDays = request.PlanDays,
            MealsPerDay = request.MealsPerDay,
            SelectedMealTypes = [.. request.SelectedMealTypes],
            PortionSize = request.PortionSize,
            DietaryModes = [.. request.DietaryModes],
            DislikesOrAllergens = request.DislikesOrAllergens,
            CustomAisleOrder = request.CustomAisleOrder,
            PantryItems = request.PantryItems,
            LeftoverCookDayIndexesCsv = request.LeftoverCookDayIndexesCsv,
            SwapHistoryState = request.SwapHistoryState,
            IgnoredMealSlotIndexesCsv = request.IgnoredMealSlotIndexesCsv,
            PreferQuickMeals = request.PreferQuickMeals,
            EnableSavedMealRepeats = request.EnableSavedMealRepeats,
            SavedMealRepeatRatePercent = request.SavedMealRepeatRatePercent,
            SavedEnjoyedMealNamesState = request.SavedEnjoyedMealNamesState,
            RequireCorePantryIngredients = request.RequireCorePantryIngredients,
            IncludeSpecialTreatMeal = request.IncludeSpecialTreatMeal,
            SelectedSpecialTreatCookDayIndex = request.SelectedSpecialTreatCookDayIndex,
            IncludeDessertAddOn = request.IncludeDessertAddOn,
            SelectedDessertAddOnName = request.SelectedDessertAddOnName
        };
    }

    private AislePilotPlanResultViewModel BuildPlanFromMeals(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> selectedMeals,
        int cookDays,
        bool usedAiGeneratedMeals = false,
        string? planSourceLabel = null)
    {
        return BuildPlanFromMealsAsync(
                request,
                context,
                selectedMeals,
                cookDays,
                usedAiGeneratedMeals,
                planSourceLabel)
            .GetAwaiter()
            .GetResult();
    }

    internal async Task<AislePilotPlanResultViewModel> BuildPlanFromMealsAsync(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> selectedMeals,
        int cookDays,
        bool usedAiGeneratedMeals = false,
        string? planSourceLabel = null,
        CancellationToken cancellationToken = default)
    {
        var planDays = NormalizePlanDays(request.PlanDays);
        var normalizedCookDays = NormalizeCookDays(cookDays, planDays);
        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var expectedMealCount = NormalizeRequestedMealCount(normalizedCookDays * mealsPerDay);
        var leftoverDays = Math.Max(0, planDays - normalizedCookDays);
        var requestedLeftoverSourceDays = ParseRequestedLeftoverSourceDays(
            request.LeftoverCookDayIndexesCsv,
            normalizedCookDays,
            leftoverDays,
            planDays);
        var dayMultipliers = BuildMealPortionMultipliers(
            normalizedCookDays,
            leftoverDays,
            requestedLeftoverSourceDays,
            planDays);
        var normalizedSelectedMeals = NormalizeSelectedMealsForCount(selectedMeals, expectedMealCount);
        var ignoredMealSlotIndexes = ParseIgnoredMealSlotIndexes(
            request.IgnoredMealSlotIndexesCsv,
            expectedMealCount);
        var mealPortionMultipliers = BuildPerMealPortionMultipliers(dayMultipliers, mealsPerDay);
        var mealImageUrls = await ResolveMealImageUrlsAsync(normalizedSelectedMeals, cancellationToken);
        var portionSizeFactor = ResolvePortionSizeFactor(context.PortionSize);
        var specialTreatMealSlotIndex = request.IncludeSpecialTreatMeal
            ? ResolveSpecialTreatDisplayMealIndex(
                normalizedSelectedMeals,
                mealTypeSlots,
                request.SelectedSpecialTreatCookDayIndex)
            : null;
        var dailyPlans = BuildDailyPlans(
            normalizedSelectedMeals,
            mealPortionMultipliers,
            dayMultipliers,
            mealTypeSlots,
            ignoredMealSlotIndexes,
            mealImageUrls,
            context.HouseholdFactor,
            portionSizeFactor,
            context.DietaryModes,
            context.DislikesOrAllergens,
            specialTreatMealSlotIndex);
        var dessertAddOnTemplate = request.IncludeDessertAddOn
            ? await TryResolveDessertAddOnTemplateForPlanAsync(request.SelectedDessertAddOnName, cancellationToken)
            : null;
        var hasSpecialTreatMealInPlan = request.IncludeSpecialTreatMeal &&
                                        dailyPlans.Any(meal => meal.IsSpecialTreat);
        var hasDessertAddOnInPlan = dessertAddOnTemplate is not null;
        var shoppingItems = BuildShoppingList(
            normalizedSelectedMeals,
            mealPortionMultipliers,
            ignoredMealSlotIndexes,
            context.HouseholdFactor,
            context.AisleOrder,
            dessertAddOnTemplate);
        var dessertAddOnCost = dessertAddOnTemplate is not null
            ? CalculateDessertAddOnEstimatedCost(context.HouseholdFactor, dessertAddOnTemplate)
            : 0m;
        var dessertAddOnName = dessertAddOnTemplate?.Name ?? string.Empty;
        var dessertAddOnIngredientLines = dessertAddOnTemplate is not null
            ? BuildDessertAddOnIngredientLines(context.HouseholdFactor, dessertAddOnTemplate)
            : Array.Empty<string>();
        var estimatedTotalCost = decimal.Round(
            dailyPlans.Sum(x => x.EstimatedCost) + dessertAddOnCost,
            2,
            MidpointRounding.AwayFromZero);
        var budgetDelta = decimal.Round(request.WeeklyBudget - estimatedTotalCost, 2, MidpointRounding.AwayFromZero);
        var isOverBudget = budgetDelta < 0;
        var budgetTips = BuildBudgetTips(isOverBudget, budgetDelta, leftoverDays).ToList();
        if (request.IncludeSpecialTreatMeal)
        {
            budgetTips.Add(hasSpecialTreatMealInPlan
                ? "Includes one special treat meal in your week."
                : "Special treat dinner is still generating in the background.");
        }

        if (request.IncludeDessertAddOn)
        {
            budgetTips.Add(hasDessertAddOnInPlan
                ? "Includes a cake/dessert add-on in your shopping list."
                : "Dessert add-on is still being prepared in the background.");
        }

        return new AislePilotPlanResultViewModel
        {
            Supermarket = context.Supermarket,
            PortionSize = context.PortionSize,
            AppliedDietaryModes = context.DietaryModes,
            UsedAiGeneratedMeals = usedAiGeneratedMeals,
            PlanSourceLabel = string.IsNullOrWhiteSpace(planSourceLabel)
                ? usedAiGeneratedMeals ? "OpenAI generated" : string.Empty
                : planSourceLabel,
            PlanDays = planDays,
            CookDays = normalizedCookDays,
            MealsPerDay = mealsPerDay,
            LeftoverDays = leftoverDays,
            WeeklyBudget = request.WeeklyBudget,
            EstimatedTotalCost = estimatedTotalCost,
            BudgetDelta = budgetDelta,
            IsOverBudget = isOverBudget,
            IncludeSpecialTreatMeal = hasSpecialTreatMealInPlan,
            IncludeDessertAddOn = hasDessertAddOnInPlan,
            DessertAddOnEstimatedCost = dessertAddOnCost,
            DessertAddOnName = dessertAddOnName,
            DessertAddOnIngredientLines = dessertAddOnIngredientLines,
            AisleOrderUsed = context.AisleOrder,
            BudgetTips = budgetTips,
            MealPlan = dailyPlans,
            ShoppingItems = shoppingItems
        };
    }

}
