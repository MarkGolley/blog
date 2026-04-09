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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly Regex TrailingCommaRegex = new(",(?=\\s*[}\\]])", RegexOptions.Compiled);

    private const int MaxAiMealNameLength = 90;
    private const int MaxAiIngredientNameLength = 60;
    private const int MaxAiDepartmentLength = 32;
    private const int MaxAiUnitLength = 18;
    private const int MaxAiRecipeStepLength = 220;
    private const int MaxSavedEnjoyedMealNameLength = 90;
    private const int MaxSavedEnjoyedMealCount = 32;
    private const int DefaultSavedMealRepeatRatePercent = 35;
    private const int PrimaryAiMealPlanMaxTokens = 3400;
    private const int RetryAiMealPlanMaxTokens = 2200;
    private const int WarmupMealMaxTokens = 1000;
    private const int SpecialTreatMealMaxTokens = 1100;
    private const int MinMealsPerDay = 1;
    private const int MaxMealsPerDay = 3;
    private const int MaxPlanMealSlots = 21;
    private const int MaxFreshAiPlanMeals = 8;
    private const string AiMealsCollection = "aislePilotAiMeals";
    private const string MealImagesCollection = "aislePilotMealImages";
    private const string DessertAddOnsCollection = "aislePilotDessertAddOns";
    private const string SupermarketLayoutsCollection = "aislePilotSupermarketLayouts";
    private const string OpenAiChatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string OpenAiResponsesEndpoint = "https://api.openai.com/v1/responses";
    private const string OpenAiImageGenerationsEndpoint = "https://api.openai.com/v1/images/generations";
    private const int OpenAiMaxAttempts = 2;
    private const int OpenAiImageMaxAttempts = 2;
    private const int MaxMealImageBytesForFirestore = 700_000;
    private const int MealImageChunkCharLength = 180_000;
    private const string MealImageChunksSubcollection = "chunks";
    private const int MaxMealImageMissCacheEntries = 2048;
    private const int MaxAiMealPoolEntries = 320;
    private const int SupermarketLayoutCacheVersion = 2;
    private const int SupermarketLayoutRefreshCooldownMinutes = 120;
    private const int SupermarketLayoutStaleDays = 30;
    private const decimal AiIngredientKnownUnitPriceMinFactor = 0.45m;
    private const decimal AiIngredientKnownUnitPriceMaxFactor = 2.0m;
    private const decimal AiMealBaseCostMinToIngredientFactor = 0.90m;
    private const decimal AiMealBaseCostMaxToIngredientFactor = 1.35m;
    private static readonly TimeSpan OpenAiRequestTimeout = TimeSpan.FromSeconds(22);
    private static readonly TimeSpan OpenAiImageRequestTimeout = TimeSpan.FromSeconds(18);
    private static readonly TimeSpan OpenAiImageDownloadTimeout = TimeSpan.FromSeconds(18);
    private static readonly TimeSpan OpenAiGenerationBudget = TimeSpan.FromSeconds(65);
    private static readonly TimeSpan MaxOpenAiRetryAfterDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AiMealPoolEntryTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan MealImageLookupMissTtl = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DessertAddOnPoolRefreshInterval = TimeSpan.FromMinutes(30);

    private static readonly ConcurrentDictionary<string, MealTemplate> AiMealPool = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> AiMealPoolLastTouchedUtc =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> MealImagePool = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DessertAddOnTemplate> DessertAddOnPool =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> MealImageLookupMissesUtc =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> MealImageGenerationInFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> SpecialTreatGenerationInFlight =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> DessertAddOnRecoveryInFlight =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SupermarketLayoutCacheEntry> SupermarketLayoutCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, byte> SupermarketLayoutRefreshInFlight =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, DateTime> SupermarketLayoutLastAttemptUtc =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim AiMealPoolRefreshLock = new(1, 1);
    private static readonly SemaphoreSlim MealImagePoolRefreshLock = new(1, 1);
    private static readonly SemaphoreSlim DessertAddOnPoolRefreshLock = new(1, 1);
    private static readonly SemaphoreSlim MealImageGenerationThrottle = new(1, 1);
    private static readonly SemaphoreSlim SupermarketLayoutRefreshLock = new(1, 1);
    private static readonly SemaphoreSlim AiMealWarmupLock = new(1, 1);
    private static DateTime? _lastAiMealPoolRefreshUtc;
    private static DateTime? _lastMealImagePoolRefreshUtc;
    private static DateTime? _lastDessertAddOnPoolRefreshUtc;
    private static DateTime? _lastSupermarketLayoutCacheRefreshUtc;

    private static readonly HashSet<string> GenericPantryTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "various sauces",
        "mixed sauces",
        "random sauces",
        "oil",
        "sauce",
        "sauces",
        "soy sauce",
        "stir fry sauce",
        "spice",
        "spices",
        "herb",
        "herbs",
        "seasoning",
        "seasonings",
        "dried herbs",
        "salt",
        "pepper"
    };
    private static readonly string[] AssumedPantryBasics =
    [
        "olive oil",
        "salt",
        "pepper",
        "onions",
        "garlic",
        "dried herbs"
    ];
    private const int PantrySuggestionNearMatchThreshold = 50;
    private static readonly HashSet<string> IngredientDescriptorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "fresh",
        "frozen",
        "mixed",
        "diced",
        "chopped",
        "lean",
        "firm",
        "king",
        "greek",
        "bell",
        "sweet",
        "red",
        "green",
        "yellow",
        "cherry",
        "smoky",
        "boneless",
        "skinless",
        "dry",
        "dried",
        "powder",
        "plain",
        "baby",
        "large",
        "small",
        "medium",
        "extra",
        "virgin",
        "egg"
    };

    private static readonly Dictionary<string, string[]> IngredientAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Frozen mixed veg"] =
        [
            "mixed veg",
            "mixed vegetables",
            "vegetables",
            "veg",
            "peas",
            "frozen peas"
        ],
        ["Soy sauce"] =
        [
            "sauce",
            "sauces",
            "stir fry sauce"
        ],
        ["Courgettes"] =
        [
            "courgette",
            "zucchini",
            "zucchinis"
        ],
        ["Chicken breast"] =
        [
            "chicken breasts",
            "chicken fillet",
            "chicken fillets"
        ],
        ["Leeks"] =
        [
            "leek",
            "leak",
            "leaks"
        ],
        ["Puff pastry"] =
        [
            "pastry",
            "shortcrust pastry"
        ],
        ["Double cream"] =
        [
            "cream",
            "single cream"
        ]
    };
    private static readonly Dictionary<string, string[]> MealNameRequiredIngredientAnchors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rice"] = ["rice"],
        ["pasta"] = ["pasta"],
        ["noodle"] = ["noodle", "noodles"],
        ["couscous"] = ["couscous"],
        ["quinoa"] = ["quinoa"],
        ["potato"] = ["potato", "potatoes"],
        ["egg"] = ["egg", "eggs"]
    };

    private static readonly Dictionary<string, NutritionReference> IngredientNutritionReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chicken breast"] = new(165m, 31m, 0m, 3.6m),
        ["chicken thigh"] = new(209m, 25m, 0m, 11m),
        ["salmon"] = new(208m, 20m, 0m, 13m),
        ["turkey mince"] = new(170m, 22m, 0m, 9m),
        ["tofu"] = new(144m, 17m, 3m, 9m),
        ["red lentils"] = new(352m, 24m, 60m, 1.5m),
        ["rice"] = new(360m, 7.5m, 79m, 0.7m),
        ["egg noodles"] = new(350m, 12m, 70m, 3m),
        ["bell peppers"] = new(31m, 1m, 6m, 0.3m, 150m),
        ["soy sauce"] = new(53m, 8m, 4m, 0.6m),
        ["potatoes"] = new(77m, 2m, 17m, 0.1m),
        ["broccoli"] = new(34m, 2.8m, 7m, 0.4m, 300m),
        ["olive oil"] = new(884m, 0m, 0m, 100m),
        ["kidney beans"] = new(127m, 8.7m, 22.8m, 0.5m, 240m),
        ["chopped tomatoes"] = new(21m, 1m, 4.8m, 0.2m, 400m),
        ["chilli seasoning"] = new(280m, 12m, 45m, 8m, 30m),
        ["coconut milk"] = new(197m, 2m, 3m, 20m, 400m),
        ["spinach"] = new(23m, 2.9m, 3.6m, 0.4m),
        ["curry paste"] = new(150m, 3m, 20m, 5m, 50m),
        ["carrots"] = new(41m, 0.9m, 10m, 0.2m, 70m),
        ["stir fry sauce"] = new(120m, 2m, 20m, 1m, 120m),
        ["wraps"] = new(310m, 8m, 50m, 7m, 370m),
        ["greek yogurt"] = new(97m, 9m, 4m, 5m),
        ["lettuce"] = new(15m, 1.4m, 2.9m, 0.2m, 450m),
        ["leeks"] = new(61m, 1.5m, 14m, 0.3m),
        ["puff pastry"] = new(558m, 6m, 37m, 41m),
        ["double cream"] = new(467m, 2m, 3m, 48m),
        ["milk"] = new(64m, 3.4m, 4.8m, 3.6m),
        ["paneer"] = new(321m, 18m, 3.6m, 25m),
        ["onions"] = new(40m, 1.1m, 9.3m, 0.1m),
        ["tikka seasoning"] = new(280m, 12m, 45m, 8m, 30m),
        ["king prawns"] = new(99m, 24m, 0.2m, 0.3m),
        ["pasta"] = new(360m, 12m, 73m, 1.5m),
        ["passata"] = new(30m, 1.5m, 5m, 0.2m, 500m),
        ["parmesan"] = new(431m, 38m, 4m, 29m),
        ["lean beef mince"] = new(250m, 26m, 0m, 17m),
        ["frozen peas"] = new(81m, 5.4m, 14m, 0.4m),
        ["chickpeas"] = new(164m, 8.9m, 27m, 2.6m, 240m),
        ["quinoa"] = new(368m, 14m, 64m, 6m),
        ["cucumber"] = new(15m, 0.7m, 3.6m, 0.1m, 300m),
        ["cherry tomatoes"] = new(18m, 0.9m, 3.9m, 0.2m),
        ["eggs"] = new(143m, 13m, 1.1m, 10.6m, 50m),
        ["frozen mixed veg"] = new(65m, 3.5m, 11m, 0.5m),
        ["black beans"] = new(132m, 8.9m, 23.7m, 0.5m, 240m),
        ["sweet potatoes"] = new(86m, 1.6m, 20m, 0.1m),
        ["halloumi"] = new(316m, 22m, 2m, 25m),
        ["couscous"] = new(376m, 12.8m, 77m, 0.6m),
        ["courgettes"] = new(17m, 1.2m, 3.1m, 0.3m, 200m),
        ["risotto rice"] = new(360m, 7.4m, 80m, 0.6m),
        ["chestnut mushrooms"] = new(22m, 3.1m, 3.3m, 0.3m),
        ["pesto"] = new(440m, 5m, 8m, 44m, 190m),
        ["mozzarella"] = new(280m, 22m, 2m, 17m, 125m),
        ["cod fillets"] = new(82m, 18m, 0m, 0.7m, 140m),
        ["green beans"] = new(31m, 1.8m, 7m, 0.2m),
        ["paprika"] = new(282m, 14m, 54m, 13m, 28m),
        ["firm tofu"] = new(144m, 17m, 3m, 9m),
        ["turkey"] = new(170m, 22m, 0m, 9m),
        ["beef mince"] = new(250m, 26m, 0m, 17m),
        ["prawns"] = new(99m, 24m, 0.2m, 0.3m),
        ["cod"] = new(82m, 18m, 0m, 0.7m)
    };

    private static readonly Dictionary<string, NutritionReference> DepartmentNutritionFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Produce"] = new(45m, 1.5m, 8m, 0.4m),
        ["Bakery"] = new(280m, 8m, 52m, 4m),
        ["Meat & Fish"] = new(185m, 24m, 0m, 8m),
        ["Dairy & Eggs"] = new(180m, 12m, 4m, 12m),
        ["Frozen"] = new(95m, 5m, 13m, 2m),
        ["Tins & Dry Goods"] = new(245m, 10m, 35m, 6m),
        ["Spices & Sauces"] = new(210m, 4m, 25m, 10m),
        ["Other"] = new(160m, 6m, 20m, 6m)
    };

    private static readonly Dictionary<string, decimal> IngredientNutritionConsumptionFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rice"] = 0.40m,
        ["pasta"] = 0.42m,
        ["egg noodles"] = 0.45m,
        ["quinoa"] = 0.45m,
        ["couscous"] = 0.42m,
        ["risotto rice"] = 0.42m,
        ["red lentils"] = 0.45m,
        ["wraps"] = 0.65m
    };

    private static readonly HashSet<string> GenericPantryTokensNormalized = new(
        GenericPantryTokens.Select(NormalizePantryText),
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AssumedPantryBasicsNormalized = new(
        AssumedPantryBasics.Select(NormalizePantryText),
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SupportedSupermarkets =
    [
        "Tesco",
        "Sainsbury's",
        "Aldi",
        "Lidl",
        "Asda",
        "Custom"
    ];

    private static readonly string[] SupportedPortionSizes =
    [
        "Small",
        "Medium",
        "Large"
    ];

    private static readonly string[] SupportedDietaryModes =
    [
        "Balanced",
        "High-Protein",
        "Vegetarian",
        "Vegan",
        "Pescatarian",
        "Gluten-Free"
    ];
    private static readonly string[] SupportedAiMealTags =
    [
        .. SupportedDietaryModes,
        "Special Treat"
    ];

    private static readonly string[] BreakfastNameKeywords =
    [
        "breakfast",
        "oat",
        "porridge",
        "granola",
        "muesli",
        "omelette",
        "omelet",
        "scrambled egg",
        "yogurt",
        "yoghurt",
        "toast",
        "pancake",
        "chia"
    ];

    private static readonly string[] LunchNameKeywords =
    [
        "lunch",
        "salad",
        "pasta salad",
        "wrap",
        "wraps",
        "sandwich",
        "toastie",
        "panini",
        "soup",
        "couscous bowl",
        "couscous bowls",
        "grain bowl",
        "grain bowls",
        "poke bowl",
        "poke bowls"
    ];

    private static readonly string[] SpecialTreatNameKeywords =
    [
        "special treat",
        "indulgent",
        "pie",
        "risotto",
        "bake",
        "roast",
        "salmon",
        "steak",
        "parmesan",
        "creamy",
        "lasagne",
        "lasagna",
        "loaded",
        "sticky"
    ];
    private static readonly string[] RecipeActionKeywords =
    [
        "heat",
        "cook",
        "bake",
        "roast",
        "stir",
        "simmer",
        "boil",
        "mix",
        "add",
        "season",
        "serve"
    ];
    private static readonly string[] WeakRecipeStepPhrases =
    [
        "prepare ingredients",
        "prep ingredients",
        "cook until done",
        "season to taste",
        "serve and enjoy",
        "as desired",
        "if needed"
    ];
    private static readonly Regex RecipeConcreteCueRegex = new(
        @"\b(\d+(\.\d+)?\s?(min|mins|minute|minutes|c|f|ml|g|kg)|medium|medium-high|high|low|simmer|roast|bake)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly DessertAddOnTemplate[] DessertAddOnTemplates =
    [
        new(
            "Chocolate sponge tray bake",
            [
                new IngredientTemplate("Self-raising flour", "Tins & Dry Goods", 0.30m, "kg", 0.40m),
                new IngredientTemplate("Caster sugar", "Tins & Dry Goods", 0.20m, "kg", 0.35m),
                new IngredientTemplate("Cocoa powder", "Tins & Dry Goods", 0.08m, "kg", 0.80m),
                new IngredientTemplate("Eggs", "Dairy & Eggs", 4m, "pcs", 0.95m),
                new IngredientTemplate("Unsalted butter", "Dairy & Eggs", 0.22m, "kg", 1.55m)
            ]),
        new(
            "Lemon drizzle loaf cake",
            [
                new IngredientTemplate("Self-raising flour", "Tins & Dry Goods", 0.30m, "kg", 0.40m),
                new IngredientTemplate("Caster sugar", "Tins & Dry Goods", 0.22m, "kg", 0.39m),
                new IngredientTemplate("Eggs", "Dairy & Eggs", 4m, "pcs", 0.95m),
                new IngredientTemplate("Unsalted butter", "Dairy & Eggs", 0.22m, "kg", 1.55m),
                new IngredientTemplate("Lemons", "Produce", 2m, "pcs", 0.90m)
            ]),
        new(
            "Apple crumble pots",
            [
                new IngredientTemplate("Cooking apples", "Produce", 0.60m, "kg", 1.20m),
                new IngredientTemplate("Plain flour", "Tins & Dry Goods", 0.25m, "kg", 0.30m),
                new IngredientTemplate("Brown sugar", "Tins & Dry Goods", 0.18m, "kg", 0.45m),
                new IngredientTemplate("Unsalted butter", "Dairy & Eggs", 0.20m, "kg", 1.40m),
                new IngredientTemplate("Ground cinnamon", "Spices & Sauces", 0.04m, "jar", 0.30m)
            ])
    ];

    private static readonly WarmupProfile[] WarmupProfilesSingleMode =
    [
        new("High-Protein", ["High-Protein"]),
        new("Vegetarian", ["Vegetarian"]),
        new("Vegan", ["Vegan"]),
        new("Pescatarian", ["Pescatarian"]),
        new("Gluten-Free", ["Gluten-Free"])
    ];

    private static readonly WarmupProfile[] WarmupProfilesKeyPairs =
    [
        new("Vegetarian + Gluten-Free", ["Vegetarian", "Gluten-Free"]),
        new("Vegan + Gluten-Free", ["Vegan", "Gluten-Free"]),
        new("Pescatarian + Gluten-Free", ["Pescatarian", "Gluten-Free"]),
        new("High-Protein + Gluten-Free", ["High-Protein", "Gluten-Free"])
    ];

    private static readonly string[] DefaultAisleOrder =
    [
        "Produce",
        "Bakery",
        "Meat & Fish",
        "Dairy & Eggs",
        "Frozen",
        "Tins & Dry Goods",
        "Spices & Sauces",
        "Snacks",
        "Drinks",
        "Household",
        "Other"
    ];
    private static readonly Dictionary<string, string> AisleOrderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["produce"] = "Produce",
        ["fresh produce"] = "Produce",
        ["fruit and veg"] = "Produce",
        ["fruit & veg"] = "Produce",
        ["bakery"] = "Bakery",
        ["bread"] = "Bakery",
        ["meat"] = "Meat & Fish",
        ["fish"] = "Meat & Fish",
        ["meat and fish"] = "Meat & Fish",
        ["meat & fish"] = "Meat & Fish",
        ["dairy"] = "Dairy & Eggs",
        ["dairy and eggs"] = "Dairy & Eggs",
        ["dairy & eggs"] = "Dairy & Eggs",
        ["eggs"] = "Dairy & Eggs",
        ["frozen"] = "Frozen",
        ["freezer"] = "Frozen",
        ["tin"] = "Tins & Dry Goods",
        ["tins"] = "Tins & Dry Goods",
        ["tinned"] = "Tins & Dry Goods",
        ["canned"] = "Tins & Dry Goods",
        ["cupboard"] = "Tins & Dry Goods",
        ["dry good"] = "Tins & Dry Goods",
        ["dry goods"] = "Tins & Dry Goods",
        ["tin and dry goods"] = "Tins & Dry Goods",
        ["tins and dry goods"] = "Tins & Dry Goods",
        ["tins & dry goods"] = "Tins & Dry Goods",
        ["spice"] = "Spices & Sauces",
        ["spices"] = "Spices & Sauces",
        ["sauce"] = "Spices & Sauces",
        ["sauces"] = "Spices & Sauces",
        ["spices and sauces"] = "Spices & Sauces",
        ["spices & sauces"] = "Spices & Sauces",
        ["snack"] = "Snacks",
        ["snacks"] = "Snacks",
        ["drink"] = "Drinks",
        ["drinks"] = "Drinks",
        ["household"] = "Household",
        ["other"] = "Other"
    };

    private static readonly Dictionary<string, string[]> SupermarketAisleOrders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tesco"] =
        [
            "Produce", "Bakery", "Meat & Fish", "Dairy & Eggs", "Frozen", "Tins & Dry Goods", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ],
        ["Sainsbury's"] =
        [
            "Produce", "Bakery", "Dairy & Eggs", "Meat & Fish", "Frozen", "Tins & Dry Goods", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ],
        ["Aldi"] =
        [
            "Produce", "Tins & Dry Goods", "Meat & Fish", "Dairy & Eggs", "Frozen", "Bakery", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ],
        ["Lidl"] =
        [
            "Produce", "Bakery", "Dairy & Eggs", "Meat & Fish", "Tins & Dry Goods", "Frozen", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ],
        ["Asda"] =
        [
            "Produce", "Bakery", "Meat & Fish", "Dairy & Eggs", "Frozen", "Tins & Dry Goods", "Spices & Sauces",
            "Snacks", "Drinks", "Household", "Other"
        ]
    };

    private static readonly IReadOnlyList<MealTemplate> MealTemplates =
    [
        new(
            "Chicken stir fry with rice",
            6.40m,
            IsQuick: true,
            ["Balanced", "High-Protein"],
            [
                new IngredientTemplate("Chicken breast", "Meat & Fish", 0.45m, "kg", 3.25m),
                new IngredientTemplate("Bell peppers", "Produce", 3m, "pcs", 1.50m),
                new IngredientTemplate("Rice", "Tins & Dry Goods", 0.40m, "kg", 0.85m),
                new IngredientTemplate("Soy sauce", "Spices & Sauces", 0.12m, "bottle", 0.45m)
            ]),
        new(
            "Chicken and pea egg fried rice",
            5.90m,
            IsQuick: true,
            ["Balanced", "High-Protein"],
            [
                new IngredientTemplate("Chicken breast", "Meat & Fish", 0.40m, "kg", 2.90m),
                new IngredientTemplate("Eggs", "Dairy & Eggs", 4m, "pcs", 0.95m),
                new IngredientTemplate("Rice", "Tins & Dry Goods", 0.40m, "kg", 0.85m),
                new IngredientTemplate("Frozen peas", "Frozen", 0.35m, "kg", 0.70m),
                new IngredientTemplate("Soy sauce", "Spices & Sauces", 0.10m, "bottle", 0.50m)
            ]),
        new(
            "Salmon, potatoes, and broccoli",
            8.90m,
            IsQuick: true,
            ["Balanced", "High-Protein", "Pescatarian", "Gluten-Free", "Special Treat"],
            [
                new IngredientTemplate("Salmon fillets", "Meat & Fish", 0.36m, "kg", 4.40m),
                new IngredientTemplate("Potatoes", "Produce", 0.90m, "kg", 1.10m),
                new IngredientTemplate("Broccoli", "Produce", 2m, "pcs", 1.60m),
                new IngredientTemplate("Olive oil", "Spices & Sauces", 0.08m, "bottle", 0.55m)
            ]),
        new(
            "Turkey chilli with beans",
            7.20m,
            IsQuick: false,
            ["Balanced", "High-Protein", "Gluten-Free"],
            [
                new IngredientTemplate("Turkey mince", "Meat & Fish", 0.50m, "kg", 3.20m),
                new IngredientTemplate("Kidney beans", "Tins & Dry Goods", 2m, "tins", 1.20m),
                new IngredientTemplate("Chopped tomatoes", "Tins & Dry Goods", 2m, "tins", 1.00m),
                new IngredientTemplate("Chilli seasoning", "Spices & Sauces", 1m, "pack", 0.55m)
            ]),
        new(
            "Veggie lentil curry",
            5.10m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "Vegan", "Gluten-Free"],
            [
                new IngredientTemplate("Red lentils", "Tins & Dry Goods", 0.45m, "kg", 1.10m),
                new IngredientTemplate("Coconut milk", "Tins & Dry Goods", 2m, "tins", 1.60m),
                new IngredientTemplate("Spinach", "Produce", 0.30m, "kg", 1.20m),
                new IngredientTemplate("Curry paste", "Spices & Sauces", 1m, "jar", 0.80m)
            ]),
        new(
            "Tofu noodle bowls",
            5.80m,
            IsQuick: true,
            ["Balanced", "Vegetarian", "Vegan"],
            [
                new IngredientTemplate("Firm tofu", "Dairy & Eggs", 0.40m, "kg", 1.60m),
                new IngredientTemplate("Egg noodles", "Tins & Dry Goods", 0.35m, "kg", 1.00m),
                new IngredientTemplate("Carrots", "Produce", 4m, "pcs", 0.80m),
                new IngredientTemplate("Stir fry sauce", "Spices & Sauces", 1m, "jar", 0.75m)
            ]),
        new(
            "Greek yogurt chicken wraps",
            6.00m,
            IsQuick: true,
            ["Balanced", "High-Protein"],
            [
                new IngredientTemplate("Chicken thigh strips", "Meat & Fish", 0.45m, "kg", 2.70m),
                new IngredientTemplate("Wraps", "Bakery", 1m, "pack", 1.00m),
                new IngredientTemplate("Greek yogurt", "Dairy & Eggs", 0.35m, "kg", 1.35m),
                new IngredientTemplate("Lettuce", "Produce", 1m, "head", 0.95m)
            ])
        {
            SuitableMealTypes = ["Lunch", "Dinner"]
        },
        new(
            "Chicken and leek cream pie",
            7.20m,
            IsQuick: false,
            ["Balanced", "High-Protein", "Special Treat"],
            [
                new IngredientTemplate("Chicken breast", "Meat & Fish", 0.45m, "kg", 3.25m),
                new IngredientTemplate("Leeks", "Produce", 2m, "pcs", 1.20m),
                new IngredientTemplate("Puff pastry", "Bakery", 1m, "pack", 1.60m),
                new IngredientTemplate("Double cream", "Dairy & Eggs", 0.25m, "pot", 0.95m),
                new IngredientTemplate("Milk", "Dairy & Eggs", 0.30m, "litre", 0.55m)
            ]),
        new(
            "Paneer tikka tray bake",
            6.50m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "Gluten-Free", "Special Treat"],
            [
                new IngredientTemplate("Paneer", "Dairy & Eggs", 0.40m, "kg", 2.40m),
                new IngredientTemplate("Onions", "Produce", 0.60m, "kg", 0.70m),
                new IngredientTemplate("Bell peppers", "Produce", 3m, "pcs", 1.50m),
                new IngredientTemplate("Tikka seasoning", "Spices & Sauces", 1m, "pack", 0.85m)
            ]),
        new(
            "Roast chicken and veg tray bake",
            7.30m,
            IsQuick: false,
            ["Balanced", "High-Protein", "Gluten-Free"],
            [
                new IngredientTemplate("Chicken breasts", "Meat & Fish", 0.50m, "kg", 3.40m),
                new IngredientTemplate("Bell peppers", "Produce", 3m, "pcs", 1.50m),
                new IngredientTemplate("Courgettes", "Produce", 2m, "pcs", 1.20m),
                new IngredientTemplate("Sweet potatoes", "Produce", 0.90m, "kg", 1.55m),
                new IngredientTemplate("Dried herbs", "Spices & Sauces", 0.08m, "jar", 0.65m)
            ]),
        new(
            "Prawn tomato pasta",
            7.10m,
            IsQuick: true,
            ["Balanced", "Pescatarian"],
            [
                new IngredientTemplate("King prawns", "Meat & Fish", 0.32m, "kg", 3.40m),
                new IngredientTemplate("Pasta", "Tins & Dry Goods", 0.45m, "kg", 0.90m),
                new IngredientTemplate("Passata", "Tins & Dry Goods", 0.70m, "bottle", 0.85m),
                new IngredientTemplate("Parmesan", "Dairy & Eggs", 0.12m, "kg", 1.10m)
            ]),
        new(
            "Beef and veg rice bowls",
            8.20m,
            IsQuick: false,
            ["Balanced", "High-Protein"],
            [
                new IngredientTemplate("Lean beef mince", "Meat & Fish", 0.50m, "kg", 3.80m),
                new IngredientTemplate("Rice", "Tins & Dry Goods", 0.45m, "kg", 0.95m),
                new IngredientTemplate("Frozen peas", "Frozen", 0.40m, "kg", 0.80m),
                new IngredientTemplate("Onions", "Produce", 0.40m, "kg", 0.55m)
            ]),
        new(
            "Chickpea quinoa salad bowls",
            5.40m,
            IsQuick: true,
            ["Balanced", "Vegetarian", "Vegan", "Gluten-Free"],
            [
                new IngredientTemplate("Chickpeas", "Tins & Dry Goods", 2m, "tins", 1.10m),
                new IngredientTemplate("Quinoa", "Tins & Dry Goods", 0.35m, "kg", 1.70m),
                new IngredientTemplate("Cucumber", "Produce", 1m, "pcs", 0.60m),
                new IngredientTemplate("Cherry tomatoes", "Produce", 0.30m, "kg", 1.00m)
            ])
        {
            SuitableMealTypes = ["Lunch", "Dinner"]
        },
        new(
            "Egg fried rice",
            4.80m,
            IsQuick: true,
            ["Balanced", "Vegetarian"],
            [
                new IngredientTemplate("Eggs", "Dairy & Eggs", 6m, "pcs", 1.30m),
                new IngredientTemplate("Rice", "Tins & Dry Goods", 0.45m, "kg", 0.95m),
                new IngredientTemplate("Frozen mixed veg", "Frozen", 0.50m, "kg", 1.05m),
                new IngredientTemplate("Soy sauce", "Spices & Sauces", 0.10m, "bottle", 0.40m)
            ])
        {
            SuitableMealTypes = ["Lunch", "Dinner"]
        },
        new(
            "Black bean sweet potato chilli",
            5.60m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "Vegan", "Gluten-Free"],
            [
                new IngredientTemplate("Black beans", "Tins & Dry Goods", 2m, "tins", 1.20m),
                new IngredientTemplate("Sweet potatoes", "Produce", 0.90m, "kg", 1.55m),
                new IngredientTemplate("Chopped tomatoes", "Tins & Dry Goods", 2m, "tins", 1.00m),
                new IngredientTemplate("Chilli seasoning", "Spices & Sauces", 1m, "pack", 0.55m)
            ]),
        new(
            "Smoky chickpea tomato stew",
            5.30m,
            IsQuick: false,
            ["Vegan", "Gluten-Free"],
            [
                new IngredientTemplate("Chickpeas", "Tins & Dry Goods", 2m, "tins", 1.10m),
                new IngredientTemplate("Chopped tomatoes", "Tins & Dry Goods", 2m, "tins", 1.00m),
                new IngredientTemplate("Spinach", "Produce", 0.25m, "kg", 1.00m),
                new IngredientTemplate("Paprika", "Spices & Sauces", 0.06m, "jar", 0.55m)
            ]),
        new(
            "Tofu coconut veg curry",
            5.90m,
            IsQuick: true,
            ["Vegan", "Gluten-Free", "Special Treat"],
            [
                new IngredientTemplate("Firm tofu", "Dairy & Eggs", 0.40m, "kg", 1.60m),
                new IngredientTemplate("Coconut milk", "Tins & Dry Goods", 2m, "tins", 1.60m),
                new IngredientTemplate("Frozen mixed veg", "Frozen", 0.50m, "kg", 1.05m),
                new IngredientTemplate("Curry paste", "Spices & Sauces", 1m, "jar", 0.80m)
            ]),
        new(
            "Sesame tofu rice bowls",
            5.70m,
            IsQuick: true,
            ["Vegan", "Gluten-Free"],
            [
                new IngredientTemplate("Firm tofu", "Dairy & Eggs", 0.40m, "kg", 1.60m),
                new IngredientTemplate("Rice", "Tins & Dry Goods", 0.45m, "kg", 0.95m),
                new IngredientTemplate("Carrots", "Produce", 4m, "pcs", 0.80m),
                new IngredientTemplate("Soy sauce", "Spices & Sauces", 0.12m, "bottle", 0.45m)
            ]),
        new(
            "Halloumi couscous bowls",
            6.30m,
            IsQuick: true,
            ["Balanced", "Vegetarian"],
            [
                new IngredientTemplate("Halloumi", "Dairy & Eggs", 0.30m, "kg", 2.40m),
                new IngredientTemplate("Couscous", "Tins & Dry Goods", 0.30m, "kg", 0.90m),
                new IngredientTemplate("Courgettes", "Produce", 2m, "pcs", 1.20m),
                new IngredientTemplate("Cherry tomatoes", "Produce", 0.25m, "kg", 0.90m)
            ])
        {
            SuitableMealTypes = ["Lunch", "Dinner"]
        },
        new(
            "Mushroom spinach risotto",
            6.10m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "Gluten-Free", "Special Treat"],
            [
                new IngredientTemplate("Risotto rice", "Tins & Dry Goods", 0.40m, "kg", 1.30m),
                new IngredientTemplate("Chestnut mushrooms", "Produce", 0.40m, "kg", 1.45m),
                new IngredientTemplate("Spinach", "Produce", 0.25m, "kg", 1.00m),
                new IngredientTemplate("Parmesan", "Dairy & Eggs", 0.10m, "kg", 1.00m)
            ]),
        new(
            "Pesto mozzarella pasta bake",
            6.40m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "Special Treat"],
            [
                new IngredientTemplate("Pasta", "Tins & Dry Goods", 0.45m, "kg", 0.90m),
                new IngredientTemplate("Pesto", "Spices & Sauces", 1m, "jar", 1.35m),
                new IngredientTemplate("Mozzarella", "Dairy & Eggs", 2m, "balls", 1.70m),
                new IngredientTemplate("Cherry tomatoes", "Produce", 0.30m, "kg", 1.00m)
            ]),
        new(
            "Creamy chicken and mushroom pasta bake",
            8.10m,
            IsQuick: false,
            ["Balanced", "High-Protein", "Special Treat"],
            [
                new IngredientTemplate("Chicken breast", "Meat & Fish", 0.50m, "kg", 3.45m),
                new IngredientTemplate("Pasta", "Tins & Dry Goods", 0.45m, "kg", 0.90m),
                new IngredientTemplate("Chestnut mushrooms", "Produce", 0.35m, "kg", 1.35m),
                new IngredientTemplate("Double cream", "Dairy & Eggs", 0.25m, "pot", 0.95m),
                new IngredientTemplate("Parmesan", "Dairy & Eggs", 0.10m, "kg", 1.05m)
            ]),
        new(
            "Halloumi and harissa roast veg tray bake",
            7.20m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "Gluten-Free", "Special Treat"],
            [
                new IngredientTemplate("Halloumi", "Dairy & Eggs", 0.35m, "kg", 2.70m),
                new IngredientTemplate("Sweet potatoes", "Produce", 0.90m, "kg", 1.55m),
                new IngredientTemplate("Courgettes", "Produce", 2m, "pcs", 1.20m),
                new IngredientTemplate("Bell peppers", "Produce", 3m, "pcs", 1.50m),
                new IngredientTemplate("Harissa paste", "Spices & Sauces", 0.12m, "jar", 0.70m)
            ]),
        new(
            "Sticky sesame tofu rice bowl",
            6.70m,
            IsQuick: false,
            ["Vegan", "Gluten-Free", "Special Treat"],
            [
                new IngredientTemplate("Firm tofu", "Dairy & Eggs", 0.45m, "kg", 1.80m),
                new IngredientTemplate("Rice", "Tins & Dry Goods", 0.45m, "kg", 0.95m),
                new IngredientTemplate("Broccoli", "Produce", 2m, "pcs", 1.60m),
                new IngredientTemplate("Carrots", "Produce", 4m, "pcs", 0.80m),
                new IngredientTemplate("Tamari", "Spices & Sauces", 0.18m, "bottle", 1.10m)
            ]),
        new(
            "Greek yogurt berry oat pots",
            4.10m,
            IsQuick: true,
            ["Balanced", "Vegetarian", "High-Protein"],
            [
                new IngredientTemplate("Greek yogurt", "Dairy & Eggs", 0.40m, "kg", 1.50m),
                new IngredientTemplate("Oats", "Tins & Dry Goods", 0.22m, "kg", 0.40m),
                new IngredientTemplate("Frozen berries", "Frozen", 0.35m, "kg", 1.50m),
                new IngredientTemplate("Honey", "Spices & Sauces", 0.10m, "jar", 0.70m)
            ])
        {
            SuitableMealTypes = ["Breakfast"]
        },
        new(
            "Spinach and tomato egg muffins",
            4.70m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "High-Protein", "Gluten-Free"],
            [
                new IngredientTemplate("Eggs", "Dairy & Eggs", 8m, "pcs", 1.70m),
                new IngredientTemplate("Spinach", "Produce", 0.20m, "kg", 0.85m),
                new IngredientTemplate("Cherry tomatoes", "Produce", 0.25m, "kg", 0.95m),
                new IngredientTemplate("Greek yogurt", "Dairy & Eggs", 0.15m, "kg", 0.65m)
            ])
        {
            SuitableMealTypes = ["Breakfast", "Lunch"]
        },
        new(
            "Tofu spinach breakfast scramble",
            4.55m,
            IsQuick: true,
            ["Balanced", "Vegan", "High-Protein", "Gluten-Free"],
            [
                new IngredientTemplate("Firm tofu", "Dairy & Eggs", 0.40m, "kg", 1.60m),
                new IngredientTemplate("Spinach", "Produce", 0.20m, "kg", 0.85m),
                new IngredientTemplate("Cherry tomatoes", "Produce", 0.25m, "kg", 0.95m),
                new IngredientTemplate("Sweet potatoes", "Produce", 0.60m, "kg", 1.15m)
            ])
        {
            SuitableMealTypes = ["Breakfast", "Lunch"]
        },
        new(
            "Smoked salmon spinach egg scramble",
            5.90m,
            IsQuick: true,
            ["Balanced", "High-Protein", "Pescatarian", "Gluten-Free"],
            [
                new IngredientTemplate("Smoked salmon", "Meat & Fish", 0.18m, "kg", 2.95m),
                new IngredientTemplate("Eggs", "Dairy & Eggs", 6m, "pcs", 1.30m),
                new IngredientTemplate("Spinach", "Produce", 0.18m, "kg", 0.80m),
                new IngredientTemplate("Milk", "Dairy & Eggs", 0.18m, "l", 0.45m)
            ])
        {
            SuitableMealTypes = ["Breakfast", "Lunch"]
        },
        new(
            "Smoked salmon scrambled eggs on toast",
            6.10m,
            IsQuick: true,
            ["Balanced", "High-Protein", "Pescatarian"],
            [
                new IngredientTemplate("Smoked salmon", "Meat & Fish", 0.20m, "kg", 3.25m),
                new IngredientTemplate("Eggs", "Dairy & Eggs", 6m, "pcs", 1.30m),
                new IngredientTemplate("Wholemeal bread", "Bakery", 1m, "pack", 1.05m),
                new IngredientTemplate("Milk", "Dairy & Eggs", 0.20m, "l", 0.50m)
            ])
        {
            SuitableMealTypes = ["Breakfast", "Lunch"]
        },
        new(
            "Mediterranean hummus wraps",
            4.70m,
            IsQuick: true,
            ["Balanced", "Vegetarian", "Vegan"],
            [
                new IngredientTemplate("Wraps", "Bakery", 1m, "pack", 1.00m),
                new IngredientTemplate("Hummus", "Dairy & Eggs", 0.22m, "kg", 1.20m),
                new IngredientTemplate("Cucumber", "Produce", 1m, "pcs", 0.60m),
                new IngredientTemplate("Cherry tomatoes", "Produce", 0.25m, "kg", 0.90m)
            ])
        {
            SuitableMealTypes = ["Lunch"]
        },
        new(
            "Tuna sweetcorn pasta salad",
            5.50m,
            IsQuick: true,
            ["Balanced", "Pescatarian", "High-Protein"],
            [
                new IngredientTemplate("Tuna chunks", "Meat & Fish", 2m, "tins", 2.40m),
                new IngredientTemplate("Pasta", "Tins & Dry Goods", 0.40m, "kg", 0.80m),
                new IngredientTemplate("Sweetcorn", "Tins & Dry Goods", 1m, "tin", 0.70m),
                new IngredientTemplate("Greek yogurt", "Dairy & Eggs", 0.20m, "kg", 0.85m)
            ])
        {
            SuitableMealTypes = ["Lunch"]
        },
        new(
            "Chicken couscous lunch bowls",
            5.60m,
            IsQuick: true,
            ["Balanced", "High-Protein"],
            [
                new IngredientTemplate("Chicken breast", "Meat & Fish", 0.40m, "kg", 2.90m),
                new IngredientTemplate("Couscous", "Tins & Dry Goods", 0.30m, "kg", 0.90m),
                new IngredientTemplate("Cucumber", "Produce", 1m, "pcs", 0.60m),
                new IngredientTemplate("Cherry tomatoes", "Produce", 0.25m, "kg", 0.90m)
            ])
        {
            SuitableMealTypes = ["Lunch", "Dinner"]
        },
        new(
            "Lentil vegetable soup bowls",
            4.60m,
            IsQuick: false,
            ["Balanced", "Vegetarian", "Vegan"],
            [
                new IngredientTemplate("Red lentils", "Tins & Dry Goods", 0.35m, "kg", 0.90m),
                new IngredientTemplate("Carrots", "Produce", 4m, "pcs", 0.80m),
                new IngredientTemplate("Chopped tomatoes", "Tins & Dry Goods", 2m, "tins", 1.00m),
                new IngredientTemplate("Spinach", "Produce", 0.20m, "kg", 0.80m)
            ])
        {
            SuitableMealTypes = ["Lunch", "Dinner"]
        },
        new(
            "Baked cod with sweet potato wedges",
            7.90m,
            IsQuick: false,
            ["Balanced", "High-Protein", "Pescatarian", "Gluten-Free"],
            [
                new IngredientTemplate("Cod fillets", "Meat & Fish", 0.36m, "kg", 3.90m),
                new IngredientTemplate("Sweet potatoes", "Produce", 0.95m, "kg", 1.65m),
                new IngredientTemplate("Green beans", "Produce", 0.30m, "kg", 1.25m),
                new IngredientTemplate("Paprika", "Spices & Sauces", 0.06m, "jar", 0.55m)
            ])
    ];
    private static readonly IReadOnlyList<IngredientUnitPriceReference> IngredientUnitPriceReferences =
        BuildIngredientUnitPriceReferences();

    private readonly HttpClient? _httpClient;
    private readonly ILogger<AislePilotService>? _logger;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly string _imageModel;
    private readonly bool _enableAiGeneration;
    private readonly bool _enableAiImageGeneration;
    private readonly bool _allowTemplateFallback;
    private readonly FirestoreDb? _db;
    private readonly IWebHostEnvironment? _webHostEnvironment;
    private readonly IAislePilotPlanGenerationOrchestrator _planGenerationOrchestrator;
    private readonly IAislePilotPlanComparisonService _planComparisonService;
    private readonly IAislePilotBudgetRebalancePipeline _budgetRebalancePipeline;
    private readonly IAislePilotMealSwapPipeline _mealSwapPipeline;
    private readonly AislePilotSlotSelectionEngine _slotSelectionEngine;
    private readonly AislePilotNutritionRecipeFallbackEngine _nutritionRecipeFallbackEngine;
    private readonly AislePilotPantryRankingEngine _pantryRankingEngine;

    public AislePilotService(
        HttpClient? httpClient = null,
        IConfiguration? configuration = null,
        ILogger<AislePilotService>? logger = null,
        FirestoreDb? db = null,
        IWebHostEnvironment? webHostEnvironment = null,
        IAislePilotPlanGenerationOrchestrator? planGenerationOrchestrator = null,
        IAislePilotPlanComparisonService? planComparisonService = null,
        IAislePilotBudgetRebalancePipeline? budgetRebalancePipeline = null,
        IAislePilotMealSwapPipeline? mealSwapPipeline = null,
        AislePilotSlotSelectionEngine? slotSelectionEngine = null,
        AislePilotNutritionRecipeFallbackEngine? nutritionRecipeFallbackEngine = null,
        AislePilotPantryRankingEngine? pantryRankingEngine = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _db = db;
        _webHostEnvironment = webHostEnvironment;
        _planGenerationOrchestrator = planGenerationOrchestrator ?? new AislePilotPlanGenerationOrchestrator();
        _planComparisonService = planComparisonService ?? new AislePilotPlanComparisonService();
        _budgetRebalancePipeline = budgetRebalancePipeline ?? new AislePilotBudgetRebalancePipeline();
        _mealSwapPipeline = mealSwapPipeline ?? new AislePilotMealSwapPipeline();
        _slotSelectionEngine = slotSelectionEngine ?? new AislePilotSlotSelectionEngine();
        _nutritionRecipeFallbackEngine = nutritionRecipeFallbackEngine ?? new AislePilotNutritionRecipeFallbackEngine();
        _pantryRankingEngine = pantryRankingEngine ?? new AislePilotPantryRankingEngine();
        _apiKey = configuration?["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _model = configuration?["AislePilot:Model"] ?? "gpt-4.1-mini";
        _imageModel = configuration?["AislePilot:ImageModel"] ?? "gpt-image-1";
        _enableAiGeneration = !bool.TryParse(configuration?["AislePilot:EnableAiGeneration"], out var parsed) || parsed;
        _enableAiImageGeneration = !bool.TryParse(
            configuration?["AislePilot:EnableAiImageGeneration"],
            out var parsedImageGeneration) || parsedImageGeneration;
        _allowTemplateFallback =
            bool.TryParse(configuration?["AislePilot:AllowTemplateFallback"], out var allowTemplateFallback)
                ? allowTemplateFallback
                : httpClient is null;
    }

    internal ILogger<AislePilotService>? Logger => _logger;

    internal IAislePilotPlanComparisonService PlanComparisonService => _planComparisonService;

    internal bool AllowTemplateFallback => _allowTemplateFallback;

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
