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
        return _budgetRebalancePipeline.BuildPlanWithBudgetRebalanceAsync(this, request, maxAttempts, currentPlanMealNames)
            .GetAwaiter()
            .GetResult();
    }

    public Task<AislePilotPlanResultViewModel> BuildPlanWithBudgetRebalanceAsync(
        AislePilotRequestModel request,
        int maxAttempts = 4,
        IReadOnlyList<string>? currentPlanMealNames = null,
        CancellationToken cancellationToken = default)
    {
        return _budgetRebalancePipeline.BuildPlanWithBudgetRebalanceAsync(
            this,
            request,
            maxAttempts,
            currentPlanMealNames,
            cancellationToken);
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
