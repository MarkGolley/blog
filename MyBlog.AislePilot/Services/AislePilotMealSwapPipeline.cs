using MyBlog.Models;

namespace MyBlog.Services;

public sealed class AislePilotMealSwapPipeline : IAislePilotMealSwapPipeline
{
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
        await service.EnsureAiMealPoolHydratedAsync(cancellationToken);
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

        var availableAiMeals = AislePilotService.GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
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

        AislePilotService.MealTemplate? replacement = null;
        var planSourceLabel = "AI meal pool";
        if (availableAiMeals.Count > 0)
        {
            var unseenPoolMeals = availableAiMeals
                .Where(meal => !normalizedSeenMealNames.Contains(meal.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var compatibleUnseenPoolMeals = unseenPoolMeals
                .Where(meal => AislePilotService.SupportsMealType(meal, mealType))
                .ToList();
            if (compatibleUnseenPoolMeals.Count > 0)
            {
                replacement = AislePilotService.SelectSwapCandidate(
                    compatibleUnseenPoolMeals,
                    selectedMeals,
                    dayIndex,
                    currentName,
                    request.WeeklyBudget,
                    context.HouseholdFactor,
                    request.PreferQuickMeals,
                    AislePilotService.IsHighProteinPreferred(context.DietaryModes),
                    mealType,
                    dayMultiplier,
                    mealsPerDay);
            }
        }

        if (replacement is null)
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

        if (replacement is null)
        {
            replacement = AislePilotService.TrySelectTemplateSwapCandidate(
                context,
                selectedMeals,
                dayIndex,
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
}
