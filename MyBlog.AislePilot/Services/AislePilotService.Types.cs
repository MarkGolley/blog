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
    private sealed record WarmupProfile(
        string Name,
        IReadOnlyList<string> Modes);

    private sealed record WarmupProfileWithTarget(
        string Name,
        IReadOnlyList<string> Modes,
        int Target);

    internal sealed record PlanContext(
        string Supermarket,
        IReadOnlyList<string> DietaryModes,
        IReadOnlyList<string> AisleOrder,
        decimal HouseholdFactor,
        string DislikesOrAllergens,
        string PortionSize);

    private sealed class MealNutritionEstimate
    {
        public int CaloriesPerServing { get; set; }
        public decimal ProteinGramsPerServing { get; set; }
        public decimal CarbsGramsPerServing { get; set; }
        public decimal FatGramsPerServing { get; set; }
        public decimal ConfidenceScore { get; set; } = 0.5m;
        public string SourceLabel { get; set; } = "Ingredient estimate";
    }

    private sealed class MutableShoppingItem
    {
        public string Department { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal EstimatedCost { get; set; }
    }

    private sealed record MealTemplate(
        string Name,
        decimal BaseCostForTwo,
        bool IsQuick,
        IReadOnlyList<string> Tags,
        IReadOnlyList<IngredientTemplate> Ingredients)
    {
        public IReadOnlyList<string>? AiRecipeSteps { get; init; }
        public AiMealNutritionEstimate? AiNutritionPerServingMedium { get; init; }
        public string? ImageUrl { get; init; }
        public IReadOnlyList<string>? SuitableMealTypes { get; init; }
    }

    private sealed record IngredientTemplate(
        string Name,
        string Department,
        decimal QuantityForTwo,
        string Unit,
        decimal EstimatedCostForTwo);

    private sealed record DessertAddOnTemplate(
        string Name,
        IReadOnlyList<IngredientTemplate> Ingredients);

    private sealed record IngredientUnitPriceReference(
        string IngredientNameNormalized,
        string UnitNormalized,
        decimal UnitPrice);

    private sealed record NutritionReference(
        decimal CaloriesPer100g,
        decimal ProteinPer100g,
        decimal CarbsPer100g,
        decimal FatPer100g,
        decimal? GramsPerUnit = null);

    private sealed class AiMealNutritionEstimate
    {
        public int CaloriesPerServingMedium { get; set; }
        public decimal ProteinGramsPerServingMedium { get; set; }
        public decimal CarbsGramsPerServingMedium { get; set; }
        public decimal FatGramsPerServingMedium { get; set; }
        public decimal ConfidenceScore { get; set; } = 0.55m;
    }

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        public string? Content { get; set; }
    }

    private sealed class AislePilotAiPlanPayload
    {
        public List<AislePilotAiMealPayload>? Meals { get; set; }
    }

    private sealed class AislePilotAiMealPayload
    {
        public string? Name { get; set; }
        public decimal? BaseCostForTwo { get; set; }
        public bool? IsQuick { get; set; }
        public List<string>? Tags { get; set; }
        public List<AislePilotAiIngredientPayload>? Ingredients { get; set; }
        public List<string>? RecipeSteps { get; set; }
        public AislePilotAiNutritionPayload? NutritionPerServing { get; set; }
        public string? ImageUrl { get; set; }
    }

    private sealed class AislePilotAiNutritionPayload
    {
        public decimal? Calories { get; set; }
        public decimal? ProteinGrams { get; set; }
        public decimal? CarbsGrams { get; set; }
        public decimal? FatGrams { get; set; }
    }

    private sealed class AislePilotAiIngredientPayload
    {
        public string? Name { get; set; }
        public string? Department { get; set; }
        public decimal? QuantityForTwo { get; set; }
        public string? Unit { get; set; }
        public decimal? EstimatedCostForTwo { get; set; }
    }

    private sealed class SupermarketAisleOrderPayload
    {
        public List<string>? AisleOrder { get; set; }
    }

    private sealed class SupermarketLayoutCacheEntry
    {
        public IReadOnlyList<string> AisleOrder { get; init; } = Array.Empty<string>();
        public DateTime UpdatedAtUtc { get; init; }
    }

    [FirestoreData]
    private sealed class FirestoreAislePilotMeal
    {
        [FirestoreProperty]
        public string Name { get; set; } = string.Empty;

        [FirestoreProperty]
        public double BaseCostForTwo { get; set; }

        [FirestoreProperty]
        public bool IsQuick { get; set; }

        [FirestoreProperty]
        public List<string> Tags { get; set; } = [];

        [FirestoreProperty]
        public List<FirestoreAislePilotIngredient> Ingredients { get; set; } = [];

        [FirestoreProperty]
        public List<string> RecipeSteps { get; set; } = [];

        [FirestoreProperty]
        public FirestoreAislePilotNutrition? NutritionPerServingMedium { get; set; }

        [FirestoreProperty]
        public string ImageUrl { get; set; } = string.Empty;

        [FirestoreProperty]
        public List<string> SuitableMealTypes { get; set; } = [];

        [FirestoreProperty]
        public DateTime CreatedAtUtc { get; set; }

        [FirestoreProperty]
        public string Source { get; set; } = string.Empty;
    }

    [FirestoreData]
    private sealed class FirestoreAislePilotIngredient
    {
        [FirestoreProperty]
        public string Name { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Department { get; set; } = string.Empty;

        [FirestoreProperty]
        public double QuantityForTwo { get; set; }

        [FirestoreProperty]
        public string Unit { get; set; } = string.Empty;

        [FirestoreProperty]
        public double EstimatedCostForTwo { get; set; }
    }

    [FirestoreData]
    private sealed class FirestoreAislePilotNutrition
    {
        [FirestoreProperty]
        public double Calories { get; set; }

        [FirestoreProperty]
        public double ProteinGrams { get; set; }

        [FirestoreProperty]
        public double CarbsGrams { get; set; }

        [FirestoreProperty]
        public double FatGrams { get; set; }

        [FirestoreProperty]
        public double ConfidenceScore { get; set; }
    }

    [FirestoreData]
    private sealed class FirestoreAislePilotDessertAddOn
    {
        [FirestoreProperty]
        public string Name { get; set; } = string.Empty;

        [FirestoreProperty]
        public List<FirestoreAislePilotIngredient> Ingredients { get; set; } = [];

        [FirestoreProperty]
        public DateTime UpdatedAtUtc { get; set; }

        [FirestoreProperty]
        public string Source { get; set; } = string.Empty;
    }

    [FirestoreData]
    private sealed class FirestoreAislePilotMealImage
    {
        [FirestoreProperty]
        public string Name { get; set; } = string.Empty;

        [FirestoreProperty]
        public string ImageUrl { get; set; } = string.Empty;

        [FirestoreProperty]
        public string ImageBase64 { get; set; } = string.Empty;

        [FirestoreProperty]
        public int ImageChunkCount { get; set; }

        [FirestoreProperty]
        public DateTime UpdatedAtUtc { get; set; }

        [FirestoreProperty]
        public string Source { get; set; } = string.Empty;
    }

    [FirestoreData]
    private sealed class FirestoreAislePilotMealImageChunk
    {
        [FirestoreProperty]
        public int Index { get; set; }

        [FirestoreProperty]
        public string Data { get; set; } = string.Empty;
    }

    [FirestoreData]
    private sealed class FirestoreAislePilotSupermarketLayout
    {
        [FirestoreProperty]
        public string Supermarket { get; set; } = string.Empty;

        [FirestoreProperty]
        public List<string> AisleOrder { get; set; } = [];

        [FirestoreProperty]
        public int Version { get; set; }

        [FirestoreProperty]
        public DateTime UpdatedAtUtc { get; set; }

        [FirestoreProperty]
        public string Source { get; set; } = string.Empty;
    }

    private sealed class OpenAiImageGenerationResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiImagePayload>? Data { get; set; }
    }

    private sealed class OpenAiImagePayload
    {
        [JsonPropertyName("b64_json")]
        public string? B64Json { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }
    }

    private sealed record PantrySuggestionCandidate(
        MealTemplate Template,
        AislePilotPantrySuggestionViewModel Suggestion,
        AislePilotPantrySuggestionViewModel UserOnlySuggestion,
        int UserMatchedTokenCount,
        int SpecificMatchedTokenCount,
        int Score);

    private sealed record AiMealBatchResult(
        IReadOnlyList<MealTemplate> Meals,
        string? OpenAiRequestId);
}
