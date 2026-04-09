using MyBlog.Models;

namespace MyBlog.Services;

public sealed class AislePilotBudgetRebalancePipeline : IAislePilotBudgetRebalancePipeline
{
    public async Task<AislePilotPlanResultViewModel> BuildPlanWithBudgetRebalanceAsync(
        AislePilotService service,
        AislePilotRequestModel request,
        int maxAttempts = 4,
        IReadOnlyList<string>? currentPlanMealNames = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedMaxAttempts = Math.Clamp(maxAttempts, 1, 8);
        var context = await service.BuildPlanContextAsync(request, cancellationToken);
        var planDays = AislePilotService.NormalizePlanDays(request.PlanDays);
        var cookDays = AislePilotService.NormalizeCookDays(request.CookDays, planDays);
        var mealTypeSlots = AislePilotService.BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = AislePilotService.NormalizeRequestedMealCount(cookDays * mealsPerDay);

        var selectedMealsFromCurrentPlan = AislePilotService.BuildSelectedMealsFromCurrentPlanNames(currentPlanMealNames, totalMealCount);
        var baselinePlan = selectedMealsFromCurrentPlan is not null
            ? await service.BuildPlanFromMealsAsync(
                request,
                context,
                selectedMealsFromCurrentPlan,
                cookDays,
                usedAiGeneratedMeals: true,
                planSourceLabel: "Current plan",
                cancellationToken: cancellationToken)
            : await service.BuildPlanAsync(request, cancellationToken);
        if (!baselinePlan.IsOverBudget)
        {
            return baselinePlan;
        }

        var baselineMealNames = (currentPlanMealNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        if (baselineMealNames.Count != baselinePlan.MealPlan.Count)
        {
            baselineMealNames = baselinePlan.MealPlan
                .Select(meal => meal.MealName)
                .ToList();
        }

        selectedMealsFromCurrentPlan ??= AislePilotService.BuildSelectedMealsFromCurrentPlanNames(baselineMealNames, totalMealCount);
        var cheapestPlan = baselinePlan;
        AislePilotPlanResultViewModel? cheapestChangedPlan = null;

        void ConsiderCandidate(AislePilotPlanResultViewModel candidatePlan)
        {
            if (candidatePlan.EstimatedTotalCost < cheapestPlan.EstimatedTotalCost)
            {
                cheapestPlan = candidatePlan;
            }

            if (!service.PlanComparisonService.HasSameMealSequence(candidatePlan, baselineMealNames) &&
                (cheapestChangedPlan is null ||
                 candidatePlan.EstimatedTotalCost < cheapestChangedPlan.EstimatedTotalCost))
            {
                cheapestChangedPlan = candidatePlan;
            }
        }

        if (selectedMealsFromCurrentPlan is not null)
        {
            var targetedSwapPlan = await service.TryBuildTargetedLowerCostPlanAsync(
                request,
                context,
                selectedMealsFromCurrentPlan,
                cookDays,
                cancellationToken);
            if (targetedSwapPlan is not null)
            {
                ConsiderCandidate(targetedSwapPlan);
                if (!targetedSwapPlan.IsOverBudget)
                {
                    return service.ApplyBudgetRebalanceStatus(targetedSwapPlan, baselinePlan, baselineMealNames);
                }
            }
        }

        try
        {
            var lowestCostPlan = await service.BuildLowestCostRebalancePlanAsync(request, context, cookDays, cancellationToken);
            lowestCostPlan = AislePilotService.RebasePlanToOriginalBudget(lowestCostPlan, request.WeeklyBudget);
            ConsiderCandidate(lowestCostPlan);
            if (!lowestCostPlan.IsOverBudget)
            {
                return service.ApplyBudgetRebalanceStatus(lowestCostPlan, baselinePlan, baselineMealNames);
            }
        }
        catch (InvalidOperationException)
        {
            // Ignore; fallback target-based passes below will handle this.
        }

        var rebalanceTargets = AislePilotService.BuildBudgetRebalanceTargets(
            request.WeeklyBudget,
            baselinePlan.EstimatedTotalCost,
            normalizedMaxAttempts - 1);
        foreach (var targetBudget in rebalanceTargets)
        {
            var candidateRequest = AislePilotService.CloneRequest(request);
            candidateRequest.WeeklyBudget = targetBudget;

            AislePilotPlanResultViewModel candidatePlan;
            try
            {
                candidatePlan = await service.BuildPlanAsync(candidateRequest, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            candidatePlan = AislePilotService.RebasePlanToOriginalBudget(candidatePlan, request.WeeklyBudget);
            ConsiderCandidate(candidatePlan);

            if (!candidatePlan.IsOverBudget)
            {
                return service.ApplyBudgetRebalanceStatus(candidatePlan, baselinePlan, baselineMealNames);
            }
        }

        var finalPlan = cheapestPlan;
        if (cheapestChangedPlan is not null &&
            cheapestChangedPlan.EstimatedTotalCost < baselinePlan.EstimatedTotalCost)
        {
            finalPlan = cheapestChangedPlan;
        }

        return service.ApplyBudgetRebalanceStatus(finalPlan, baselinePlan, baselineMealNames);
    }
}
