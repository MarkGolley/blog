namespace MyBlog.Services;

public sealed class AislePilotNutritionRecipeFallbackEngine
{
    internal IReadOnlyList<string> BuildRecipeSteps(AislePilotService.MealTemplate template)
    {
        return AislePilotService.BuildRecipeSteps(template);
    }

    internal AislePilotService.MealNutritionEstimate EstimateMealNutritionPerServing(
        AislePilotService.MealTemplate template,
        decimal portionSizeFactor)
    {
        return AislePilotService.EstimateMealNutritionPerServing(template, portionSizeFactor);
    }
}
