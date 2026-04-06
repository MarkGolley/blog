using MyBlog.Utilities;

namespace MyBlog.Models;

public sealed class AislePilotPageViewModel
{
    public AislePilotRequestModel Request { get; set; } = new();
    public string ReturnUrl { get; set; } = string.Empty;
    public AislePilotPlanResultViewModel? Result { get; set; }
    public IReadOnlyList<AislePilotSavedWeekSummaryViewModel> SavedWeeks { get; set; } =
        Array.Empty<AislePilotSavedWeekSummaryViewModel>();
    public IReadOnlyList<AislePilotPantrySuggestionViewModel> PantrySuggestions { get; set; } =
        Array.Empty<AislePilotPantrySuggestionViewModel>();
    public IReadOnlyList<string> SupermarketOptions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> PortionSizeOptions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DietaryOptions { get; set; } = Array.Empty<string>();
    public bool MealImagePollingEnabled { get; set; }
    public bool HasResult => Result is not null;
}

public sealed class AislePilotSavedWeekSummaryViewModel
{
    public string WeekId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTimeOffset SavedAtUtc { get; set; }
    public int PlanDays { get; set; }
    public int MealCount { get; set; }
    public string Supermarket { get; set; } = string.Empty;
}

public sealed class AislePilotPlanResultViewModel
{
    public string Supermarket { get; set; } = string.Empty;
    public string PortionSize { get; set; } = string.Empty;
    public IReadOnlyList<string> AppliedDietaryModes { get; set; } = Array.Empty<string>();
    public bool UsedAiGeneratedMeals { get; set; }
    public string PlanSourceLabel { get; set; } = string.Empty;
    public int PlanDays { get; set; } = 7;
    public int CookDays { get; set; } = 7;
    public int MealsPerDay { get; set; } = 1;
    public int LeftoverDays { get; set; }
    public decimal WeeklyBudget { get; set; }
    public decimal EstimatedTotalCost { get; set; }
    public decimal BudgetDelta { get; set; }
    public bool IsOverBudget { get; set; }
    public bool BudgetRebalanceAttempted { get; set; }
    public bool BudgetRebalanceReducedCost { get; set; }
    public string BudgetRebalanceStatusMessage { get; set; } = string.Empty;
    public bool IncludeSpecialTreatMeal { get; set; }
    public bool IncludeDessertAddOn { get; set; }
    public decimal DessertAddOnEstimatedCost { get; set; }
    public string DessertAddOnName { get; set; } = string.Empty;
    public IReadOnlyList<string> DessertAddOnIngredientLines { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> AisleOrderUsed { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> BudgetTips { get; set; } = Array.Empty<string>();
    public IReadOnlyList<AislePilotMealDayViewModel> MealPlan { get; set; } = Array.Empty<AislePilotMealDayViewModel>();
    public IReadOnlyList<AislePilotShoppingItemViewModel> ShoppingItems { get; set; } =
        Array.Empty<AislePilotShoppingItemViewModel>();
}

public sealed class AislePilotMealDayViewModel
{
    public string Day { get; set; } = string.Empty;
    public string MealType { get; set; } = "Dinner";
    public bool IsIgnored { get; set; }
    public string MealName { get; set; } = string.Empty;
    public bool IsSpecialTreat { get; set; }
    public string MealImageUrl { get; set; } = string.Empty;
    public string MealReason { get; set; } = string.Empty;
    public int LeftoverDaysCovered { get; set; }
    public decimal EstimatedCost { get; set; }
    public int EstimatedPrepMinutes { get; set; }
    public int CaloriesPerServing { get; set; }
    public decimal ProteinGramsPerServing { get; set; }
    public decimal CarbsGramsPerServing { get; set; }
    public decimal FatGramsPerServing { get; set; }
    public IReadOnlyList<string> IngredientLines { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RecipeSteps { get; set; } = Array.Empty<string>();
}

public sealed class AislePilotShoppingItemViewModel
{
    public string Department { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal EstimatedCost { get; set; }
    public string QuantityDisplay => QuantityDisplayFormatter.Format(Quantity, Unit);
}

public sealed class AislePilotPantrySuggestionViewModel
{
    public string MealName { get; set; } = string.Empty;
    public int MatchPercent { get; set; }
    public int MissingCoreIngredientCount { get; set; }
    public decimal MissingIngredientsEstimatedCost { get; set; }
    public bool CanCookNow { get; set; }
    public AislePilotMealDayViewModel? MealCard { get; set; }
    public IReadOnlyList<string> MatchedIngredients { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingIngredients { get; set; } = Array.Empty<string>();
}
