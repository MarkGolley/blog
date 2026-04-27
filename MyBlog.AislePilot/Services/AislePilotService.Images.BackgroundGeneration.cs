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
    internal void QueueSpecialTreatGeneration(
        AislePilotRequestModel request,
        PlanContext context,
        IReadOnlyList<string>? excludedMealNames = null)
    {
        if (!request.IncludeSpecialTreatMeal || !CanAttemptAiGenerationForPlanRequest())
        {
            return;
        }

        var mealTypeSlots = BuildMealTypeSlots(request);
        if (!mealTypeSlots.Any(slot => slot.Equals("Dinner", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var key = BuildSpecialTreatBackgroundKey(request, context);
        if (!SpecialTreatGenerationInFlight.TryAdd(key, 1))
        {
            return;
        }

        var requestSnapshot = CloneRequest(request);
        var excludedNames = (excludedMealNames ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _ = Task.Run(async () =>
        {
            try
            {
                using var generationBudgetCts = new CancellationTokenSource(OpenAiGenerationBudget);
                var generatedTreat = await TryGenerateSpecialTreatMealWithAiAsync(
                    requestSnapshot,
                    context,
                    excludedNames,
                    generationBudgetCts.Token);
                if (generatedTreat is null)
                {
                    return;
                }

                var persistedTreatMeals = await PersistAiMealsAsync([generatedTreat], generationBudgetCts.Token);
                if (persistedTreatMeals.Count > 0)
                {
                    AddMealsToAiPool(persistedTreatMeals);
                }
                else
                {
                    AddMealsToAiPool([generatedTreat]);
                }

                QueueMealImageGeneration(generatedTreat);
                _logger?.LogInformation(
                    "AislePilot generated a deferred special treat meal in the background: {MealName}",
                    generatedTreat.Name);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AislePilot deferred special treat generation failed.");
            }
            finally
            {
                SpecialTreatGenerationInFlight.TryRemove(key, out _);
            }
        });
    }

    private void QueueDessertAddOnRecovery(string? selectedDessertAddOnName)
    {
        var key = string.IsNullOrWhiteSpace(selectedDessertAddOnName)
            ? "__default__"
            : ToAiMealDocumentId(selectedDessertAddOnName.Trim());
        if (!DessertAddOnRecoveryInFlight.TryAdd(key, 1))
        {
            return;
        }

        var selectedDessertNameSnapshot = selectedDessertAddOnName;
        _ = Task.Run(async () =>
        {
            try
            {
                var resolvedTemplate = await ResolveDessertAddOnTemplateAsync(
                    selectedDessertNameSnapshot,
                    CancellationToken.None);
                await PersistDessertAddOnTemplateAsync(resolvedTemplate, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AislePilot deferred dessert add-on recovery failed.");
            }
            finally
            {
                DessertAddOnRecoveryInFlight.TryRemove(key, out _);
            }
        });
    }

    private string BuildSpecialTreatBackgroundKey(AislePilotRequestModel request, PlanContext context)
    {
        var dietaryKey = string.Join(
            "|",
            context.DietaryModes
                .Where(mode => !string.IsNullOrWhiteSpace(mode))
                .Select(mode => mode.Trim())
                .OrderBy(mode => mode, StringComparer.OrdinalIgnoreCase));
        var mealTypeKey = string.Join("|", BuildMealTypeSlots(request));
        var dislikesKey = string.IsNullOrWhiteSpace(context.DislikesOrAllergens)
            ? string.Empty
            : context.DislikesOrAllergens.Trim().ToLowerInvariant();
        return string.Join(
            "|",
            context.Supermarket,
            context.PortionSize,
            request.HouseholdSize.ToString(CultureInfo.InvariantCulture),
            mealTypeKey,
            dietaryKey,
            dislikesKey);
    }

    private void QueueMealImageGeneration(MealTemplate meal)
    {
        if (!CanGenerateMealImages())
        {
            return;
        }

        var key = ToAiMealDocumentId(meal.Name);
        if (!MealImageGenerationInFlight.TryAdd(key, 1))
        {
            return;
        }

        MarkMealImageLookupMiss(meal.Name, DateTime.UtcNow);

        _ = Task.Run(async () =>
        {
            var throttleAcquired = false;
            try
            {
                await MealImageGenerationThrottle.WaitAsync(CancellationToken.None);
                throttleAcquired = true;
                if (TryGetCachedMealImageUrl(meal.Name, out _))
                {
                    return;
                }

                var imageBytes = await TryGenerateMealImageBytesWithAiAsync(meal, CancellationToken.None);
                if (imageBytes is null || imageBytes.Length == 0)
                {
                    return;
                }

                var imageUrl = await SaveMealImageToDiskAsync(meal.Name, imageBytes, CancellationToken.None);
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    return;
                }

                MealImagePool[meal.Name] = imageUrl;
                ClearMealImageLookupMiss(meal.Name);

                if (AiMealPool.TryGetValue(meal.Name, out var existingMeal))
                {
                    UpsertAiMealPoolEntry(existingMeal with { ImageUrl = imageUrl }, DateTime.UtcNow);
                }

                await PersistMealImageAsync(meal.Name, imageUrl, imageBytes, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AislePilot meal image generation failed for '{MealName}'.", meal.Name);
            }
            finally
            {
                if (throttleAcquired)
                {
                    MealImageGenerationThrottle.Release();
                }

                MealImageGenerationInFlight.TryRemove(key, out _);
            }
        });
    }

    private async Task<byte[]?> TryGenerateMealImageBytesWithAiAsync(
        MealTemplate meal,
        CancellationToken cancellationToken)
    {
        if (_httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var requestBody = new
        {
            model = _imageModel,
            prompt = BuildAiMealImagePrompt(meal),
            size = "1024x1024",
            quality = "low",
            n = 1
        };
        var serializedBody = JsonSerializer.Serialize(requestBody);

        for (var attempt = 1; attempt <= OpenAiImageMaxAttempts; attempt++)
        {
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCts.CancelAfter(OpenAiImageRequestTimeout);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, OpenAiImageGenerationsEndpoint)
            {
                Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            try
            {
                using var response = await _httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestCts.Token);
                var responseContent = await response.Content.ReadAsStringAsync(CancellationToken.None);
                if (!response.IsSuccessStatusCode)
                {
                    var errorSample = responseContent.Length <= 220 ? responseContent : responseContent[..220];
                    _logger?.LogWarning(
                        "AislePilot meal image request failed with status {StatusCode}. Attempt={Attempt}/{MaxAttempts}. ResponseSample={ResponseSample}",
                        (int)response.StatusCode,
                        attempt,
                        OpenAiImageMaxAttempts,
                        errorSample);
                    continue;
                }

                var payload = JsonSerializer.Deserialize<OpenAiImageGenerationResponse>(responseContent, JsonOptions);
                var imageData = payload?.Data?.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(imageData?.B64Json))
                {
                    try
                    {
                        return Convert.FromBase64String(imageData.B64Json);
                    }
                    catch (FormatException)
                    {
                        _logger?.LogWarning("AislePilot meal image payload had invalid base64 data for '{MealName}'.", meal.Name);
                    }
                }

                if (!string.IsNullOrWhiteSpace(imageData?.Url))
                {
                    try
                    {
                        using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        downloadCts.CancelAfter(OpenAiImageDownloadTimeout);
                        using var imageResponse = await _httpClient.GetAsync(
                            imageData.Url,
                            HttpCompletionOption.ResponseHeadersRead,
                            downloadCts.Token);
                        if (imageResponse.IsSuccessStatusCode)
                        {
                            return await imageResponse.Content.ReadAsByteArrayAsync(CancellationToken.None);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _logger?.LogInformation(
                            "AislePilot image CDN fetch timed out for '{MealName}'. Attempt={Attempt}/{MaxAttempts}.",
                            meal.Name,
                            attempt,
                            OpenAiImageMaxAttempts);
                    }
                    catch
                    {
                        // Ignore and return null below.
                    }
                }
            }
            catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
            {
                _logger?.LogInformation(
                    "AislePilot meal image request timed out for '{MealName}'. Attempt={Attempt}/{MaxAttempts}.",
                    meal.Name,
                    attempt,
                    OpenAiImageMaxAttempts);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "AislePilot meal image request failed for '{MealName}'.", meal.Name);
            }
        }

        return null;
    }

    private static string BuildAiMealImagePrompt(MealTemplate meal)
    {
        var ingredientNames = meal.Ingredients
            .Select(ingredient => ingredient.Name.Trim().ToLowerInvariant())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        var ingredientText = string.Join(
            ", ",
            ingredientNames.Take(6));
        if (string.IsNullOrWhiteSpace(ingredientText))
        {
            ingredientText = "seasonal ingredients";
        }

        var stapleTokens = new[]
        {
            "rice",
            "pasta",
            "noodle",
            "bread",
            "potato"
        };
        var excludedStapleNames = stapleTokens
            .Where(token => ingredientNames.All(name => !ContainsWholeWord(name, token)))
            .ToArray();
        var excludedStapleRule = excludedStapleNames.Length == 0
            ? string.Empty
            : $"Do not depict {string.Join(", ", excludedStapleNames)} unless included in the ingredient list.";

        return $"""
Create a photorealistic hero image of the finished plated dish: "{meal.Name}".
Style: natural light food photography, 45-degree angle, realistic textures, appetising and modern.
Use this ingredient list as ground truth: {ingredientText}.
Keep ingredient visuals consistent with that list and avoid adding extra staple carbs.
{excludedStapleRule}
Single plated meal only, neutral background, no people, no text, no logos, no watermarks.
""";
    }

}
