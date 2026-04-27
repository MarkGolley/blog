using MyBlog.Models;
using Microsoft.Extensions.Logging;

namespace MyBlog.Services;

public sealed class AislePilotMealSwapPipeline : IAislePilotMealSwapPipeline
{
    private const int FreshAiGenerationSwapAttemptThreshold = 3;

    public async Task<AislePilotPlanResultViewModel> SwapMealForDayAsync(
        AislePilotService service,
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames,
        CancellationToken cancellationToken = default)
    {
        var planDays = AislePilotService.NormalizePlanDays(request.PlanDays);
        var cookDays = AislePilotService.NormalizeCookDays(request.CookDays, planDays);
        var mealTypeSlots = AislePilotService.BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = AislePilotService.NormalizeRequestedMealCount(cookDays * mealsPerDay);
        if (dayIndex < 0 || dayIndex >= totalMealCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayIndex),
                $"Day index must be between 0 and {totalMealCount - 1}.");
        }

        var context = await service.BuildPlanContextAsync(request, cancellationToken);
        var selectedMeals = AislePilotService.BuildSelectedMealsFromCurrentPlanNames(currentPlanMealNames, totalMealCount);
        if (selectedMeals is null && service.AllowTemplateFallback)
        {
            var fallbackPlan = await service.BuildPlanFromTemplateCatalogAsync(
                request,
                context,
                cookDays,
                totalMealCount,
                cancellationToken: cancellationToken);
            selectedMeals = AislePilotService.BuildSelectedMealsFromCurrentPlanNames(
                fallbackPlan.MealPlan.Select(meal => meal.MealName).ToList(),
                totalMealCount);
        }

        if (selectedMeals is null)
        {
            throw new InvalidOperationException("Could not resolve the current plan for swapping. Generate a fresh AI plan and try again.");
        }

        var currentName = string.IsNullOrWhiteSpace(currentMealName)
            ? selectedMeals[dayIndex].Name
            : currentMealName.Trim();
        var leftoverDays = Math.Max(0, planDays - cookDays);
        var requestedLeftoverSourceDays = AislePilotService.ParseRequestedLeftoverSourceDays(
            request.LeftoverCookDayIndexesCsv,
            cookDays,
            leftoverDays,
            planDays);
        var mealPortionMultipliers = AislePilotService.BuildMealPortionMultipliers(
            cookDays,
            leftoverDays,
            requestedLeftoverSourceDays,
            planDays);
        var mealDayIndex = dayIndex / mealsPerDay;
        var dayMultiplier = mealPortionMultipliers[Math.Clamp(mealDayIndex, 0, mealPortionMultipliers.Count - 1)];
        var mealType = mealTypeSlots[dayIndex % mealTypeSlots.Count];
        var normalizedSeenMealNames = (seenMealNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currentMeal = selectedMeals[dayIndex];
        var priorSuccessfulSwapsForSlot = CountPriorSuccessfulSwapsForSlot(request.SwapHistoryState, dayIndex);
        var currentSwapAttempt = priorSuccessfulSwapsForSlot + 1;

        AislePilotService.MealTemplate? replacement = null;
        var planSourceLabel = "AI meal pool";
        replacement = TrySelectPoolReplacement(
            context,
            selectedMeals,
            dayIndex,
            currentMeal,
            currentName,
            request,
            mealType,
            dayMultiplier,
            mealsPerDay,
            normalizedSeenMealNames);

        if (replacement is null)
        {
            await service.EnsureAiMealPoolHydratedAsync(cancellationToken);
            replacement = TrySelectPoolReplacement(
                context,
                selectedMeals,
                dayIndex,
                currentMeal,
                currentName,
                request,
                mealType,
                dayMultiplier,
                mealsPerDay,
                normalizedSeenMealNames);
        }

        if (replacement is null)
        {
            replacement = AislePilotService.TrySelectTemplateSwapCandidate(
                context,
                selectedMeals,
                dayIndex,
                currentMeal,
                currentName,
                request.WeeklyBudget,
                request.PreferQuickMeals,
                mealType,
                dayMultiplier,
                mealsPerDay,
                normalizedSeenMealNames);
            if (replacement is not null)
            {
                planSourceLabel = "Template swap";
            }
        }

        var hasTemplateReplacement =
            replacement is not null &&
            planSourceLabel.Equals("Template swap", StringComparison.OrdinalIgnoreCase);
        var shouldTryFreshAiGeneration =
            replacement is null ||
            (hasTemplateReplacement && currentSwapAttempt >= FreshAiGenerationSwapAttemptThreshold);

        if (shouldTryFreshAiGeneration)
        {
            replacement = await service.TryBuildReplacementMealWithAiAsync(
                request,
                context,
                selectedMeals,
                dayIndex,
                currentName,
                dayMultiplier,
                mealType,
                normalizedSeenMealNames,
                cancellationToken);
            if (replacement is not null)
            {
                planSourceLabel = "OpenAI swap";
            }
        }
        else if (hasTemplateReplacement)
        {
            service.Logger?.LogInformation(
                "AislePilot deferring fresh AI swap generation until swap attempt {RequiredAttempt}. DayIndex={DayIndex}, CurrentAttempt={CurrentAttempt}",
                FreshAiGenerationSwapAttemptThreshold,
                dayIndex,
                currentSwapAttempt);
        }

        if (replacement is null)
        {
            throw new InvalidOperationException(
                "No unique compatible replacement meal is available right now. Loosen one dietary filter or regenerate your full plan.");
        }

        AislePilotService.AddMealsToAiPool([replacement]);
        selectedMeals[dayIndex] = replacement;
        return await service.BuildPlanFromMealsAsync(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: !planSourceLabel.Equals("Template swap", StringComparison.OrdinalIgnoreCase),
            planSourceLabel: planSourceLabel,
            cancellationToken: cancellationToken);
    }

    private static AislePilotService.MealTemplate? TrySelectPoolReplacement(
        AislePilotService.PlanContext context,
        IReadOnlyList<AislePilotService.MealTemplate> selectedMeals,
        int dayIndex,
        AislePilotService.MealTemplate currentMeal,
        string currentMealName,
        AislePilotRequestModel request,
        string mealType,
        int dayMultiplier,
        int mealsPerDay,
        IReadOnlyList<string> normalizedSeenMealNames)
    {
        var availableAiMeals = AislePilotService.GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        if (availableAiMeals.Count == 0)
        {
            return null;
        }

        var unseenPoolMeals = availableAiMeals
            .Where(meal => !normalizedSeenMealNames.Contains(meal.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var compatibleUnseenPoolMeals = unseenPoolMeals
            .Where(meal => AislePilotService.SupportsMealType(meal, mealType))
            .ToList();
        if (compatibleUnseenPoolMeals.Count == 0)
        {
            return null;
        }

        return AislePilotService.SelectSwapCandidate(
            compatibleUnseenPoolMeals,
            selectedMeals,
            dayIndex,
            currentMeal,
            currentMealName,
            request.WeeklyBudget,
            context.HouseholdFactor,
            context.PriceProfile.RelativeCostFactor,
            request.PreferQuickMeals,
            AislePilotService.IsHighProteinPreferred(context.DietaryModes),
            mealType,
            dayMultiplier,
            mealsPerDay);
    }

    private static int CountPriorSuccessfulSwapsForSlot(string? swapHistoryState, int dayIndex)
    {
        if (string.IsNullOrWhiteSpace(swapHistoryState))
        {
            return 0;
        }

        var dayEntries = swapHistoryState.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in dayEntries)
        {
            var segments = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (segments.Length != 2 ||
                !int.TryParse(segments[0], out var parsedDayIndex) ||
                parsedDayIndex != dayIndex)
            {
                continue;
            }

            var seenMealsForSlot = segments[1]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(meal => !string.IsNullOrWhiteSpace(meal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return Math.Max(0, seenMealsForSlot - 1);
        }

        return 0;
    }
}
