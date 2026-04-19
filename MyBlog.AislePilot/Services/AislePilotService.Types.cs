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
        SupermarketLayoutResolution Layout,
        SupermarketPriceResolution PriceProfile,
        decimal HouseholdFactor,
        string DislikesOrAllergens,
        string PortionSize);

    internal sealed class MealNutritionEstimate
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
        public string Department { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal EstimatedCost { get; set; }
    }

    internal sealed record MealTemplate(
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

    internal sealed record IngredientTemplate(
        string Name,
        string Department,
        decimal QuantityForTwo,
        string Unit,
        decimal EstimatedCostForTwo);

    internal sealed record DessertAddOnTemplate(
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

    internal sealed class AiMealNutritionEstimate
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

    internal sealed class SupermarketLayoutResolution
    {
        public IReadOnlyList<string> AisleOrder { get; init; } = Array.Empty<string>();
        public string SourceLabel { get; init; } = string.Empty;
        public decimal ConfidenceScore { get; init; }
        public string ConfidenceLabel { get; init; } = string.Empty;
        public bool IsDefaultLayout { get; init; }
        public bool NeedsReview { get; init; }
        public DateTime? LastVerifiedUtc { get; init; }
        public IReadOnlyList<SupermarketLayoutEvidence> Evidence { get; init; } = Array.Empty<SupermarketLayoutEvidence>();
    }

    internal sealed class SupermarketLayoutEvidence
    {
        public string Title { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
    }

    internal sealed class SupermarketPriceResolution
    {
        public decimal RelativeCostFactor { get; init; } = 1m;
        public string RelativeCostBasis { get; init; } = string.Empty;
        public string SourceLabel { get; init; } = string.Empty;
        public decimal ConfidenceScore { get; init; }
        public string ConfidenceLabel { get; init; } = string.Empty;
        public bool IsDirectBasketData { get; init; }
        public bool NeedsReview { get; init; }
        public DateTime? LastVerifiedUtc { get; init; }
        public IReadOnlyList<SupermarketPriceEvidence> Evidence { get; init; } = Array.Empty<SupermarketPriceEvidence>();
    }

    internal sealed class SupermarketPriceEvidence
    {
        public string Title { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
    }

    private sealed class SupermarketPriceProfilesFilePayload
    {
        public int? Version { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public string? Notes { get; set; }
        public List<SupermarketPriceProfileFileEntry>? Profiles { get; set; }
    }

    private sealed class SupermarketPriceProfileFileEntry
    {
        public string? Supermarket { get; set; }
        public decimal? RelativeCostFactor { get; set; }
        public string? RelativeCostBasis { get; set; }
        public string? SourceLabel { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public string? ConfidenceLabel { get; set; }
        public bool? IsDirectBasketData { get; set; }
        public bool? NeedsReview { get; set; }
        public DateTime? LastVerifiedUtc { get; set; }
        public List<SupermarketPriceProfileFileEvidence>? Evidence { get; set; }
    }

    private sealed class SupermarketPriceProfileFileEvidence
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? SourceType { get; set; }
    }

    private sealed class SupermarketPriceFileCacheEntry
    {
        public string Path { get; init; } = string.Empty;
        public DateTime LastWriteUtc { get; init; }
        public IReadOnlyDictionary<string, SupermarketPriceResolution> Profiles { get; init; } =
            new Dictionary<string, SupermarketPriceResolution>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SupermarketLayoutResearchPayload
    {
        public List<string>? AisleOrder { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public string? ConfidenceLabel { get; set; }
        public bool? NeedsReview { get; set; }
        public List<SupermarketLayoutResearchSourcePayload>? Sources { get; set; }
    }

    private sealed class SupermarketLayoutResearchSourcePayload
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? SourceType { get; set; }
    }

    private sealed class SupermarketLayoutCacheEntry
    {
        public IReadOnlyList<string> AisleOrder { get; init; } = Array.Empty<string>();
        public DateTime UpdatedAtUtc { get; init; }
        public string SourceLabel { get; init; } = string.Empty;
        public decimal ConfidenceScore { get; init; }
        public string ConfidenceLabel { get; init; } = string.Empty;
        public bool IsDefaultLayout { get; init; }
        public bool NeedsReview { get; init; }
        public IReadOnlyList<SupermarketLayoutEvidence> Evidence { get; init; } = Array.Empty<SupermarketLayoutEvidence>();
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

        [FirestoreProperty]
        public double ConfidenceScore { get; set; }

        [FirestoreProperty]
        public string ConfidenceLabel { get; set; } = string.Empty;

        [FirestoreProperty]
        public bool NeedsReview { get; set; }

        [FirestoreProperty]
        public bool IsDefaultLayout { get; set; }

        [FirestoreProperty]
        public List<FirestoreAislePilotSupermarketLayoutEvidence> Evidence { get; set; } = [];
    }

    [FirestoreData]
    private sealed class FirestoreAislePilotSupermarketLayoutEvidence
    {
        [FirestoreProperty]
        public string Title { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Url { get; set; } = string.Empty;

        [FirestoreProperty]
        public string SourceType { get; set; } = string.Empty;
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

    internal sealed record PantrySuggestionCandidate(
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
