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

public sealed partial class AislePilotService : IAislePilotService
{
    public IReadOnlyList<string> GetSupportedSupermarkets()
    {
        return SupportedSupermarkets;
    }

    public IReadOnlyList<string> GetSupportedPortionSizes()
    {
        return SupportedPortionSizes;
    }

    public IReadOnlyList<string> GetSupportedDietaryModes()
    {
        return SupportedDietaryModes;
    }

    public bool CanGenerateMealImages()
    {
        return _enableAiGeneration &&
               _enableAiImageGeneration &&
               _httpClient is not null &&
               !string.IsNullOrWhiteSpace(_apiKey) &&
               _webHostEnvironment is not null &&
               !string.IsNullOrWhiteSpace(_webHostEnvironment.WebRootPath);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetMealImageUrlsAsync(
        IReadOnlyList<string> mealNames,
        CancellationToken cancellationToken = default)
    {
        if (mealNames.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var normalizedMealNames = mealNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
        if (normalizedMealNames.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        await EnsureMealImagePoolHydratedAsync(normalizedMealNames, cancellationToken);
        await EnsureDessertAddOnPoolHydratedAsync(cancellationToken);

        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mealName in normalizedMealNames)
        {
            if (TryGetCachedMealImageUrl(mealName, out var cachedUrl))
            {
                resolved[mealName] = cachedUrl;
                continue;
            }

            if (TryGetBundledMealImageUrl(mealName, out var bundledUrl))
            {
                resolved[mealName] = bundledUrl;
                continue;
            }

            var template = AiMealPool.TryGetValue(mealName, out var aiMeal)
                ? aiMeal
                : MealTemplates.FirstOrDefault(template =>
                    template.Name.Equals(mealName, StringComparison.OrdinalIgnoreCase));
            template ??= TryBuildDessertAddOnImageMealTemplate(mealName);
            if (template is not null)
            {
                QueueMealImageGeneration(template);
            }

            resolved[mealName] = GetFallbackMealImageUrl();
        }

        return resolved;
    }

    private static MealTemplate? TryBuildDessertAddOnImageMealTemplate(string mealName)
    {
        if (string.IsNullOrWhiteSpace(mealName))
        {
            return null;
        }

        var dessertTemplate = GetAvailableDessertAddOnTemplatesSnapshot().FirstOrDefault(template =>
            template.Name.Equals(mealName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (dessertTemplate is null)
        {
            return null;
        }

        var estimatedBaseCostForTwo = decimal.Round(
            dessertTemplate.Ingredients.Sum(ingredient => Math.Max(0m, ingredient.EstimatedCostForTwo)),
            2,
            MidpointRounding.AwayFromZero);
        if (estimatedBaseCostForTwo <= 0m)
        {
            estimatedBaseCostForTwo = 4.5m;
        }

        return new MealTemplate(
            dessertTemplate.Name,
            estimatedBaseCostForTwo,
            IsQuick: false,
            ["Balanced", "Special Treat"],
            dessertTemplate.Ingredients.ToList());
    }

    private static IReadOnlyList<DessertAddOnTemplate> GetAvailableDessertAddOnTemplatesSnapshot()
    {
        var dedupedByName = new Dictionary<string, DessertAddOnTemplate>(StringComparer.OrdinalIgnoreCase);
        var orderedTemplates = new List<DessertAddOnTemplate>();

        void AddTemplateIfUnique(DessertAddOnTemplate template)
        {
            if (string.IsNullOrWhiteSpace(template.Name) || dedupedByName.ContainsKey(template.Name))
            {
                return;
            }

            dedupedByName[template.Name] = template;
            orderedTemplates.Add(template);
        }

        foreach (var builtInTemplate in DessertAddOnTemplates)
        {
            AddTemplateIfUnique(builtInTemplate);
        }

        foreach (var persistedTemplate in DessertAddOnPool.Values
                     .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase))
        {
            AddTemplateIfUnique(persistedTemplate);
        }

        return orderedTemplates;
    }

    private void EnsureDessertAddOnPoolHydrated()
    {
        try
        {
            EnsureDessertAddOnPoolHydratedAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Unable to hydrate AislePilot dessert add-on pool from Firestore.");
        }
    }

    private async Task EnsureDessertAddOnPoolHydratedAsync(CancellationToken cancellationToken = default)
    {
        if (_db is null)
        {
            return;
        }

        var shouldRefresh =
            !_lastDessertAddOnPoolRefreshUtc.HasValue ||
            DateTime.UtcNow - _lastDessertAddOnPoolRefreshUtc.Value > DessertAddOnPoolRefreshInterval;
        if (!shouldRefresh)
        {
            return;
        }

        await DessertAddOnPoolRefreshLock.WaitAsync(cancellationToken);
        try
        {
            shouldRefresh =
                !_lastDessertAddOnPoolRefreshUtc.HasValue ||
                DateTime.UtcNow - _lastDessertAddOnPoolRefreshUtc.Value > DessertAddOnPoolRefreshInterval;
            if (!shouldRefresh)
            {
                return;
            }

            var snapshot = await _db.Collection(DessertAddOnsCollection)
                .OrderByDescending(nameof(FirestoreAislePilotDessertAddOn.UpdatedAtUtc))
                .Limit(160)
                .GetSnapshotAsync(cancellationToken);
            DessertAddOnPool.Clear();

            foreach (var doc in snapshot.Documents)
            {
                if (!doc.Exists)
                {
                    continue;
                }

                FirestoreAislePilotDessertAddOn? mappedDoc;
                try
                {
                    mappedDoc = doc.ConvertTo<FirestoreAislePilotDessertAddOn>();
                }
                catch
                {
                    continue;
                }

                var mappedTemplate = FromFirestoreDessertAddOnDocument(mappedDoc);
                if (mappedTemplate is not null)
                {
                    DessertAddOnPool[mappedTemplate.Name] = mappedTemplate;
                }
            }

            _lastDessertAddOnPoolRefreshUtc = DateTime.UtcNow;
        }
        finally
        {
            DessertAddOnPoolRefreshLock.Release();
        }
    }

    private async Task PersistDessertAddOnTemplateAsync(
        DessertAddOnTemplate dessertAddOnTemplate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dessertAddOnTemplate.Name))
        {
            return;
        }

        DessertAddOnPool[dessertAddOnTemplate.Name] = dessertAddOnTemplate;
        if (_db is null)
        {
            return;
        }

        try
        {
            var docRef = _db.Collection(DessertAddOnsCollection).Document(ToAiMealDocumentId(dessertAddOnTemplate.Name));
            await docRef.SetAsync(
                ToFirestoreDessertAddOnDocument(dessertAddOnTemplate),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Unable to persist AislePilot dessert add-on '{DessertName}'.",
                dessertAddOnTemplate.Name);
        }
    }

    public async Task<AislePilotWarmupResult> WarmupAiMealPoolAsync(
        int minPerSingleMode = 8,
        int minPerKeyPair = 6,
        int maxMealsToGenerate = 2,
        CancellationToken cancellationToken = default)
    {
        var normalizedMinSingleMode = Math.Clamp(minPerSingleMode, 0, 30);
        var normalizedMinKeyPair = Math.Clamp(minPerKeyPair, 0, 30);
        var normalizedMaxMeals = Math.Clamp(maxMealsToGenerate, 0, 3);
        var profiles = BuildWarmupProfiles(normalizedMinSingleMode, normalizedMinKeyPair);

        var result = new AislePilotWarmupResult
        {
            MinPerSingleMode = normalizedMinSingleMode,
            MinPerKeyPair = normalizedMinKeyPair,
            MaxMealsToGenerate = normalizedMaxMeals
        };

        await AiMealWarmupLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureAiMealPoolHydratedAsync(cancellationToken);
            result.CoverageBefore = BuildWarmupCoverage(profiles);

            if (normalizedMaxMeals == 0)
            {
                result.CoverageAfter = result.CoverageBefore;
                return result;
            }

            var generatedMealNames = new List<string>(normalizedMaxMeals);
            var maxAttempts = Math.Max(1, normalizedMaxMeals * 4);
            for (var attempt = 0; attempt < maxAttempts && generatedMealNames.Count < normalizedMaxMeals; attempt++)
            {
                var coverageNow = BuildWarmupCoverage(profiles);
                var nextProfile = coverageNow
                    .Where(item => item.Deficit > 0)
                    .OrderByDescending(item => item.Deficit)
                    .ThenBy(item => item.Count)
                    .FirstOrDefault();

                if (nextProfile is null)
                {
                    break;
                }

                var excludedMealNames = GetCompatibleAiPoolMeals(nextProfile.Modes, string.Empty)
                    .Select(meal => meal.Name)
                    .Concat(generatedMealNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(40)
                    .ToList();

                var generatedMeal = await TryGenerateWarmupMealWithAiAsync(
                    nextProfile.Modes,
                    excludedMealNames,
                    cancellationToken);
                if (generatedMeal is null || generatedMealNames.Contains(generatedMeal.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var persistedMeals = await PersistAiMealsAsync([generatedMeal], cancellationToken);
                if (persistedMeals.Count > 0)
                {
                    AddMealsToAiPool(persistedMeals);
                }
                else
                {
                    AddMealsToAiPool([generatedMeal]);
                    _logger?.LogWarning(
                        "AislePilot warm-up generated meal '{MealName}' but persistence failed. Keeping it in memory for this runtime.",
                        generatedMeal.Name);
                }

                generatedMealNames.Add(generatedMeal.Name);
            }

            result.GeneratedMealNames = generatedMealNames;
            result.GeneratedCount = generatedMealNames.Count;
            result.CoverageAfter = BuildWarmupCoverage(profiles);
            return result;
        }
        finally
        {
            AiMealWarmupLock.Release();
        }
    }

    public bool HasCompatibleMeals(AislePilotRequestModel request)
    {
        var dietaryModes = NormalizeDietaryModes(request.DietaryModes);
        var dislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty;
        var mealTypeSlots = BuildMealTypeSlots(request);
        var templateCandidates = FilterMeals(dietaryModes, dislikesOrAllergens)
            .Select(meal => EnsureMealTypeSuitability(meal))
            .ToList();
        if (HasSlotCoverageForMealTypes(templateCandidates, mealTypeSlots))
        {
            return true;
        }

        var pooledCandidates = FilterMeals(dietaryModes, dislikesOrAllergens, AiMealPool.Values.ToList())
            .Select(meal => EnsureMealTypeSuitability(meal))
            .ToList();
        if (HasSlotCoverageForMealTypes(pooledCandidates, mealTypeSlots))
        {
            return true;
        }

        if (CanAttemptAiGenerationForPlanRequest())
        {
            _logger?.LogInformation(
                "AislePilot compatibility pre-check found no local slot coverage; allowing AI generation attempt for MealTypes={MealTypes}.",
                string.Join(",", mealTypeSlots));
            return true;
        }

        return false;
    }

    private bool CanAttemptAiGenerationForPlanRequest()
    {
        return _enableAiGeneration &&
               _httpClient is not null &&
               !string.IsNullOrWhiteSpace(_apiKey);
    }

    private static bool HasSlotCoverageForMealTypes(
        IReadOnlyList<MealTemplate> candidates,
        IReadOnlyList<string> mealTypeSlots)
    {
        if (candidates.Count == 0 || mealTypeSlots.Count == 0)
        {
            return false;
        }

        return mealTypeSlots.All(mealType =>
            candidates.Any(meal => SupportsMealType(meal, mealType)));
    }

    public IReadOnlyList<AislePilotPantrySuggestionViewModel> SuggestMealsFromPantry(
        AislePilotRequestModel request,
        int maxResults = 5,
        IReadOnlyList<string>? excludedMealNames = null,
        string? generationNonce = null)
    {
        var dietaryModes = NormalizeDietaryModes(request.DietaryModes);
        var dislikesOrAllergens = request.DislikesOrAllergens ?? string.Empty;
        var normalizedPortionSize = NormalizePortionSize(request.PortionSize);
        var portionSizeFactor = ResolvePortionSizeFactor(normalizedPortionSize);
        var householdFactor = Math.Max(0.5m, request.HouseholdSize / 2m) * portionSizeFactor;
        var normalizedExcludedMealNames = (excludedMealNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var excludedMealNameSet = new HashSet<string>(normalizedExcludedMealNames, StringComparer.OrdinalIgnoreCase);
        var userPantryTokens = _pantryRankingEngine.ParsePantryTokens(request.PantryItems);
        var pantryTokensWithAssumedBasics = _pantryRankingEngine.MergePantryTokensWithAssumedBasics(userPantryTokens);
        var specificPantryTokens = _pantryRankingEngine.ParseSpecificPantryTokens(userPantryTokens);
        if (userPantryTokens.Count == 0)
        {
            return [];
        }

        var cappedResults = Math.Clamp(maxResults, 1, 12);
        var aiGeneratedCandidates = TryGeneratePantryMealsWithAi(
            request,
            dietaryModes,
            dislikesOrAllergens,
            cappedResults,
            normalizedExcludedMealNames,
            generationNonce);
        if (aiGeneratedCandidates.Count > 0)
        {
            AddMealsToAiPool(aiGeneratedCandidates);
        }

        var templateCandidates = FilterMeals(dietaryModes, dislikesOrAllergens);
        EnsureAiMealPoolHydrated();
        var aiPoolCandidates = GetCompatibleAiPoolMeals(dietaryModes, dislikesOrAllergens);
        var candidates = aiGeneratedCandidates
            .Concat(templateCandidates)
            .Concat(aiPoolCandidates)
            .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(candidate => !excludedMealNameSet.Contains(candidate.Name))
            .ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var allCandidates = candidates
            .Select(template =>
            {
                var suggestion = _pantryRankingEngine.BuildPantrySuggestion(
                    template,
                    pantryTokensWithAssumedBasics,
                    householdFactor);
                var userOnlySuggestion = _pantryRankingEngine.BuildPantrySuggestion(
                    template,
                    userPantryTokens,
                    householdFactor);
                var userMatchedTokenCount = _pantryRankingEngine.CountMatchedPantryTokens(template, userPantryTokens);
                var specificMatchedTokenCount = _pantryRankingEngine.CountMatchedPantryTokens(template, specificPantryTokens);
                var score = _pantryRankingEngine.ComputePantrySuggestionScore(
                    suggestion,
                    userOnlySuggestion,
                    userMatchedTokenCount,
                    specificMatchedTokenCount,
                    specificPantryTokens.Count);
                return new PantrySuggestionCandidate(
                    template,
                    suggestion,
                    userOnlySuggestion,
                    userMatchedTokenCount,
                    specificMatchedTokenCount,
                    score);
            })
            .Where(candidate =>
                candidate.Suggestion.MatchPercent > 0 ||
                candidate.UserOnlySuggestion.MatchPercent > 0 ||
                candidate.UserMatchedTokenCount > 0)
            .ToList();

        var eligibleCandidates = specificPantryTokens.Count > 0
            ? allCandidates.Where(candidate => candidate.SpecificMatchedTokenCount > 0).ToList()
            : allCandidates.Where(candidate => candidate.UserMatchedTokenCount > 0).ToList();
        if (eligibleCandidates.Count == 0)
        {
            return [];
        }

        if (request.RequireCorePantryIngredients)
        {
            var strictCoreSuggestions = _pantryRankingEngine.RankPantrySuggestionCandidates(
                    eligibleCandidates
                        .Where(candidate => _pantryRankingEngine.TemplateUsesCoreIngredientsFromUserPantry(
                            candidate.Template,
                            userPantryTokens))
                        .Where(candidate => candidate.Suggestion.MissingCoreIngredientCount == 0)
                        .Where(candidate => candidate.Suggestion.MatchPercent >= PantrySuggestionNearMatchThreshold)
                        .ToList(),
                    cappedResults,
                    allowVariation: true)
                .Take(cappedResults)
                .Select(candidate => (candidate.Template, candidate.Suggestion))
                .ToList();
            return BuildPantrySuggestionCards(
                OrderPantrySuggestionsByMatch(strictCoreSuggestions),
                dietaryModes,
                dislikesOrAllergens,
                householdFactor,
                portionSizeFactor);
        }

        var minimumStrongTokenMatches = userPantryTokens.Count >= 4 ? 2 : 1;
        var strongCandidates = eligibleCandidates
            .Where(candidate => candidate.UserMatchedTokenCount >= minimumStrongTokenMatches)
            .ToList();
        var primaryCandidates = strongCandidates.Count > 0
            ? strongCandidates
            : eligibleCandidates;

        var readyNowCandidates = _pantryRankingEngine.RankPantrySuggestionCandidates(
            primaryCandidates.Where(candidate => candidate.Suggestion.CanCookNow).ToList(),
            cappedResults,
            allowVariation: true);
        var topUpCandidates = _pantryRankingEngine.RankPantrySuggestionCandidates(
            primaryCandidates
                .Where(candidate =>
                    !candidate.Suggestion.CanCookNow &&
                    candidate.Suggestion.MissingCoreIngredientCount <= 2)
                .ToList(),
            cappedResults,
            allowVariation: true);
        var stretchCandidates = _pantryRankingEngine.RankPantrySuggestionCandidates(
            primaryCandidates
                .Where(candidate =>
                    candidate.Suggestion.MissingCoreIngredientCount <= 4 &&
                    candidate.UserOnlySuggestion.MatchedIngredients.Count >= 1)
                .ToList(),
            cappedResults,
            allowVariation: false);

        var selectedSuggestions = new List<(MealTemplate Template, AislePilotPantrySuggestionViewModel Suggestion)>(cappedResults);
        var selectedMealNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddSuggestions(IEnumerable<PantrySuggestionCandidate> source)
        {
            foreach (var candidate in source)
            {
                if (selectedSuggestions.Count >= cappedResults)
                {
                    break;
                }

                if (!selectedMealNames.Add(candidate.Suggestion.MealName))
                {
                    continue;
                }

                selectedSuggestions.Add((candidate.Template, candidate.Suggestion));
            }
        }

        AddSuggestions(readyNowCandidates);
        AddSuggestions(topUpCandidates);
        AddSuggestions(stretchCandidates);
        if (selectedSuggestions.Count == 0)
        {
            return [];
        }

        if (selectedSuggestions.Count < cappedResults)
        {
            var supplementalCandidates = _pantryRankingEngine.RankPantrySuggestionCandidates(
                eligibleCandidates,
                cappedResults,
                allowVariation: true);
            AddSuggestions(supplementalCandidates);
        }

        return BuildPantrySuggestionCards(
            OrderPantrySuggestionsByMatch(selectedSuggestions),
            dietaryModes,
            dislikesOrAllergens,
            householdFactor,
            portionSizeFactor);
    }

    private static IReadOnlyList<(MealTemplate Template, AislePilotPantrySuggestionViewModel Suggestion)> OrderPantrySuggestionsByMatch(
        IReadOnlyList<(MealTemplate Template, AislePilotPantrySuggestionViewModel Suggestion)> suggestions)
    {
        if (suggestions.Count <= 1)
        {
            return suggestions;
        }

        return suggestions
            .OrderByDescending(entry => entry.Suggestion.MatchPercent)
            .ThenBy(entry => entry.Suggestion.MissingIngredientsEstimatedCost)
            .ThenBy(entry => entry.Suggestion.MissingCoreIngredientCount)
            .ThenBy(entry => entry.Template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<MealTemplate> TryGeneratePantryMealsWithAi(
        AislePilotRequestModel request,
        IReadOnlyList<string> dietaryModes,
        string dislikesOrAllergens,
        int maxResults,
        IReadOnlyList<string> excludedMealNames,
        string? generationNonce)
    {
        if (!_enableAiGeneration || _httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return [];
        }

        try
        {
            return TryGeneratePantryMealsWithAiAsync(
                    request,
                    dietaryModes,
                    dislikesOrAllergens,
                    maxResults,
                    excludedMealNames,
                    generationNonce)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AislePilot pantry AI suggestions failed; falling back to catalog ranking.");
            return [];
        }
    }

    private async Task<IReadOnlyList<MealTemplate>> TryGeneratePantryMealsWithAiAsync(
        AislePilotRequestModel request,
        IReadOnlyList<string> dietaryModes,
        string dislikesOrAllergens,
        int maxResults,
        IReadOnlyList<string> excludedMealNames,
        string? generationNonce,
        CancellationToken cancellationToken = default)
    {
        var requestedCount = Math.Clamp(maxResults, 1, 6);
        var prompt = BuildAiPantrySuggestionPrompt(
            request,
            dietaryModes,
            dislikesOrAllergens,
            requestedCount,
            excludedMealNames,
            generationNonce);
        var requestBody = new
        {
            model = _model,
            temperature = 0.85,
            max_tokens = PrimaryAiMealPlanMaxTokens,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "You generate practical UK pantry meal ideas. Prioritise pantry matching, avoid random substitutions, and return valid JSON only."
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
            return [];
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
            var rawJson = payload?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return [];
            }

            var normalizedJson = NormalizeModelJson(rawJson);
            if (!TryParseAiPlanPayloadWithRecovery(normalizedJson, out var aiPayload, out _))
            {
                return [];
            }

            var aiMeals = ValidateAndMapAiMeals(
                aiPayload,
                dietaryModes,
                requestedCount,
                requestedCount,
                mealsPerDay: 1,
                mealTypeSlots: ["Dinner"],
                requireSpecialTreatDinner: false,
                out var validationReason);
            if (aiMeals is null)
            {
                _logger?.LogInformation(
                    "AislePilot pantry AI suggestions failed validation: {ValidationReason}",
                    validationReason ?? "unknown");
                return [];
            }

            return aiMeals;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "AislePilot pantry AI response parsing failed.");
            return [];
        }
    }

    private IReadOnlyList<AislePilotPantrySuggestionViewModel> BuildPantrySuggestionCards(
        IReadOnlyList<(MealTemplate Template, AislePilotPantrySuggestionViewModel Suggestion)> suggestionsWithTemplate,
        IReadOnlyList<string> dietaryModes,
        string dislikesOrAllergens,
        decimal householdFactor,
        decimal portionSizeFactor)
    {
        if (suggestionsWithTemplate.Count == 0)
        {
            return [];
        }

        var templates = suggestionsWithTemplate
            .Select(entry => entry.Template)
            .ToList();
        var dayMultipliers = Enumerable.Repeat(1, templates.Count).ToList();
        var mealMultipliers = BuildPerMealPortionMultipliers(dayMultipliers, 1);
        var mealImageUrls = ResolveMealImageUrls(templates);
        var mealCards = BuildDailyPlans(
            templates,
            mealMultipliers,
            dayMultipliers,
            mealTypeSlots: ["Dinner"],
            ignoredMealSlotIndexes: new HashSet<int>(),
            mealImageUrls,
            householdFactor,
            portionSizeFactor,
            dietaryModes,
            dislikesOrAllergens,
            specialTreatMealSlotIndex: null);
        var mealCardsByName = mealCards.ToDictionary(card => card.MealName, StringComparer.OrdinalIgnoreCase);

        var resolvedSuggestions = new List<AislePilotPantrySuggestionViewModel>(suggestionsWithTemplate.Count);
        foreach (var entry in suggestionsWithTemplate)
        {
            var suggestion = entry.Suggestion;
            if (mealCardsByName.TryGetValue(entry.Template.Name, out var mealCard))
            {
                mealCard.Day = "Dish";
                mealCard.LeftoverDaysCovered = 0;
                suggestion.MealCard = mealCard;
            }

            resolvedSuggestions.Add(suggestion);
        }

        return resolvedSuggestions;
    }

    private static string NormalizeSupermarket(string value)
    {
        var selected = SupportedSupermarkets.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        return selected ?? SupportedSupermarkets[0];
    }

    private static string NormalizePortionSize(string value)
    {
        var selected = SupportedPortionSizes.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));
        return selected ?? "Medium";
    }

    private static decimal ResolvePortionSizeFactor(string portionSize)
    {
        if (portionSize.Equals("Small", StringComparison.OrdinalIgnoreCase))
        {
            return 0.75m;
        }

        if (portionSize.Equals("Large", StringComparison.OrdinalIgnoreCase))
        {
            return 1.15m;
        }

        return 1m;
    }

    private static IReadOnlyList<string> NormalizeDietaryModes(IReadOnlyList<string>? incomingModes)
    {
        var normalized = incomingModes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Where(x => SupportedDietaryModes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];

        if (normalized.Count == 0)
        {
            normalized.Add("Balanced");
        }

        return normalized;
    }

}
