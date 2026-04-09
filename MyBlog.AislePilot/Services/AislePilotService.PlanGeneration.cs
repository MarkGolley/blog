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
    public AislePilotPlanResultViewModel BuildPlan(AislePilotRequestModel request)
    {
        return _planGenerationOrchestrator.BuildPlanAsync(this, request).GetAwaiter().GetResult();
    }

    public Task<AislePilotPlanResultViewModel> BuildPlanAsync(
        AislePilotRequestModel request,
        CancellationToken cancellationToken = default)
    {
        return _planGenerationOrchestrator.BuildPlanAsync(this, request, cancellationToken);
    }

    public async Task<AislePilotPlanResultViewModel> BuildPlanFromCurrentMealsAsync(
        AislePilotRequestModel request,
        IReadOnlyList<string> currentPlanMealNames,
        CancellationToken cancellationToken = default)
    {
        var planDays = NormalizePlanDays(request.PlanDays);
        var cookDays = NormalizeCookDays(request.CookDays, planDays);
        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        var normalizedMealNames = currentPlanMealNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToList();
        if (normalizedMealNames.Count == 0)
        {
            throw new InvalidOperationException("Could not resolve the current plan for export. Generate a fresh plan and try again.");
        }
        if (normalizedMealNames.Count > totalMealCount)
        {
            normalizedMealNames = normalizedMealNames
                .Take(totalMealCount)
                .ToList();
        }

        var context = await BuildPlanContextAsync(request, cancellationToken);
        await EnsureAiMealPoolHydratedAsync(cancellationToken);
        var resolvableMealCount = Math.Min(normalizedMealNames.Count, totalMealCount);
        var selectedMeals = BuildSelectedMealsFromCurrentPlanNames(normalizedMealNames, resolvableMealCount);
        var usedTemplateTopUp = false;
        if (selectedMeals is null || selectedMeals.Count == 0)
        {
            var fallbackMeals = SelectMeals(
                MealTemplates,
                context.DietaryModes,
                request.WeeklyBudget,
                context.HouseholdFactor,
                request.PreferQuickMeals,
                context.DislikesOrAllergens,
                totalMealCount,
                mealTypeSlots,
                request.IncludeSpecialTreatMeal,
                request.SelectedSpecialTreatCookDayIndex,
                savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
                enableSavedMealRepeats: request.EnableSavedMealRepeats,
                savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
            selectedMeals = fallbackMeals.ToList();
            AddMealsToAiPool(fallbackMeals);
            usedTemplateTopUp = true;
        }

        if (selectedMeals.Count < totalMealCount)
        {
            try
            {
                var fallbackMeals = SelectMeals(
                    MealTemplates,
                    context.DietaryModes,
                    request.WeeklyBudget,
                    context.HouseholdFactor,
                    request.PreferQuickMeals,
                    context.DislikesOrAllergens,
                    totalMealCount,
                    mealTypeSlots,
                    request.IncludeSpecialTreatMeal,
                    request.SelectedSpecialTreatCookDayIndex,
                    savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
                    enableSavedMealRepeats: request.EnableSavedMealRepeats,
                    savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);

                var stitchedMeals = new List<MealTemplate>(totalMealCount);
                stitchedMeals.AddRange(selectedMeals);

                foreach (var fallbackMeal in fallbackMeals)
                {
                    if (stitchedMeals.Count >= totalMealCount)
                    {
                        break;
                    }

                    if (stitchedMeals.Any(existing =>
                            existing.Name.Equals(fallbackMeal.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    stitchedMeals.Add(fallbackMeal);
                }

                if (stitchedMeals.Count < totalMealCount)
                {
                    foreach (var fallbackMeal in fallbackMeals)
                    {
                        if (stitchedMeals.Count >= totalMealCount)
                        {
                            break;
                        }

                        stitchedMeals.Add(fallbackMeal);
                    }
                }

                selectedMeals = stitchedMeals;
                AddMealsToAiPool(fallbackMeals);
                usedTemplateTopUp = true;
            }
            catch (InvalidOperationException)
            {
                selectedMeals = NormalizeSelectedMealsForCount(selectedMeals, totalMealCount).ToList();
            }
        }

        return await BuildPlanFromMealsAsync(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: true,
            planSourceLabel: usedTemplateTopUp ? "Current plan + template top-up" : "Current plan",
            cancellationToken: cancellationToken);
    }

    public AislePilotPlanResultViewModel BuildPlanWithBudgetRebalance(
        AislePilotRequestModel request,
        int maxAttempts = 4,
        IReadOnlyList<string>? currentPlanMealNames = null)
    {
        return BuildPlanWithBudgetRebalanceAsync(request, maxAttempts, currentPlanMealNames)
            .GetAwaiter()
            .GetResult();
    }

    public async Task<AislePilotPlanResultViewModel> BuildPlanWithBudgetRebalanceAsync(
        AislePilotRequestModel request,
        int maxAttempts = 4,
        IReadOnlyList<string>? currentPlanMealNames = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedMaxAttempts = Math.Clamp(maxAttempts, 1, 8);
        var context = await BuildPlanContextAsync(request, cancellationToken);
        var planDays = NormalizePlanDays(request.PlanDays);
        var cookDays = NormalizeCookDays(request.CookDays, planDays);
        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);

        var selectedMealsFromCurrentPlan = BuildSelectedMealsFromCurrentPlanNames(currentPlanMealNames, totalMealCount);
        var baselinePlan = selectedMealsFromCurrentPlan is not null
            ? await BuildPlanFromMealsAsync(
                request,
                context,
                selectedMealsFromCurrentPlan,
                cookDays,
                usedAiGeneratedMeals: true,
                planSourceLabel: "Current plan",
                cancellationToken: cancellationToken)
            : await BuildPlanAsync(request, cancellationToken);
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

        selectedMealsFromCurrentPlan ??= BuildSelectedMealsFromCurrentPlanNames(baselineMealNames, totalMealCount);
        var cheapestPlan = baselinePlan;
        AislePilotPlanResultViewModel? cheapestChangedPlan = null;

        void ConsiderCandidate(AislePilotPlanResultViewModel candidatePlan)
        {
            if (candidatePlan.EstimatedTotalCost < cheapestPlan.EstimatedTotalCost)
            {
                cheapestPlan = candidatePlan;
            }

            if (!_planComparisonService.HasSameMealSequence(candidatePlan, baselineMealNames) &&
                (cheapestChangedPlan is null ||
                 candidatePlan.EstimatedTotalCost < cheapestChangedPlan.EstimatedTotalCost))
            {
                cheapestChangedPlan = candidatePlan;
            }
        }

        if (selectedMealsFromCurrentPlan is not null)
        {
            var targetedSwapPlan = await TryBuildTargetedLowerCostPlanAsync(
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
                    return ApplyBudgetRebalanceStatus(targetedSwapPlan, baselinePlan, baselineMealNames);
                }
            }
        }

        try
        {
            var lowestCostPlan = await BuildLowestCostRebalancePlanAsync(request, context, cookDays, cancellationToken);
            lowestCostPlan = RebasePlanToOriginalBudget(lowestCostPlan, request.WeeklyBudget);
            ConsiderCandidate(lowestCostPlan);
            if (!lowestCostPlan.IsOverBudget)
            {
                return ApplyBudgetRebalanceStatus(lowestCostPlan, baselinePlan, baselineMealNames);
            }
        }
        catch (InvalidOperationException)
        {
            // Ignore; fallback target-based passes below will handle this.
        }

        var rebalanceTargets = BuildBudgetRebalanceTargets(
            request.WeeklyBudget,
            baselinePlan.EstimatedTotalCost,
            normalizedMaxAttempts - 1);
        foreach (var targetBudget in rebalanceTargets)
        {
            var candidateRequest = CloneRequest(request);
            candidateRequest.WeeklyBudget = targetBudget;

            AislePilotPlanResultViewModel candidatePlan;
            try
            {
                candidatePlan = await BuildPlanAsync(candidateRequest, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            candidatePlan = RebasePlanToOriginalBudget(candidatePlan, request.WeeklyBudget);
            ConsiderCandidate(candidatePlan);

            if (!candidatePlan.IsOverBudget)
            {
                return ApplyBudgetRebalanceStatus(candidatePlan, baselinePlan, baselineMealNames);
            }
        }

        var finalPlan = cheapestPlan;
        if (cheapestChangedPlan is not null &&
            cheapestChangedPlan.EstimatedTotalCost < baselinePlan.EstimatedTotalCost)
        {
            finalPlan = cheapestChangedPlan;
        }

        return ApplyBudgetRebalanceStatus(finalPlan, baselinePlan, baselineMealNames);
    }

    internal bool ShouldUseTemplateFallback()
    {
        return _allowTemplateFallback &&
               (!_enableAiGeneration || _httpClient is null || string.IsNullOrWhiteSpace(_apiKey));
    }

    private AislePilotPlanResultViewModel BuildPlanFromTemplateCatalog(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        int totalMealCount)
    {
        return BuildPlanFromTemplateCatalogAsync(request, context, cookDays, totalMealCount).GetAwaiter().GetResult();
    }

    internal async Task<AislePilotPlanResultViewModel> BuildPlanFromTemplateCatalogAsync(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        int totalMealCount,
        CancellationToken cancellationToken = default)
    {
        var selectedMeals = SelectMeals(
            MealTemplates,
            context.DietaryModes,
            request.WeeklyBudget,
            context.HouseholdFactor,
            request.PreferQuickMeals,
            context.DislikesOrAllergens,
            totalMealCount,
            BuildMealTypeSlots(request),
            request.IncludeSpecialTreatMeal,
            request.SelectedSpecialTreatCookDayIndex,
            savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
            enableSavedMealRepeats: request.EnableSavedMealRepeats,
            savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);

        // Keep swap behavior consistent by making fallback-selected meals available in the in-memory pool.
        AddMealsToAiPool(selectedMeals);

        return await BuildPlanFromMealsAsync(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: false,
            planSourceLabel: "Template fallback",
            cancellationToken: cancellationToken);
    }

    private AislePilotPlanResultViewModel? TryBuildPlanWithAi(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        int totalMealCount)
    {
        try
        {
            return TryBuildPlanWithAiAsync(request, context, cookDays, totalMealCount).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AislePilot AI meal generation failed.");
            return null;
        }
    }

    internal async Task<AislePilotPlanResultViewModel?> TryBuildPlanWithAiAsync(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        int totalMealCount,
        CancellationToken cancellationToken = default)
    {
        if (!_enableAiGeneration || _httpClient is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger?.LogInformation("OPENAI_API_KEY is missing. AislePilot will only use the AI meal pool if compatible meals are already cached.");
            return null;
        }

        if (totalMealCount > MaxFreshAiPlanMeals)
        {
            _logger?.LogInformation(
                "AislePilot skipping fresh AI meal generation for {MealCount} requested meals; using pool/template selection.",
                totalMealCount);
            if (!request.IncludeSpecialTreatMeal)
            {
                return null;
            }

            return await TryBuildPlanWithDedicatedSpecialTreatAsync(
                request,
                context,
                cookDays,
                totalMealCount,
                cancellationToken);
        }

        using var generationBudgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        generationBudgetCts.CancelAfter(OpenAiGenerationBudget);
        var generationToken = generationBudgetCts.Token;

        var requestedMealCount = GetRequestedAiMealCount(totalMealCount);
        var aiBatch = await TryRequestAiMealBatchAsync(
            request,
            context,
            cookDays,
            totalMealCount,
            requestedMealCount,
            PrimaryAiMealPlanMaxTokens,
            compactJson: false,
            generationToken);

        if (aiBatch is null && ShouldRetryWithCompactPayload(totalMealCount))
        {
            _logger?.LogWarning(
                "AislePilot AI generation returned invalid or truncated JSON. Retrying with a compact {MealCount}-meal payload.",
                totalMealCount);

            aiBatch = await TryRequestAiMealBatchAsync(
                request,
                context,
                cookDays,
                totalMealCount,
                totalMealCount,
                RetryAiMealPlanMaxTokens,
                compactJson: true,
                generationToken);
        }

        if (aiBatch is null)
        {
            _logger?.LogWarning("AislePilot AI generation returned content that did not pass validation.");
            return null;
        }

        var aiMeals = aiBatch.Meals.ToList();
        var mealTypeSlots = BuildMealTypeSlots(request);
        var savedEnjoyedMealNames = ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState);
        var selectionCandidates = aiMeals.ToList();
        if (request.EnableSavedMealRepeats && savedEnjoyedMealNames.Count > 0)
        {
            await EnsureAiMealPoolHydratedAsync(cancellationToken);
            var savedFallbackCandidates = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens)
                .Concat(FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates))
                .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToDictionary(meal => meal.Name, meal => meal, StringComparer.OrdinalIgnoreCase);
            foreach (var savedMealName in savedEnjoyedMealNames)
            {
                if (selectionCandidates.Any(meal => meal.Name.Equals(savedMealName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (savedFallbackCandidates.TryGetValue(savedMealName, out var savedFallbackMeal))
                {
                    selectionCandidates.Add(savedFallbackMeal);
                }
            }
        }

        if (!HasSlotCoverageForMealTypes(selectionCandidates, mealTypeSlots))
        {
            _logger?.LogWarning(
                "AislePilot AI generation returned meals without required slot coverage for {MealTypes}; falling back.",
                string.Join(",", mealTypeSlots));
            return null;
        }

        if (request.IncludeSpecialTreatMeal &&
            mealTypeSlots.Any(slot => slot.Equals("Dinner", StringComparison.OrdinalIgnoreCase)) &&
            !HasSpecialTreatDinner(selectionCandidates, mealTypeSlots))
        {
            var dedicatedSpecialTreatMeal = await TryGenerateSpecialTreatMealWithAiAsync(
                request,
                context,
                aiMeals.Select(meal => meal.Name).ToArray(),
                generationToken);
            if (dedicatedSpecialTreatMeal is not null)
            {
                aiMeals.Add(dedicatedSpecialTreatMeal);
                selectionCandidates.Add(dedicatedSpecialTreatMeal);
            }
        }

        IReadOnlyList<MealTemplate> selectedMeals;
        try
        {
            selectedMeals = SelectMeals(
                selectionCandidates,
                context.DietaryModes,
                request.WeeklyBudget,
                context.HouseholdFactor,
                request.PreferQuickMeals,
                context.DislikesOrAllergens,
                totalMealCount,
                mealTypeSlots,
                request.IncludeSpecialTreatMeal,
                request.SelectedSpecialTreatCookDayIndex,
                savedEnjoyedMealNames: savedEnjoyedMealNames,
                enableSavedMealRepeats: request.EnableSavedMealRepeats,
                savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
        }
        catch (InvalidOperationException ex) when (request.IncludeSpecialTreatMeal)
        {
            _logger?.LogWarning(
                ex,
                "AislePilot AI generation did not include a valid special treat dinner; serving core meals and queueing background special generation.");
            try
            {
                var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);
                var combinedCandidates = selectionCandidates
                    .Concat(templateMeals)
                    .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
                    .ToList();
                selectedMeals = SelectMeals(
                    combinedCandidates,
                    context.DietaryModes,
                    request.WeeklyBudget,
                    context.HouseholdFactor,
                    request.PreferQuickMeals,
                    context.DislikesOrAllergens,
                    totalMealCount,
                    mealTypeSlots,
                    includeSpecialTreatMeal: true,
                    selectedSpecialTreatCookDayIndex: request.SelectedSpecialTreatCookDayIndex,
                    savedEnjoyedMealNames: savedEnjoyedMealNames,
                    enableSavedMealRepeats: request.EnableSavedMealRepeats,
                    savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
            }
            catch (InvalidOperationException)
            {
                QueueSpecialTreatGeneration(request, context, aiMeals.Select(meal => meal.Name).ToArray());
                try
                {
                    selectedMeals = SelectMeals(
                        selectionCandidates,
                        context.DietaryModes,
                        request.WeeklyBudget,
                        context.HouseholdFactor,
                        request.PreferQuickMeals,
                        context.DislikesOrAllergens,
                        totalMealCount,
                        mealTypeSlots,
                        includeSpecialTreatMeal: false,
                        savedEnjoyedMealNames: savedEnjoyedMealNames,
                        enableSavedMealRepeats: request.EnableSavedMealRepeats,
                        savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        if (!HasUniqueMealNames(selectedMeals, totalMealCount, mealTypeSlots))
        {
            _logger?.LogWarning(
                "AislePilot AI generation did not yield enough slot-compatible meals for {MealCount} requested meals.",
                totalMealCount);
            return null;
        }

        _logger?.LogInformation(
            "AislePilot generated {MealCount} meals via AI and selected {SelectedMealCount} for the visible plan. OpenAIRequestId={OpenAIRequestId}",
            aiMeals.Count,
            selectedMeals.Count,
            aiBatch.OpenAiRequestId ?? "n/a");

        var persistedMeals = await PersistAiMealsAsync(aiMeals, generationToken);
        if (persistedMeals.Count > 0)
        {
            AddMealsToAiPool(persistedMeals);
        }
        else
        {
            _logger?.LogWarning(
                "AislePilot generated {MealCount} meals but none were persisted; skipping shared AI meal pool update.",
                aiMeals.Count);
        }

        return BuildPlanFromMeals(request, context, selectedMeals, cookDays, usedAiGeneratedMeals: true);
    }

    private async Task<AislePilotPlanResultViewModel?> TryBuildPlanWithDedicatedSpecialTreatAsync(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        int totalMealCount,
        CancellationToken cancellationToken = default)
    {
        if (!_enableAiGeneration || _httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        if (!mealTypeSlots.Any(slot => slot.Equals("Dinner", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        await EnsureAiMealPoolHydratedAsync(cancellationToken);
        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);
        var combinedCandidates = pooledMeals
            .Concat(templateMeals)
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
            .ToList();
        if (combinedCandidates.Count == 0)
        {
            return null;
        }

        IReadOnlyList<MealTemplate> selectedMeals;
        try
        {
            selectedMeals = SelectMeals(
                combinedCandidates,
                context.DietaryModes,
                request.WeeklyBudget,
                context.HouseholdFactor,
                request.PreferQuickMeals,
                context.DislikesOrAllergens,
                totalMealCount,
                mealTypeSlots,
                includeSpecialTreatMeal: false,
                savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
                enableSavedMealRepeats: request.EnableSavedMealRepeats,
                savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        using var generationBudgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        generationBudgetCts.CancelAfter(OpenAiGenerationBudget);
        var generationToken = generationBudgetCts.Token;

        var dedicatedSpecialTreatMeal = await TryGenerateSpecialTreatMealWithAiAsync(
            request,
            context,
            selectedMeals.Select(meal => meal.Name).ToArray(),
            generationToken);
        if (dedicatedSpecialTreatMeal is null)
        {
            var fallbackPatchedMeals = selectedMeals.ToList();
            var localTreatApplied = TryApplySpecialTreatMeal(
                fallbackPatchedMeals,
                combinedCandidates,
                mealTypeSlots,
                context.HouseholdFactor,
                request.SelectedSpecialTreatCookDayIndex);
            if (localTreatApplied && HasSpecialTreatDinner(fallbackPatchedMeals, mealTypeSlots))
            {
                return await BuildPlanFromMealsAsync(
                    request,
                    context,
                    fallbackPatchedMeals,
                    cookDays,
                    usedAiGeneratedMeals: pooledMeals.Count > 0,
                    planSourceLabel: "AI/template mix",
                    cancellationToken: cancellationToken);
            }

            QueueSpecialTreatGeneration(request, context, selectedMeals.Select(meal => meal.Name).ToArray());
            return await BuildPlanFromMealsAsync(
                request,
                context,
                selectedMeals,
                cookDays,
                usedAiGeneratedMeals: pooledMeals.Count > 0,
                planSourceLabel: "AI/template mix (special treat pending)",
                cancellationToken: cancellationToken);
        }

        var patchedMeals = selectedMeals.ToList();
        var applied = ForceReplaceDinnerWithSpecialTreatMeal(
            patchedMeals,
            dedicatedSpecialTreatMeal,
            mealTypeSlots,
            context.HouseholdFactor,
            request.SelectedSpecialTreatCookDayIndex);
        if (!applied || !HasSpecialTreatDinner(patchedMeals, mealTypeSlots))
        {
            var localTreatApplied = TryApplySpecialTreatMeal(
                patchedMeals,
                combinedCandidates,
                mealTypeSlots,
                context.HouseholdFactor,
                request.SelectedSpecialTreatCookDayIndex);
            if (localTreatApplied && HasSpecialTreatDinner(patchedMeals, mealTypeSlots))
            {
                return await BuildPlanFromMealsAsync(
                    request,
                    context,
                    patchedMeals,
                    cookDays,
                    usedAiGeneratedMeals: pooledMeals.Count > 0,
                    planSourceLabel: "AI/template mix",
                    cancellationToken: cancellationToken);
            }

            QueueSpecialTreatGeneration(request, context, selectedMeals.Select(meal => meal.Name).ToArray());
            return await BuildPlanFromMealsAsync(
                request,
                context,
                selectedMeals,
                cookDays,
                usedAiGeneratedMeals: pooledMeals.Count > 0,
                planSourceLabel: "AI/template mix (special treat pending)",
                cancellationToken: cancellationToken);
        }

        var persistedTreatMeals = await PersistAiMealsAsync([dedicatedSpecialTreatMeal], generationToken);
        if (persistedTreatMeals.Count > 0)
        {
            AddMealsToAiPool(persistedTreatMeals);
        }
        else
        {
            AddMealsToAiPool([dedicatedSpecialTreatMeal]);
        }

        return await BuildPlanFromMealsAsync(
            request,
            context,
            patchedMeals,
            cookDays,
            usedAiGeneratedMeals: true,
            planSourceLabel: "Template + AI special treat",
            cancellationToken: cancellationToken);
    }

    private async Task<AiMealBatchResult?> TryRequestAiMealBatchAsync(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        int totalMealCount,
        int requestedMealCount,
        int maxTokens,
        bool compactJson,
        CancellationToken cancellationToken)
    {
        var planDays = NormalizePlanDays(request.PlanDays);
        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var prompt = BuildAiMealPlanPrompt(
            request,
            context,
            cookDays,
            planDays,
            mealsPerDay,
            mealTypeSlots,
            totalMealCount,
            requestedMealCount,
            compactJson);
        var requestBody = new
        {
            model = _model,
            temperature = compactJson ? 0.6 : 0.9,
            max_tokens = maxTokens,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You generate practical weekly meal plans for a UK grocery-planning app. Always return valid JSON only. Use UK English. Prioritise variety and never repeat the same meal in a single plan unless explicitly impossible."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        var responseContent = await SendOpenAiRequestWithRetryAsync(requestBody, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
        var rawJson = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            var normalizedJson = NormalizeModelJson(rawJson);
            if (!TryParseAiPlanPayloadWithRecovery(normalizedJson, out var aiPayload, out var repairedJson))
            {
                var sample = normalizedJson.Length <= 280 ? normalizedJson : normalizedJson[..280];
                _logger?.LogWarning(
                    "AislePilot AI generation returned malformed JSON after repair attempts. PayloadSample={PayloadSample}",
                    sample);
                return null;
            }

            if (!string.IsNullOrEmpty(repairedJson))
            {
                _logger?.LogInformation("AislePilot AI JSON required light repair before parsing.");
            }

            var effectiveJson = repairedJson ?? normalizedJson;
            var aiMeals = ValidateAndMapAiMeals(
                aiPayload,
                context.DietaryModes,
                totalMealCount,
                requestedMealCount,
                mealsPerDay,
                mealTypeSlots,
                requireSpecialTreatDinner: false,
                out var validationReason);
            if (aiMeals is null)
            {
                var sample = effectiveJson.Length <= 280 ? effectiveJson : effectiveJson[..280];
                _logger?.LogWarning(
                    "AislePilot AI payload validation failed. Reason={Reason}. PayloadSample={PayloadSample}",
                    validationReason ?? "unknown",
                    sample);
                return null;
            }

            var requestId = (string?)null;
            return new AiMealBatchResult(aiMeals, requestId);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "AislePilot AI generation returned malformed JSON.");
            return null;
        }
    }

    private AislePilotPlanResultViewModel? TryBuildPlanFromAiPool(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        int totalMealCount)
    {
        EnsureAiMealPoolHydrated();
        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        if (pooledMeals.Count == 0)
        {
            _logger?.LogWarning("AislePilot AI meal pool did not contain compatible meals for the current request.");
            return null;
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        if (!HasSlotCoverageForMealTypes(pooledMeals, mealTypeSlots))
        {
            _logger?.LogInformation(
                "AislePilot AI meal pool lacked slot coverage for requested meal types {MealTypes}; requesting fresh AI meals.",
                string.Join(",", mealTypeSlots));
            return null;
        }

        IReadOnlyList<MealTemplate> selectedMeals;
        try
        {
            selectedMeals = SelectMeals(
                pooledMeals,
                context.DietaryModes,
                request.WeeklyBudget,
                context.HouseholdFactor,
                request.PreferQuickMeals,
                context.DislikesOrAllergens,
                totalMealCount,
                mealTypeSlots,
                request.IncludeSpecialTreatMeal,
                request.SelectedSpecialTreatCookDayIndex,
                savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
                enableSavedMealRepeats: request.EnableSavedMealRepeats,
                savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
        }
        catch (InvalidOperationException ex) when (request.IncludeSpecialTreatMeal)
        {
            _logger?.LogInformation(
                ex,
                "AislePilot AI meal pool lacked a valid special treat dinner; serving core meals and queueing background special generation.");
            try
            {
                var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);
                var combinedCandidates = pooledMeals
                    .Concat(templateMeals)
                    .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
                    .ToList();
                selectedMeals = SelectMeals(
                    combinedCandidates,
                    context.DietaryModes,
                    request.WeeklyBudget,
                    context.HouseholdFactor,
                    request.PreferQuickMeals,
                    context.DislikesOrAllergens,
                    totalMealCount,
                    mealTypeSlots,
                    includeSpecialTreatMeal: true,
                    selectedSpecialTreatCookDayIndex: request.SelectedSpecialTreatCookDayIndex,
                    savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
                    enableSavedMealRepeats: request.EnableSavedMealRepeats,
                    savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
            }
            catch (InvalidOperationException)
            {
                QueueSpecialTreatGeneration(request, context, pooledMeals.Select(meal => meal.Name).ToArray());
                try
                {
                    selectedMeals = SelectMeals(
                        pooledMeals,
                        context.DietaryModes,
                        request.WeeklyBudget,
                        context.HouseholdFactor,
                        request.PreferQuickMeals,
                        context.DislikesOrAllergens,
                        totalMealCount,
                        mealTypeSlots,
                        includeSpecialTreatMeal: false,
                        savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
                        enableSavedMealRepeats: request.EnableSavedMealRepeats,
                        savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        if (!HasUniqueMealNames(selectedMeals, totalMealCount, mealTypeSlots))
        {
            _logger?.LogInformation(
                "AislePilot AI meal pool did not contain enough slot-compatible meals for {MealCount} requested meals; requesting fresh AI meals.",
                totalMealCount);
            return null;
        }

        return BuildPlanFromMeals(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: true,
            planSourceLabel: "AI meal pool");
    }

    internal async Task<AislePilotPlanResultViewModel?> TryBuildPlanFromAiPoolAsync(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        int totalMealCount,
        CancellationToken cancellationToken = default)
    {
        await EnsureAiMealPoolHydratedAsync(cancellationToken);
        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        if (pooledMeals.Count == 0)
        {
            _logger?.LogWarning("AislePilot AI meal pool did not contain compatible meals for the current request.");
            return null;
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        if (!HasSlotCoverageForMealTypes(pooledMeals, mealTypeSlots))
        {
            _logger?.LogInformation(
                "AislePilot AI meal pool lacked slot coverage for requested meal types {MealTypes}; requesting fresh AI meals.",
                string.Join(",", mealTypeSlots));
            return null;
        }

        IReadOnlyList<MealTemplate> selectedMeals;
        try
        {
            selectedMeals = SelectMeals(
                pooledMeals,
                context.DietaryModes,
                request.WeeklyBudget,
                context.HouseholdFactor,
                request.PreferQuickMeals,
                context.DislikesOrAllergens,
                totalMealCount,
                mealTypeSlots,
                request.IncludeSpecialTreatMeal,
                request.SelectedSpecialTreatCookDayIndex,
                savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
                enableSavedMealRepeats: request.EnableSavedMealRepeats,
                savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
        }
        catch (InvalidOperationException ex) when (request.IncludeSpecialTreatMeal)
        {
            _logger?.LogInformation(
                ex,
                "AislePilot AI meal pool lacked a valid special treat dinner; serving core meals and queueing background special generation.");
            try
            {
                var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);
                var combinedCandidates = pooledMeals
                    .Concat(templateMeals)
                    .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
                    .ToList();
                selectedMeals = SelectMeals(
                    combinedCandidates,
                    context.DietaryModes,
                    request.WeeklyBudget,
                    context.HouseholdFactor,
                    request.PreferQuickMeals,
                    context.DislikesOrAllergens,
                    totalMealCount,
                    mealTypeSlots,
                    includeSpecialTreatMeal: true,
                    selectedSpecialTreatCookDayIndex: request.SelectedSpecialTreatCookDayIndex,
                    savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
                    enableSavedMealRepeats: request.EnableSavedMealRepeats,
                    savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
            }
            catch (InvalidOperationException)
            {
                QueueSpecialTreatGeneration(request, context, pooledMeals.Select(meal => meal.Name).ToArray());
                try
                {
                    selectedMeals = SelectMeals(
                        pooledMeals,
                        context.DietaryModes,
                        request.WeeklyBudget,
                        context.HouseholdFactor,
                        request.PreferQuickMeals,
                        context.DislikesOrAllergens,
                        totalMealCount,
                        mealTypeSlots,
                        includeSpecialTreatMeal: false,
                        savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
                        enableSavedMealRepeats: request.EnableSavedMealRepeats,
                        savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        if (!HasUniqueMealNames(selectedMeals, totalMealCount, mealTypeSlots))
        {
            _logger?.LogInformation(
                "AislePilot AI meal pool did not contain enough slot-compatible meals for {MealCount} requested meals; requesting fresh AI meals.",
                totalMealCount);
            return null;
        }

        return await BuildPlanFromMealsAsync(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: true,
            planSourceLabel: "AI meal pool",
            cancellationToken: cancellationToken);
    }

    public AislePilotPlanResultViewModel SwapMealForDay(
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames)
    {
        return SwapMealForDayAsync(request, dayIndex, currentMealName, currentPlanMealNames, seenMealNames)
            .GetAwaiter()
            .GetResult();
    }

    public async Task<AislePilotPlanResultViewModel> SwapMealForDayAsync(
        AislePilotRequestModel request,
        int dayIndex,
        string? currentMealName,
        IReadOnlyList<string>? currentPlanMealNames,
        IReadOnlyList<string>? seenMealNames,
        CancellationToken cancellationToken = default)
    {
        var planDays = NormalizePlanDays(request.PlanDays);
        var cookDays = NormalizeCookDays(request.CookDays, planDays);
        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        if (dayIndex < 0 || dayIndex >= totalMealCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayIndex),
                $"Day index must be between 0 and {totalMealCount - 1}.");
        }

        var context = await BuildPlanContextAsync(request, cancellationToken);
        await EnsureAiMealPoolHydratedAsync(cancellationToken);
        var selectedMeals = BuildSelectedMealsFromCurrentPlanNames(currentPlanMealNames, totalMealCount);
        if (selectedMeals is null && _allowTemplateFallback)
        {
            var fallbackPlan = await BuildPlanFromTemplateCatalogAsync(
                request,
                context,
                cookDays,
                totalMealCount,
                cancellationToken);
            selectedMeals = BuildSelectedMealsFromCurrentPlanNames(
                fallbackPlan.MealPlan.Select(meal => meal.MealName).ToList(),
                totalMealCount);
        }

        if (selectedMeals is null)
        {
            throw new InvalidOperationException("Could not resolve the current plan for swapping. Generate a fresh AI plan and try again.");
        }

        var availableAiMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var currentName = string.IsNullOrWhiteSpace(currentMealName)
            ? selectedMeals[dayIndex].Name
            : currentMealName.Trim();
        var leftoverDays = Math.Max(0, planDays - cookDays);
        var requestedLeftoverSourceDays = ParseRequestedLeftoverSourceDays(
            request.LeftoverCookDayIndexesCsv,
            cookDays,
            leftoverDays,
            planDays);
        var mealPortionMultipliers = BuildMealPortionMultipliers(
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

        MealTemplate? replacement = null;
        var planSourceLabel = "AI meal pool";
        if (availableAiMeals.Count > 0)
        {
            var unseenPoolMeals = availableAiMeals
                .Where(meal => !normalizedSeenMealNames.Contains(meal.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var compatibleUnseenPoolMeals = unseenPoolMeals
                .Where(meal => SupportsMealType(meal, mealType))
                .ToList();
            if (compatibleUnseenPoolMeals.Count > 0)
            {
                replacement = SelectSwapCandidate(
                    compatibleUnseenPoolMeals,
                    selectedMeals,
                    dayIndex,
                    currentName,
                    request.WeeklyBudget,
                    context.HouseholdFactor,
                    request.PreferQuickMeals,
                    IsHighProteinPreferred(context.DietaryModes),
                    mealType,
                    dayMultiplier,
                    mealsPerDay);
            }
        }

        if (replacement is null)
        {
            replacement = await TryBuildReplacementMealWithAiAsync(
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
            replacement = TrySelectTemplateSwapCandidate(
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

        AddMealsToAiPool([replacement]);
        selectedMeals[dayIndex] = replacement;
        return await BuildPlanFromMealsAsync(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: !planSourceLabel.Equals("Template swap", StringComparison.OrdinalIgnoreCase),
            planSourceLabel: planSourceLabel,
            cancellationToken: cancellationToken);
    }

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

    private MealTemplate? TryBuildReplacementMealWithAi(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        string currentMealName,
        int dayMultiplier,
        string mealType,
        IReadOnlyList<string> seenMealNames)
    {
        try
        {
            return TryBuildReplacementMealWithAiAsync(
                request,
                context,
                selectedMeals,
                dayIndex,
                currentMealName,
                dayMultiplier,
                mealType,
                seenMealNames).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AislePilot AI meal swap failed.");
            return null;
        }
    }

    private async Task<MealTemplate?> TryBuildReplacementMealWithAiAsync(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        string currentMealName,
        int dayMultiplier,
        string mealType,
        IReadOnlyList<string> seenMealNames,
        CancellationToken cancellationToken = default)
    {
        if (!_enableAiGeneration || _httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var excludedMealNames = selectedMeals
            .Where((_, index) => index != dayIndex)
            .Select(meal => meal.Name)
            .Concat(seenMealNames)
            .ToArray();
        var prompt = BuildAiMealSwapPrompt(
            request,
            context,
            currentMealName,
            excludedMealNames,
            dayMultiplier,
            mealType,
            BuildMealTypeSlots(request).Count);
        var requestBody = new
        {
            model = _model,
            temperature = 0.9,
            max_tokens = 1000,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You generate one practical replacement meal for a UK grocery-planning app. Always return valid JSON only. Use UK English."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        var responseContent = await SendOpenAiRequestWithRetryAsync(requestBody, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
        var rawJson = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        var normalizedJson = NormalizeModelJson(rawJson);
        if (!TryParseAiMealPayloadWithRecovery(normalizedJson, out var aiPayload))
        {
            return null;
        }

        var strictModes = ResolveHardDietaryModes(context.DietaryModes);
        var replacement = ValidateAndMapAiMeal(
            aiPayload,
            strictModes,
            requireRecipeSteps: true,
            suitableMealTypes: [mealType],
            out _);
        if (replacement is null)
        {
            return null;
        }

        if (replacement.Name.Equals(currentMealName, StringComparison.OrdinalIgnoreCase) ||
            excludedMealNames.Contains(replacement.Name, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var persistedMeals = await PersistAiMealsAsync([replacement], cancellationToken);
        if (persistedMeals.Count > 0)
        {
            AddMealsToAiPool(persistedMeals);
        }
        else
        {
            _logger?.LogWarning(
                "AislePilot generated swap meal '{MealName}' but it was not persisted; skipping shared AI meal pool update.",
                replacement.Name);
        }

        return replacement;
    }

    private static MealTemplate? TrySelectTemplateSwapCandidate(
        PlanContext context,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        string currentMealName,
        decimal weeklyBudget,
        bool preferQuickMeals,
        string mealType,
        int dayMultiplier,
        int mealsPerDay,
        IReadOnlyList<string> seenMealNames)
    {
        var templateCandidates = FilterMeals(context.DietaryModes, context.DislikesOrAllergens);
        if (templateCandidates.Count == 0)
        {
            return null;
        }

        var unseenTemplates = templateCandidates
            .Where(meal => !seenMealNames.Contains(meal.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (unseenTemplates.Count == 0)
        {
            return null;
        }

        return SelectSwapCandidate(
            unseenTemplates,
            selectedMeals,
            dayIndex,
            currentMealName,
            weeklyBudget,
            context.HouseholdFactor,
            preferQuickMeals,
            IsHighProteinPreferred(context.DietaryModes),
            mealType,
            dayMultiplier,
            mealsPerDay);
    }

    private static IReadOnlyList<WarmupProfileWithTarget> BuildWarmupProfiles(
        int minPerSingleMode,
        int minPerKeyPair)
    {
        var profiles = new List<WarmupProfileWithTarget>(WarmupProfilesSingleMode.Length + WarmupProfilesKeyPairs.Length);
        if (minPerSingleMode > 0)
        {
            profiles.AddRange(WarmupProfilesSingleMode.Select(profile =>
                new WarmupProfileWithTarget(profile.Name, profile.Modes, minPerSingleMode)));
        }

        if (minPerKeyPair > 0)
        {
            profiles.AddRange(WarmupProfilesKeyPairs.Select(profile =>
                new WarmupProfileWithTarget(profile.Name, profile.Modes, minPerKeyPair)));
        }

        return profiles;
    }

    private static IReadOnlyList<AislePilotWarmupCoverageViewModel> BuildWarmupCoverage(
        IReadOnlyList<WarmupProfileWithTarget> profiles)
    {
        return profiles
            .Select(profile =>
            {
                var count = GetCompatibleAiPoolMeals(profile.Modes, string.Empty).Count;
                return new AislePilotWarmupCoverageViewModel
                {
                    Profile = profile.Name,
                    Modes = profile.Modes.ToArray(),
                    Target = profile.Target,
                    Count = count,
                    Deficit = Math.Max(0, profile.Target - count)
                };
            })
            .ToList();
    }

    private async Task<MealTemplate?> TryGenerateSpecialTreatMealWithAiAsync(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<string> excludedMealNames,
        CancellationToken cancellationToken)
    {
        if (!_enableAiGeneration || _httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        if (!mealTypeSlots.Any(slot => slot.Equals("Dinner", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var strictModes = ResolveHardDietaryModes(context.DietaryModes);
        var prompt = BuildAiSpecialTreatMealPrompt(request, context, mealTypeSlots.Count, excludedMealNames);
        var requestBody = new
        {
            model = _model,
            temperature = 0.85,
            max_tokens = SpecialTreatMealMaxTokens,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You generate one indulgent special-treat dinner for a UK grocery-planning app. Always return valid JSON only. Use UK English."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        var responseContent = await SendOpenAiRequestWithRetryAsync(requestBody, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
        var rawJson = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        var normalizedJson = NormalizeModelJson(rawJson);
        if (!TryParseAiMealPayloadWithRecovery(normalizedJson, out var aiPayload))
        {
            return null;
        }

        var specialTreatMeal = ValidateAndMapAiMeal(
            aiPayload,
            strictModes,
            requireRecipeSteps: true,
            suitableMealTypes: ["Dinner"],
            out _);
        if (specialTreatMeal is null)
        {
            return null;
        }

        if (excludedMealNames.Contains(specialTreatMeal.Name, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!IsSpecialTreatMealCandidate(specialTreatMeal))
        {
            return null;
        }

        return MarkMealAsSpecialTreat(specialTreatMeal);
    }

    private async Task<MealTemplate?> TryGenerateWarmupMealWithAiAsync(
        IReadOnlyList<string> dietaryModes,
        IReadOnlyList<string> excludedMealNames,
        CancellationToken cancellationToken)
    {
        if (!_enableAiGeneration || _httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var strictModes = dietaryModes
            .Where(mode => !mode.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (strictModes.Length == 0)
        {
            return null;
        }

        var prompt = BuildAiWarmupMealPrompt(strictModes, excludedMealNames);
        var requestBody = new
        {
            model = _model,
            temperature = 0.75,
            max_tokens = WarmupMealMaxTokens,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You generate one practical dinner for a UK grocery-planning app. Always return valid JSON only. Use UK English."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        var responseContent = await SendOpenAiRequestWithRetryAsync(requestBody, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
        var rawJson = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        var normalizedJson = NormalizeModelJson(rawJson);
        if (!TryParseAiMealPayloadWithRecovery(normalizedJson, out var aiPayload))
        {
            return null;
        }

        var meal = ValidateAndMapAiMeal(aiPayload, strictModes, requireRecipeSteps: true, out _);
        if (meal is null)
        {
            return null;
        }

        return excludedMealNames.Contains(meal.Name, StringComparer.OrdinalIgnoreCase)
            ? null
            : meal;
    }

    private static string BuildAiSpecialTreatMealPrompt(
        AislePilotRequestModel request,
        PlanContext context,
        int mealsPerDay,
        IReadOnlyList<string> excludedMealNames)
    {
        var strictModes = context.DietaryModes
            .Where(mode => !mode.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var strictModeText = strictModes.Length == 0 ? "Balanced" : string.Join(", ", strictModes);
        var dislikesText = string.IsNullOrWhiteSpace(context.DislikesOrAllergens) ? "none" : context.DislikesOrAllergens;
        var excludedText = excludedMealNames.Count == 0 ? "none" : string.Join(", ", excludedMealNames);
        var targetMealBudget = decimal.Round(
            request.WeeklyBudget / (7m * Math.Max(1, mealsPerDay)),
            2,
            MidpointRounding.AwayFromZero);

        return $$"""
Generate one indulgent dinner for a UK grocery-planning app.

Planner inputs:
- Supermarket: {{context.Supermarket}}
- Target meal budget: {{targetMealBudget.ToString("0.##", CultureInfo.InvariantCulture)}} GBP for serving 2
- Household size: {{request.HouseholdSize}}
- Portion size: {{context.PortionSize}}
- Dietary requirements: {{strictModeText}}
- Dislikes or allergens: {{dislikesText}}
- Avoid these meal names: {{excludedText}}

Rules:
- Return exactly one dinner object.
- It must be clearly indulgent and feel like a special occasion dinner, not a normal weekday dinner.
- It must be different from every excluded meal name.
- Use UK English.
- Keep it realistic for a UK supermarket shop.
- Use typical UK non-promo shelf prices (no loyalty-only offers, markdowns, or extreme bulk discounts).
- Respect dietary requirements and dislikes/allergens strictly.
- Every meal must include 3-7 ingredients only.
- Department must be one of: Produce, Bakery, Meat & Fish, Dairy & Eggs, Frozen, Tins & Dry Goods, Spices & Sauces, Snacks, Drinks, Household, Other
- Unit should be short plain text such as kg, g, pcs, tins, jar, bottle, pack, head, fillets.
- `baseCostForTwo` is an estimated GBP cost for serving 2 people once.
- `estimatedCostForTwo` is the portion of the meal cost attributable to that ingredient for serving 2 people once.
- Use realistic prices, avoid placeholder values, and keep all monetary values to 2 decimal places.
- The sum of `estimatedCostForTwo` across ingredients should be broadly consistent with `baseCostForTwo`.
- `quantityForTwo` must be a positive number.
- `tags` must only use values from: Balanced, High-Protein, Vegetarian, Vegan, Pescatarian, Gluten-Free, Special Treat
- Include all requested dietary modes in `tags` (except Balanced is optional) and include `Special Treat` in `tags`.
- `recipeSteps` must contain 5-6 concrete, meal-specific cooking steps in order.
- Include `nutritionPerServing` for one medium serving (not household total), with calories and grams for protein/carbs/fat.

Return JSON only with this schema:
{
  "name": "",
  "baseCostForTwo": 0,
  "isQuick": false,
  "tags": ["Special Treat"],
  "recipeSteps": [
    "",
    "",
    "",
    "",
    ""
  ],
  "nutritionPerServing": {
    "calories": 0,
    "proteinGrams": 0,
    "carbsGrams": 0,
    "fatGrams": 0
  },
  "ingredients": [
    {
      "name": "",
      "department": "",
      "quantityForTwo": 0,
      "unit": "",
      "estimatedCostForTwo": 0
    }
  ]
}
""";
    }

    private static string BuildAiWarmupMealPrompt(
        IReadOnlyList<string> strictModes,
        IReadOnlyList<string> excludedMealNames)
    {
        var strictModeText = string.Join(", ", strictModes);
        var excludedText = excludedMealNames.Count == 0
            ? "none"
            : string.Join(", ", excludedMealNames);

        return $$"""
Generate one dinner for a UK grocery-planning app to improve future cache coverage.

Planner inputs:
- Dietary requirements: {{strictModeText}}
- Avoid these meal names: {{excludedText}}
- Target budget: 4.5 to 9.5 GBP for serving 2

Rules:
- Return exactly one dinner object.
- It must be different from all excluded meal names.
- Use UK English.
- Keep it realistic for a UK supermarket shop.
- Use typical UK non-promo shelf prices (no loyalty-only offers, markdowns, or extreme bulk discounts).
- Respect dietary requirements strictly.
- Keep it practical for a weekday dinner.
- Every meal must include 3-7 ingredients only.
- Department must be one of: Produce, Bakery, Meat & Fish, Dairy & Eggs, Frozen, Tins & Dry Goods, Spices & Sauces, Snacks, Drinks, Household, Other
- Unit should be short plain text such as kg, g, pcs, tins, jar, bottle, pack, head, fillets.
- `baseCostForTwo` is an estimated GBP cost for serving 2 people once.
- `estimatedCostForTwo` is the portion of the meal cost attributable to that ingredient for serving 2 people once.
- Use realistic prices, avoid placeholder values, and keep all monetary values to 2 decimal places.
- The sum of `estimatedCostForTwo` across ingredients should be broadly consistent with `baseCostForTwo`.
- `quantityForTwo` must be a positive number.
- `tags` must only use values from: Balanced, High-Protein, Vegetarian, Vegan, Pescatarian, Gluten-Free, Special Treat
- `tags` must include every listed dietary requirement.
- `recipeSteps` must contain 5-6 concrete, meal-specific cooking steps in order.
- Keep each recipe step concise (ideally <= 140 characters).
- Include `nutritionPerServing` for one medium serving (not household total), with calories and grams for protein/carbs/fat.

Return JSON only with this schema:
{
  "name": "",
  "baseCostForTwo": 0,
  "isQuick": true,
  "tags": ["Balanced"],
  "recipeSteps": [
    "",
    "",
    "",
    "",
    ""
  ],
  "nutritionPerServing": {
    "calories": 0,
    "proteinGrams": 0,
    "carbsGrams": 0,
    "fatGrams": 0
  },
  "ingredients": [
    {
      "name": "",
      "department": "",
      "quantityForTwo": 0,
      "unit": "",
      "estimatedCostForTwo": 0
    }
  ]
}
""";
    }

    private void EnsureAiMealPoolHydrated()
    {
        try
        {
            EnsureAiMealPoolHydratedAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to hydrate AislePilot AI meal pool from Firestore.");
        }
    }

    private async Task EnsureAiMealPoolHydratedAsync(CancellationToken cancellationToken = default)
    {
        PruneAiMealPool(DateTime.UtcNow);

        if (_db is null)
        {
            return;
        }

        var shouldRefresh =
            AiMealPool.IsEmpty ||
            !_lastAiMealPoolRefreshUtc.HasValue ||
            DateTime.UtcNow - _lastAiMealPoolRefreshUtc.Value > TimeSpan.FromMinutes(10);

        if (!shouldRefresh)
        {
            return;
        }

        await AiMealPoolRefreshLock.WaitAsync(cancellationToken);
        try
        {
            shouldRefresh =
                AiMealPool.IsEmpty ||
                !_lastAiMealPoolRefreshUtc.HasValue ||
                DateTime.UtcNow - _lastAiMealPoolRefreshUtc.Value > TimeSpan.FromMinutes(10);

            if (!shouldRefresh)
            {
                return;
            }

            var snapshot = await _db.Collection(AiMealsCollection)
                .OrderByDescending(nameof(FirestoreAislePilotMeal.CreatedAtUtc))
                .Limit(150)
                .GetSnapshotAsync(cancellationToken);
            var refreshedAtUtc = DateTime.UtcNow;

            foreach (var doc in snapshot.Documents)
            {
                if (!doc.Exists)
                {
                    continue;
                }

                var firestoreMeal = doc.ConvertTo<FirestoreAislePilotMeal>();
                var mappedMeal = FromFirestoreDocument(firestoreMeal);
                if (mappedMeal is not null)
                {
                    UpsertAiMealPoolEntry(mappedMeal, refreshedAtUtc);
                }
            }

            PruneAiMealPool(refreshedAtUtc);
            _lastAiMealPoolRefreshUtc = refreshedAtUtc;
        }
        finally
        {
            AiMealPoolRefreshLock.Release();
        }
    }

    private async Task<IReadOnlyList<MealTemplate>> PersistAiMealsAsync(
        IReadOnlyList<MealTemplate> meals,
        CancellationToken cancellationToken = default)
    {
        if (meals.Count == 0)
        {
            return [];
        }

        if (_db is null)
        {
            _logger?.LogWarning(
                "AislePilot generated {MealCount} meals but Firestore is unavailable; meals will be memory-only for this runtime.",
                meals.Count);
            return [];
        }

        var persistedMeals = new List<MealTemplate>(meals.Count);
        foreach (var meal in meals)
        {
            try
            {
                var docRef = _db.Collection(AiMealsCollection).Document(ToAiMealDocumentId(meal.Name));
                await docRef.SetAsync(ToFirestoreDocument(meal), cancellationToken: cancellationToken);
                persistedMeals.Add(meal);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Unable to persist AislePilot AI meal '{MealName}'.", meal.Name);
            }
        }

        if (persistedMeals.Count < meals.Count)
        {
            _logger?.LogWarning(
                "AislePilot persisted {PersistedCount} of {TotalCount} AI meals to Firestore.",
                persistedMeals.Count,
                meals.Count);
        }

        return persistedMeals;
    }

    private static string BuildAiMealSwapPrompt(
        AislePilotRequestModel request,
        PlanContext context,
        string currentMealName,
        IReadOnlyList<string> excludedMealNames,
        int dayMultiplier,
        string mealType,
        int mealsPerDay)
    {
        var strictModes = context.DietaryModes
            .Where(mode => !mode.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var strictModeText = strictModes.Length == 0 ? "Balanced" : string.Join(", ", strictModes);
        var excludedText = excludedMealNames.Count == 0 ? "none" : string.Join(", ", excludedMealNames);
        var dislikesText = string.IsNullOrWhiteSpace(context.DislikesOrAllergens) ? "none" : context.DislikesOrAllergens;
        var safeMealsPerDay = NormalizeMealsPerDay(mealsPerDay);
        var targetMealBudget = decimal.Round(
            (request.WeeklyBudget / (7m * safeMealsPerDay)) * Math.Max(1, dayMultiplier),
            2,
            MidpointRounding.AwayFromZero);

        return $$"""
Generate one replacement {{mealType}} for a UK grocery-planning app.

Planner inputs:
- Replace this meal: {{currentMealName}}
- Meal slot to fill: {{mealType}}
- Supermarket: {{context.Supermarket}}
- Target meal budget: {{targetMealBudget.ToString("0.##", CultureInfo.InvariantCulture)}} GBP
- Household size: {{request.HouseholdSize}}
- Portion size: {{context.PortionSize}}
- Prefer quick meals: {{(request.PreferQuickMeals ? "yes" : "no")}}
- Dietary requirements: {{strictModeText}}
- Dislikes or allergens: {{dislikesText}}
- Avoid these other meals already in the week: {{excludedText}}

Rules:
- Return exactly one {{mealType}} object.
- It must be different from the current meal and different from the excluded meals.
- Use UK English.
- Keep it realistic for a UK supermarket shop.
- Use typical UK non-promo shelf prices (no loyalty-only offers, markdowns, or extreme bulk discounts).
- Respect dietary requirements and dislikes/allergens strictly.
- If quick meals are preferred, target 30 minutes or less.
- Every meal must include 3-7 ingredients only.
- Department must be one of: Produce, Bakery, Meat & Fish, Dairy & Eggs, Frozen, Tins & Dry Goods, Spices & Sauces, Snacks, Drinks, Household, Other
- Unit should be short plain text such as kg, g, pcs, tins, jar, bottle, pack, head, fillets.
- `baseCostForTwo` is an estimated GBP cost for serving 2 people once.
- `estimatedCostForTwo` is the portion of the meal cost attributable to that ingredient for serving 2 people once.
- Use realistic prices, avoid placeholder values, and keep all monetary values to 2 decimal places.
- The sum of `estimatedCostForTwo` across ingredients should be broadly consistent with `baseCostForTwo`.
- `quantityForTwo` must be a positive number.
- `tags` must only use values from: Balanced, High-Protein, Vegetarian, Vegan, Pescatarian, Gluten-Free, Special Treat
- `recipeSteps` must contain 5-8 concrete, meal-specific cooking steps in order.
- Include `nutritionPerServing` for one medium serving (not household total), with calories and grams for protein/carbs/fat.

Return JSON only with this schema:
{
  "name": "",
  "baseCostForTwo": 0,
  "isQuick": true,
  "tags": ["Balanced"],
  "recipeSteps": [
    "",
    "",
    "",
    "",
    ""
  ],
  "nutritionPerServing": {
    "calories": 0,
    "proteinGrams": 0,
    "carbsGrams": 0,
    "fatGrams": 0
  },
  "ingredients": [
    {
      "name": "",
      "department": "",
      "quantityForTwo": 0,
      "unit": "",
      "estimatedCostForTwo": 0
    }
  ]
}
""";
    }

    private static List<MealTemplate>? BuildSelectedMealsFromCurrentPlanNames(
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

    private static IReadOnlyList<decimal> BuildBudgetRebalanceTargets(
        decimal originalBudget,
        decimal baselineEstimatedTotal,
        int maxTargets)
    {
        if (maxTargets <= 0 || originalBudget <= 15m)
        {
            return [];
        }

        var overspend = Math.Max(0m, baselineEstimatedTotal - originalBudget);
        var rawTargets = new[]
        {
            originalBudget - Math.Max(overspend + 1m, originalBudget * 0.08m),
            originalBudget * 0.92m,
            originalBudget * 0.88m,
            originalBudget * 0.84m,
            originalBudget * 0.80m
        };

        var maxTargetBudget = decimal.Round(Math.Max(15m, originalBudget - 1m), 2, MidpointRounding.AwayFromZero);
        var dedupedTargets = new List<decimal>(rawTargets.Length);
        foreach (var rawTarget in rawTargets)
        {
            var normalized = decimal.Round(rawTarget, 2, MidpointRounding.AwayFromZero);
            if (normalized < 15m)
            {
                normalized = 15m;
            }

            if (normalized > maxTargetBudget)
            {
                normalized = maxTargetBudget;
            }

            if (normalized >= originalBudget)
            {
                continue;
            }

            if (!dedupedTargets.Contains(normalized))
            {
                dedupedTargets.Add(normalized);
            }
        }

        return dedupedTargets
            .Take(maxTargets)
            .ToList();
    }

    private AislePilotPlanResultViewModel? TryBuildTargetedLowerCostPlan(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> baselineMeals,
        int cookDays)
    {
        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        if (baselineMeals.Count != totalMealCount)
        {
            return null;
        }

        EnsureAiMealPoolHydrated();
        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);
        var compatiblePool = pooledMeals
            .Concat(templateMeals)
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
            .ToList();
        if (compatiblePool.Count == 0)
        {
            return null;
        }

        var selectedMeals = baselineMeals.ToList();
        var planDays = NormalizePlanDays(request.PlanDays);
        var leftoverDays = Math.Max(0, planDays - cookDays);
        var requestedLeftoverSourceDays = ParseRequestedLeftoverSourceDays(
            request.LeftoverCookDayIndexesCsv,
            cookDays,
            leftoverDays,
            planDays);
        var dayMultipliers = BuildMealPortionMultipliers(
            cookDays,
            leftoverDays,
            requestedLeftoverSourceDays,
            planDays);
        var mealMultipliers = BuildPerMealPortionMultipliers(dayMultipliers, mealsPerDay);

        var currentTotal = CalculatePlanCost(selectedMeals, context.HouseholdFactor, mealMultipliers);
        var maxIterations = Math.Max(4, totalMealCount * 2);
        var hasChanges = false;

        for (var iteration = 0; iteration < maxIterations && currentTotal > request.WeeklyBudget; iteration++)
        {
            var orderedDayIndexes = Enumerable.Range(0, totalMealCount)
                .OrderByDescending(index => CalculateScaledMealCost(
                    selectedMeals[index],
                    context.HouseholdFactor,
                    mealMultipliers[index]))
                .ToList();

            var swappedThisIteration = false;
            foreach (var dayIndex in orderedDayIndexes)
            {
                var replacement = SelectLowerCostSwapCandidateForDay(
                    compatiblePool,
                    selectedMeals,
                    dayIndex,
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex],
                    request.PreferQuickMeals,
                    IsHighProteinPreferred(context.DietaryModes),
                    mealTypeSlots);
                if (replacement is null)
                {
                    continue;
                }

                var currentMealCost = CalculateScaledMealCost(
                    selectedMeals[dayIndex],
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex]);
                var replacementMealCost = CalculateScaledMealCost(
                    replacement,
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex]);

                if (replacementMealCost >= currentMealCost)
                {
                    continue;
                }

                selectedMeals[dayIndex] = replacement;
                currentTotal = decimal.Round(
                    currentTotal - currentMealCost + replacementMealCost,
                    2,
                    MidpointRounding.AwayFromZero);
                hasChanges = true;
                swappedThisIteration = true;
                break;
            }

            if (!swappedThisIteration)
            {
                break;
            }
        }

        if (!hasChanges)
        {
            return null;
        }

        if (request.IncludeSpecialTreatMeal && !HasSpecialTreatDinner(selectedMeals, mealTypeSlots))
        {
            return null;
        }

        AddMealsToAiPool(selectedMeals);
        return BuildPlanFromMeals(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: pooledMeals.Count > 0,
            planSourceLabel: "Budget trim swaps");
    }

    private async Task<AislePilotPlanResultViewModel?> TryBuildTargetedLowerCostPlanAsync(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<MealTemplate> baselineMeals,
        int cookDays,
        CancellationToken cancellationToken = default)
    {
        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        if (baselineMeals.Count != totalMealCount)
        {
            return null;
        }

        await EnsureAiMealPoolHydratedAsync(cancellationToken);
        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);
        var compatiblePool = pooledMeals
            .Concat(templateMeals)
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
            .ToList();
        if (compatiblePool.Count == 0)
        {
            return null;
        }

        var selectedMeals = baselineMeals.ToList();
        var planDays = NormalizePlanDays(request.PlanDays);
        var leftoverDays = Math.Max(0, planDays - cookDays);
        var requestedLeftoverSourceDays = ParseRequestedLeftoverSourceDays(
            request.LeftoverCookDayIndexesCsv,
            cookDays,
            leftoverDays,
            planDays);
        var dayMultipliers = BuildMealPortionMultipliers(
            cookDays,
            leftoverDays,
            requestedLeftoverSourceDays,
            planDays);
        var mealMultipliers = BuildPerMealPortionMultipliers(dayMultipliers, mealsPerDay);

        var currentTotal = CalculatePlanCost(selectedMeals, context.HouseholdFactor, mealMultipliers);
        var maxIterations = Math.Max(4, totalMealCount * 2);
        var hasChanges = false;

        for (var iteration = 0; iteration < maxIterations && currentTotal > request.WeeklyBudget; iteration++)
        {
            var orderedDayIndexes = Enumerable.Range(0, totalMealCount)
                .OrderByDescending(index => CalculateScaledMealCost(
                    selectedMeals[index],
                    context.HouseholdFactor,
                    mealMultipliers[index]))
                .ToList();

            var swappedThisIteration = false;
            foreach (var dayIndex in orderedDayIndexes)
            {
                var replacement = SelectLowerCostSwapCandidateForDay(
                    compatiblePool,
                    selectedMeals,
                    dayIndex,
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex],
                    request.PreferQuickMeals,
                    IsHighProteinPreferred(context.DietaryModes),
                    mealTypeSlots);
                if (replacement is null)
                {
                    continue;
                }

                var currentMealCost = CalculateScaledMealCost(
                    selectedMeals[dayIndex],
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex]);
                var replacementMealCost = CalculateScaledMealCost(
                    replacement,
                    context.HouseholdFactor,
                    mealMultipliers[dayIndex]);

                if (replacementMealCost >= currentMealCost)
                {
                    continue;
                }

                selectedMeals[dayIndex] = replacement;
                currentTotal = decimal.Round(
                    currentTotal - currentMealCost + replacementMealCost,
                    2,
                    MidpointRounding.AwayFromZero);
                hasChanges = true;
                swappedThisIteration = true;
                break;
            }

            if (!swappedThisIteration)
            {
                break;
            }
        }

        if (!hasChanges)
        {
            return null;
        }

        if (request.IncludeSpecialTreatMeal && !HasSpecialTreatDinner(selectedMeals, mealTypeSlots))
        {
            return null;
        }

        AddMealsToAiPool(selectedMeals);
        return await BuildPlanFromMealsAsync(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: pooledMeals.Count > 0,
            planSourceLabel: "Budget trim swaps",
            cancellationToken: cancellationToken);
    }

    private static MealTemplate? SelectLowerCostSwapCandidateForDay(
        IReadOnlyList<MealTemplate> compatiblePool,
        IReadOnlyList<MealTemplate> selectedMeals,
        int dayIndex,
        decimal householdFactor,
        int dayMultiplier,
        bool preferQuickMeals,
        bool preferHighProtein,
        IReadOnlyList<string> mealTypeSlots)
    {
        if (compatiblePool.Count == 0 || dayIndex < 0 || dayIndex >= selectedMeals.Count)
        {
            return null;
        }

        var currentMeal = selectedMeals[dayIndex];
        var currentMealCost = CalculateScaledMealCost(currentMeal, householdFactor, dayMultiplier);
        var usedNames = selectedMeals
            .Where((_, index) => index != dayIndex)
            .Select(meal => meal.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slotMealType = mealTypeSlots[dayIndex % mealTypeSlots.Count];
        var normalizedPool = compatiblePool
            .Select(meal => EnsureMealTypeSuitability(meal))
            .ToList();
        var slotCompatiblePool = normalizedPool
            .Where(meal => SupportsMealType(meal, slotMealType))
            .ToList();
        if (slotCompatiblePool.Count == 0)
        {
            return null;
        }

        return slotCompatiblePool
            .Where(meal =>
                !meal.Name.Equals(currentMeal.Name, StringComparison.OrdinalIgnoreCase) &&
                !usedNames.Contains(meal.Name))
            .Select(meal => new
            {
                Meal = meal,
                Cost = CalculateScaledMealCost(meal, householdFactor, dayMultiplier)
            })
            .Where(x => x.Cost < currentMealCost)
            .OrderBy(x => x.Cost)
            .ThenBy(x =>
                preferHighProtein &&
                !x.Meal.Tags.Contains("High-Protein", StringComparer.OrdinalIgnoreCase)
                    ? 1
                    : 0)
            .ThenBy(x => preferQuickMeals && !x.Meal.IsQuick ? 1 : 0)
            .ThenBy(x => x.Meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Meal)
            .FirstOrDefault();
    }

    private static decimal CalculatePlanCost(
        IReadOnlyList<MealTemplate> meals,
        decimal householdFactor,
        IReadOnlyList<int> dayMultipliers)
    {
        if (meals.Count == 0)
        {
            return 0m;
        }

        var normalizedCount = Math.Min(meals.Count, dayMultipliers.Count);
        var total = 0m;
        for (var i = 0; i < normalizedCount; i++)
        {
            total += CalculateScaledMealCost(meals[i], householdFactor, dayMultipliers[i]);
        }

        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal CalculateScaledMealCost(
        MealTemplate meal,
        decimal householdFactor,
        int dayMultiplier)
    {
        var normalizedMultiplier = Math.Max(1, dayMultiplier);
        return decimal.Round(
            meal.BaseCostForTwo * householdFactor * normalizedMultiplier,
            4,
            MidpointRounding.AwayFromZero);
    }

    private AislePilotPlanResultViewModel BuildLowestCostRebalancePlan(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays)
    {
        EnsureAiMealPoolHydrated();

        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);

        var combinedSource = pooledMeals
            .Concat(templateMeals)
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
            .ToList();
        if (combinedSource.Count == 0)
        {
            throw new InvalidOperationException(
                "No meals match the selected dietary modes and dislikes/allergens.");
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        var selectedMeals = SelectLowestCostMeals(
            combinedSource,
            context.HouseholdFactor,
            request.PreferQuickMeals,
            IsHighProteinPreferred(context.DietaryModes),
            totalMealCount,
            mealTypeSlots,
            request.IncludeSpecialTreatMeal,
            request.SelectedSpecialTreatCookDayIndex,
            savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
            enableSavedMealRepeats: request.EnableSavedMealRepeats,
            savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);

        return BuildPlanFromMeals(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: pooledMeals.Count > 0,
            planSourceLabel: "Budget floor");
    }

    private async Task<AislePilotPlanResultViewModel> BuildLowestCostRebalancePlanAsync(
        AislePilotRequestModel request,
        PlanContext context,
        int cookDays,
        CancellationToken cancellationToken = default)
    {
        await EnsureAiMealPoolHydratedAsync(cancellationToken);

        var pooledMeals = GetCompatibleAiPoolMeals(context.DietaryModes, context.DislikesOrAllergens);
        var templateMeals = FilterMeals(context.DietaryModes, context.DislikesOrAllergens, MealTemplates);

        var combinedSource = pooledMeals
            .Concat(templateMeals)
            .GroupBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(meal => meal.BaseCostForTwo).First())
            .ToList();
        if (combinedSource.Count == 0)
        {
            throw new InvalidOperationException(
                "No meals match the selected dietary modes and dislikes/allergens.");
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        var mealsPerDay = mealTypeSlots.Count;
        var totalMealCount = NormalizeRequestedMealCount(cookDays * mealsPerDay);
        var selectedMeals = SelectLowestCostMeals(
            combinedSource,
            context.HouseholdFactor,
            request.PreferQuickMeals,
            IsHighProteinPreferred(context.DietaryModes),
            totalMealCount,
            mealTypeSlots,
            request.IncludeSpecialTreatMeal,
            request.SelectedSpecialTreatCookDayIndex,
            savedEnjoyedMealNames: ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
            enableSavedMealRepeats: request.EnableSavedMealRepeats,
            savedMealRepeatRatePercent: request.SavedMealRepeatRatePercent);

        return await BuildPlanFromMealsAsync(
            request,
            context,
            selectedMeals,
            cookDays,
            usedAiGeneratedMeals: pooledMeals.Count > 0,
            planSourceLabel: "Budget floor",
            cancellationToken: cancellationToken);
    }

    private static IReadOnlyList<MealTemplate> SelectLowestCostMeals(
        IReadOnlyList<MealTemplate> mealSource,
        decimal householdFactor,
        bool preferQuickMeals,
        bool preferHighProtein,
        int requestedMealCount,
        IReadOnlyList<string> mealTypeSlots,
        bool includeSpecialTreatMeal,
        int? selectedSpecialTreatCookDayIndex,
        IReadOnlySet<string>? savedEnjoyedMealNames = null,
        bool enableSavedMealRepeats = false,
        int savedMealRepeatRatePercent = DefaultSavedMealRepeatRatePercent)
    {
        if (mealSource.Count == 0)
        {
            throw new InvalidOperationException(
                "No meals match the selected dietary modes and dislikes/allergens.");
        }

        var normalizedMealCount = NormalizeRequestedMealCount(requestedMealCount);
        var orderedCandidates = mealSource
            .Select(meal => EnsureMealTypeSuitability(meal))
            .OrderBy(meal => decimal.Round(meal.BaseCostForTwo * householdFactor, 4, MidpointRounding.AwayFromZero))
            .ThenBy(meal =>
                preferHighProtein &&
                !meal.Tags.Contains("High-Protein", StringComparer.OrdinalIgnoreCase)
                    ? 1
                    : 0)
            .ThenBy(meal => preferQuickMeals && !meal.IsQuick ? 1 : 0)
            .ThenBy(meal => meal.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selected = new List<MealTemplate>(normalizedMealCount);
        var resolvedMealTypeSlots = NormalizeMealTypeSlots(mealTypeSlots, fallbackMealsPerDay: 1);
        var normalizedSavedMealNames = savedEnjoyedMealNames is null || savedEnjoyedMealNames.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(savedEnjoyedMealNames, StringComparer.OrdinalIgnoreCase);
        var normalizedSavedMealRepeatRatePercent = Math.Clamp(savedMealRepeatRatePercent, 10, 100);
        var rotationSeed = Math.Abs((long)normalizedMealCount + 211L);
        for (var i = 0; i < normalizedMealCount; i++)
        {
            var slotMealType = resolvedMealTypeSlots[i % resolvedMealTypeSlots.Count];
            var slotCompatibleCandidates = orderedCandidates
                .Where(meal => SupportsMealType(meal, slotMealType))
                .ToList();
            if (slotCompatibleCandidates.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No {slotMealType.ToLowerInvariant()} meals match the selected dietary modes and dislikes/allergens.");
            }

            var shouldPreferSavedMealsForSlot =
                enableSavedMealRepeats &&
                normalizedSavedMealNames.Count > 0 &&
                slotCompatibleCandidates.Any(meal => normalizedSavedMealNames.Contains(meal.Name)) &&
                ShouldPreferSavedMealForSlot(
                    rotationSeed,
                    i,
                    normalizedSavedMealRepeatRatePercent);
            var candidate = SelectCandidateForSlot(
                slotCompatibleCandidates,
                selected,
                resolvedMealTypeSlots,
                i,
                normalizedMealCount,
                preferredMealNames: normalizedSavedMealNames,
                shouldPreferPreferredMeals: shouldPreferSavedMealsForSlot);
            selected.Add(candidate);
        }

        if (includeSpecialTreatMeal)
        {
            var treatApplied = TryApplySpecialTreatMeal(
                selected,
                orderedCandidates,
                resolvedMealTypeSlots,
                householdFactor,
                selectedSpecialTreatCookDayIndex);
            if (!treatApplied || !HasSpecialTreatDinner(selected, resolvedMealTypeSlots))
            {
                throw new InvalidOperationException(
                    "No indulgent special treat dinner was available for the current settings.");
            }
        }

        return selected;
    }

    private AislePilotPlanResultViewModel ApplyBudgetRebalanceStatus(
        AislePilotPlanResultViewModel result,
        AislePilotPlanResultViewModel baselinePlan,
        IReadOnlyList<string> baselineMealNames)
    {
        var ukCulture = CultureInfo.GetCultureInfo("en-GB");
        var costDrop = decimal.Round(
            baselinePlan.EstimatedTotalCost - result.EstimatedTotalCost,
            2,
            MidpointRounding.AwayFromZero);
        var hasMealChanges = !_planComparisonService.HasSameMealSequence(result, baselineMealNames);
        var changedMealCount = _planComparisonService.CountChangedMealDays(result, baselineMealNames);
        var usedTargetedTrim = result.PlanSourceLabel.Contains("Budget trim swaps", StringComparison.OrdinalIgnoreCase);

        result.BudgetRebalanceAttempted = true;
        result.BudgetRebalanceReducedCost = costDrop > 0m;

        if (!result.IsOverBudget)
        {
            result.BudgetRebalanceStatusMessage = costDrop > 0m && usedTargetedTrim
                ? $"Swapped {changedMealCount} higher-cost meal(s) for lower-cost options. Estimated spend reduced by {costDrop.ToString("C", ukCulture)}."
                : costDrop > 0m
                    ? $"Lower-cost mix found. Estimated spend reduced by {costDrop.ToString("C", ukCulture)}."
                : "Plan already sits within your budget.";
            return result;
        }

        if (costDrop > 0m)
        {
            result.BudgetRebalanceStatusMessage = usedTargetedTrim
                ? $"Swapped {changedMealCount} higher-cost meal(s) for lower-cost options and reduced spend by {costDrop.ToString("C", ukCulture)}, but this plan is still {Math.Abs(result.BudgetDelta).ToString("C", ukCulture)} over budget."
                : hasMealChanges
                    ? $"Lowest-cost compatible mix found right now. Estimated spend reduced by {costDrop.ToString("C", ukCulture)}, but this plan is still {Math.Abs(result.BudgetDelta).ToString("C", ukCulture)} over budget."
                : $"Estimated spend reduced by {costDrop.ToString("C", ukCulture)}, but this plan is still {Math.Abs(result.BudgetDelta).ToString("C", ukCulture)} over budget.";
            return result;
        }

        result.BudgetRebalanceStatusMessage =
            "Sorry, we do not currently have compatible recipes that come in cheaper than this right now.";
        return result;
    }

    private static AislePilotPlanResultViewModel RebasePlanToOriginalBudget(
        AislePilotPlanResultViewModel plan,
        decimal originalBudget)
    {
        var budgetDelta = decimal.Round(originalBudget - plan.EstimatedTotalCost, 2, MidpointRounding.AwayFromZero);
        var isOverBudget = budgetDelta < 0;
        var sourceLabel = string.IsNullOrWhiteSpace(plan.PlanSourceLabel)
            ? "Budget rebalance"
            : $"Budget rebalance ({plan.PlanSourceLabel})";

        plan.WeeklyBudget = originalBudget;
        plan.BudgetDelta = budgetDelta;
        plan.IsOverBudget = isOverBudget;
        plan.BudgetTips = BuildBudgetTips(isOverBudget, budgetDelta, plan.LeftoverDays);
        plan.PlanSourceLabel = sourceLabel;
        return plan;
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

    private async Task<AislePilotPlanResultViewModel> BuildPlanFromMealsAsync(
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
