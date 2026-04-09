using MyBlog.Models;
using Microsoft.Extensions.Logging;

namespace MyBlog.Services;

public sealed class AislePilotPlanGenerationOrchestrator : IAislePilotPlanGenerationOrchestrator
{
    public async Task<AislePilotPlanResultViewModel> BuildPlanAsync(
        AislePilotService service,
        AislePilotRequestModel request,
        CancellationToken cancellationToken = default)
    {
        var context = await service.BuildPlanContextAsync(request, cancellationToken);
        var planDays = AislePilotService.NormalizePlanDays(request.PlanDays);
        var cookDays = AislePilotService.NormalizeCookDays(request.CookDays, planDays);
        var mealTypeSlots = AislePilotService.BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = AislePilotService.NormalizeRequestedMealCount(cookDays * mealsPerDay);
        if (service.ShouldUseTemplateFallback() && !request.IncludeSpecialTreatMeal)
        {
            service.Logger?.LogWarning("AislePilot is using local meal templates because AI generation is unavailable in this runtime.");
            return await service.BuildPlanFromTemplateCatalogAsync(request, context, cookDays, totalMealCount, cancellationToken);
        }

        var pooledAiPlan = await service.TryBuildPlanFromAiPoolAsync(
            request,
            context,
            cookDays,
            totalMealCount,
            cancellationToken);
        if (pooledAiPlan is not null)
        {
            return pooledAiPlan;
        }

        var aiPlan = await service.TryBuildPlanWithAiAsync(
            request,
            context,
            cookDays,
            totalMealCount,
            cancellationToken);
        if (aiPlan is not null)
        {
            return aiPlan;
        }

        if (request.IncludeSpecialTreatMeal)
        {
            service.Logger?.LogWarning(
                "AislePilot could not produce a special treat dinner right now; serving core meals and continuing special treat generation in the background.");
            try
            {
                // Try to satisfy special treat immediately from local/template candidates first.
                return await service.BuildPlanFromTemplateCatalogAsync(request, context, cookDays, totalMealCount, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                service.QueueSpecialTreatGeneration(request, context);

                var requestWithoutSpecialTreat = AislePilotService.CloneRequest(request);
                requestWithoutSpecialTreat.IncludeSpecialTreatMeal = false;
                requestWithoutSpecialTreat.SelectedSpecialTreatCookDayIndex = null;

                var nonBlockingPlan = await service.TryBuildPlanFromAiPoolAsync(
                    requestWithoutSpecialTreat,
                    context,
                    cookDays,
                    totalMealCount,
                    cancellationToken);
                nonBlockingPlan ??= await service.BuildPlanFromTemplateCatalogAsync(
                    requestWithoutSpecialTreat,
                    context,
                    cookDays,
                    totalMealCount,
                    cancellationToken);
                nonBlockingPlan.BudgetTips = nonBlockingPlan.BudgetTips
                    .Concat(["Special treat dinner is still generating in the background."])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return nonBlockingPlan;
            }
        }

        service.Logger?.LogWarning(
            "AislePilot AI generation was unavailable for this request. Serving template fallback instead.");
        return await service.BuildPlanFromTemplateCatalogAsync(request, context, cookDays, totalMealCount, cancellationToken);
    }
}
