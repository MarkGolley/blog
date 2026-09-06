using MyBlog.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MyBlog.Services;

public sealed class AislePilotPlanGenerationOrchestrator : IAislePilotPlanGenerationOrchestrator
{
    public async Task<AislePilotPlanResultViewModel> BuildPlanAsync(
        AislePilotService service,
        AislePilotRequestModel request,
        IReadOnlyList<string>? excludedMealNames = null,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var contextStopwatch = Stopwatch.StartNew();
        var context = service.BuildPlanContextForInteractiveRequest(request);
        var contextElapsedMs = contextStopwatch.ElapsedMilliseconds;
        AislePilotTelemetry.RecordPlanStage("context", contextStopwatch.Elapsed);
        var planDays = AislePilotService.NormalizePlanDays(request.PlanDays);
        var cookDays = AislePilotService.NormalizeCookDays(request.CookDays, planDays);
        var mealTypeSlots = AislePilotService.BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = AislePilotService.NormalizeRequestedMealCount(cookDays * mealsPerDay);
        if (service.ShouldUseTemplateFallback() && !request.IncludeSpecialTreatMeal)
        {
            service.Logger?.LogWarning("AislePilot is using local meal templates because AI generation is unavailable in this runtime.");
            return await service.BuildPlanFromTemplateCatalogAsync(
                request,
                context,
                cookDays,
                totalMealCount,
                excludedMealNames,
                cancellationToken);
        }

        var poolStopwatch = Stopwatch.StartNew();
        var pooledAiPlan = await service.TryBuildPlanFromAiPoolAsync(
            request,
            context,
            cookDays,
            totalMealCount,
            excludedMealNames,
            cancellationToken,
            hydrateOnMiss: service.EnableInteractiveAiGeneration);
        AislePilotTelemetry.RecordPlanStage("pool_lookup", poolStopwatch.Elapsed);
        if (pooledAiPlan is not null)
        {
            LogInteractiveTiming(service, totalStopwatch, contextElapsedMs, "pool");
            return pooledAiPlan;
        }

        if (!service.EnableInteractiveAiGeneration)
        {
            service.QueuePlanPoolReplenishment(request, excludedMealNames);
            var fallbackStopwatch = Stopwatch.StartNew();
            var fallbackPlan = await service.BuildPlanFromTemplateCatalogAsync(
                request,
                context,
                cookDays,
                totalMealCount,
                excludedMealNames,
                cancellationToken);
            AislePilotTelemetry.RecordPlanStage("template_selection_and_assembly", fallbackStopwatch.Elapsed, "template");
            LogInteractiveTiming(service, totalStopwatch, contextElapsedMs, "template_fast_fallback");
            return fallbackPlan;
        }

        var aiStopwatch = Stopwatch.StartNew();
        var aiPlan = await service.TryBuildPlanWithAiAsync(
            request,
            context,
            cookDays,
            totalMealCount,
            excludedMealNames,
            cancellationToken);
        AislePilotTelemetry.RecordPlanStage("ai_generation_and_validation", aiStopwatch.Elapsed, "ai");
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
                return await service.BuildPlanFromTemplateCatalogAsync(
                    request,
                    context,
                    cookDays,
                    totalMealCount,
                    excludedMealNames,
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                service.QueueSpecialTreatGeneration(request, context, excludedMealNames);

                var requestWithoutSpecialTreat = AislePilotService.CloneRequest(request);
                requestWithoutSpecialTreat.IncludeSpecialTreatMeal = false;
                requestWithoutSpecialTreat.SelectedSpecialTreatCookDayIndex = null;

                var nonBlockingPlan = await service.TryBuildPlanFromAiPoolAsync(
                    requestWithoutSpecialTreat,
                    context,
                    cookDays,
                    totalMealCount,
                    excludedMealNames,
                    cancellationToken);
                nonBlockingPlan ??= await service.BuildPlanFromTemplateCatalogAsync(
                    requestWithoutSpecialTreat,
                    context,
                    cookDays,
                    totalMealCount,
                    excludedMealNames,
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
        return await service.BuildPlanFromTemplateCatalogAsync(
            request,
            context,
            cookDays,
            totalMealCount,
            excludedMealNames,
            cancellationToken);
    }

    private static void LogInteractiveTiming(
        AislePilotService service,
        Stopwatch totalStopwatch,
        long contextElapsedMs,
        string source)
    {
        service.Logger?.LogInformation(
            "AislePilot interactive plan completed in {ElapsedMs}ms. ContextMs={ContextMs}, Source={Source}",
            totalStopwatch.ElapsedMilliseconds,
            contextElapsedMs,
            source);
    }
}
