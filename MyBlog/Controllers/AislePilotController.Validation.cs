using MyBlog.Models;

namespace MyBlog.Controllers;

public partial class AislePilotController
{
    private AislePilotPageViewModel BuildPageModel(
        AislePilotRequestModel request,
        AislePilotPlanResultViewModel? result = null,
        IReadOnlyList<AislePilotPantrySuggestionViewModel>? pantrySuggestions = null,
        string returnUrl = "",
        IReadOnlyList<AislePilotSavedWeekSummaryViewModel>? savedWeeks = null)
    {
        return new AislePilotPageViewModel
        {
            Request = request,
            ReturnUrl = returnUrl,
            Result = result,
            SavedWeeks = savedWeeks ?? BuildSavedWeekSummaries(TryReadSavedWeekState()),
            SavedMeals = ParseSavedEnjoyedMealNamesState(request.SavedEnjoyedMealNamesState),
            PantrySuggestions = pantrySuggestions ?? [],
            SupermarketOptions = aislePilotService.GetSupportedSupermarkets(),
            PortionSizeOptions = aislePilotService.GetSupportedPortionSizes(),
            DietaryOptions = aislePilotService.GetSupportedDietaryModes(),
            MealImagePollingEnabled = aislePilotService.CanGenerateMealImages()
        };
    }

    private void ValidateRequest(AislePilotRequestModel request)
    {
        ValidateSupportedSelections(request);

        if (request.IncludeSpecialTreatMeal &&
            !request.SelectedMealTypes.Contains("Dinner", StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(
                "Request.IncludeSpecialTreatMeal",
                "Special treat requires the Dinner meal slot.");
        }

        if (string.Equals(request.Supermarket, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            var customAisles = ParseCustomAisles(request.CustomAisleOrder);
            if (customAisles.Count < 3)
            {
                ModelState.AddModelError(
                    "Request.CustomAisleOrder",
                    "Add at least 3 comma-separated aisles when using Custom supermarket.");
            }
        }

        if (ModelState.ErrorCount == 0 && !aislePilotService.HasCompatibleMeals(request))
        {
            ModelState.AddModelError(
                "Request.DietaryModes",
                "No meals match your dietary modes and dislike/allergen notes. Remove one constraint and try again.");
        }
    }

    private void ValidateRequestForSuggestions(AislePilotRequestModel request)
    {
        ValidateSupportedSelections(request);

        if (string.IsNullOrWhiteSpace(request.PantryItems))
        {
            ModelState.AddModelError("Request.PantryItems", "Add a few pantry ingredients to get meal suggestions.");
        }
    }

    private void ValidateSupportedSelections(AislePilotRequestModel request)
    {
        var supermarkets = aislePilotService.GetSupportedSupermarkets();
        var portionSizes = aislePilotService.GetSupportedPortionSizes();
        var dietaryModes = aislePilotService.GetSupportedDietaryModes();

        if (!supermarkets.Contains(request.Supermarket, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Request.Supermarket", "Select a supported supermarket.");
        }

        if (!portionSizes.Contains(request.PortionSize, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Request.PortionSize", "Select a supported portion size.");
        }

        var unsupportedDietaryModes = request.DietaryModes
            .Where(mode => !dietaryModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (unsupportedDietaryModes.Count > 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "One or more dietary options were not recognised.");
        }

        if (request.DietaryModes.Count == 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "Choose at least one dietary mode.");
        }

        ValidateMealTypeSelection(request);
    }

    private void ValidateMealTypeSelection(AislePilotRequestModel request)
    {
        var selectedMealTypes = NormalizeSelectedMealTypes(request.SelectedMealTypes);
        request.SelectedMealTypes = selectedMealTypes;
        if (selectedMealTypes.Count > 0)
        {
            request.MealsPerDay = selectedMealTypes.Count;
        }

        if (selectedMealTypes.Count < MinMealsPerDay || selectedMealTypes.Count > MaxMealsPerDay)
        {
            ModelState.AddModelError(
                "Request.SelectedMealTypes",
                "Choose 1 to 3 meal slots (Breakfast, Lunch, Dinner).");
        }
    }

    private static IReadOnlyList<string> ParseCustomAisles(string? customAisleOrder)
    {
        return (customAisleOrder ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
