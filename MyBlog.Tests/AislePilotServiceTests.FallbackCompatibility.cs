using System.Text.RegularExpressions;
using MyBlog.Models;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    public static TheoryData<string[], string> TemplateDietaryCompatibilityCases => new()
    {
        { ["Vegan"], @"\b(chicken|turkey|beef|pork|lamb|bacon|ham|sausage|salmon|tuna|cod|prawn|egg|eggs|butter|cream|cheese|yogurt|yoghurt|paneer|halloumi|parmesan|mozzarella|honey)\b" },
        { ["Vegetarian"], @"\b(chicken|turkey|beef|pork|lamb|bacon|ham|sausage|salmon|tuna|cod|prawn)\b" },
        { ["Pescatarian"], @"\b(chicken|turkey|beef|pork|lamb|bacon|ham|sausage)\b" },
        { ["Gluten-Free"], @"\b(wheat|flour|bread|wrap|wraps|pasta|couscous|noodle|noodles|pastry|barley|rye|oat|oats|soy sauce)\b" },
        { ["Vegan", "Gluten-Free"], @"\b(chicken|turkey|beef|pork|lamb|bacon|ham|sausage|salmon|tuna|cod|prawn|egg|eggs|butter|cream|cheese|yogurt|yoghurt|paneer|halloumi|parmesan|mozzarella|honey|wheat|flour|bread|wrap|wraps|pasta|couscous|noodle|noodles|pastry|barley|rye|oat|oats|soy sauce)\b" }
    };

    [Theory]
    [MemberData(nameof(TemplateDietaryCompatibilityCases))]
    public void TemplateFallback_DietaryModes_DoNotReturnContradictoryIngredients(
        string[] dietaryModes,
        string forbiddenIngredientPattern)
    {
        ClearAiPool();
        var request = new AislePilotRequestModel
        {
            DietaryModes = dietaryModes.ToList(),
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 7,
            CookDays = 7
        };

        var result = _service.BuildPlan(request);

        Assert.Equal("AislePilot recipe plan", result.PlanSourceLabel);
        Assert.All(result.MealPlan, meal => Assert.DoesNotMatch(
            new Regex(forbiddenIngredientPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            NormalizePlantBasedIngredientNames(string.Join("; ", meal.IngredientLines))));
    }

    [Fact]
    public void TemplateFallback_DairyAllergen_ExcludesDairyFromMealsAndOptionalDessert()
    {
        ClearAiPool();
        var request = new AislePilotRequestModel
        {
            DietaryModes = ["Balanced"],
            DislikesOrAllergens = "dairy",
            WeeklyBudget = 80m,
            HouseholdSize = 2,
            PlanDays = 7,
            CookDays = 7,
            IncludeDessertAddOn = true
        };

        var result = _service.BuildPlan(request);
        var allIngredientText = string.Join(
            "; ",
            result.MealPlan.SelectMany(meal => meal.IngredientLines)
                .Concat(result.DessertAddOnIngredientLines));

        Assert.DoesNotMatch(
            new Regex(@"\b(milk|butter|cream|cheese|yogurt|yoghurt|paneer|halloumi|parmesan|mozzarella)\b", RegexOptions.IgnoreCase),
            NormalizePlantBasedIngredientNames(allIngredientText));
    }

    [Fact]
    public void TemplateFallback_TreeNutAllergen_DoesNotMistakeCoconutForTreeNut()
    {
        var names = GetCompatibleTemplateMealNamesForSlot(["Vegan"], "nut", "Dinner");

        Assert.Contains("Veggie lentil curry", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemplateFallback_GlutenFreeMode_RejectsOrdinarySoySauceButAllowsTamari()
    {
        var names = GetCompatibleTemplateMealNamesForSlot(["Vegan", "Gluten-Free"], null, "Dinner");

        Assert.DoesNotContain("Sesame tofu rice bowls", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Sticky sesame tofu rice bowl", names, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePlantBasedIngredientNames(string value)
    {
        return value
            .Replace("coconut milk", "coconut drink", StringComparison.OrdinalIgnoreCase)
            .Replace("coconut cream", "coconut puree", StringComparison.OrdinalIgnoreCase)
            .Replace("peanut butter", "peanut spread", StringComparison.OrdinalIgnoreCase);
    }
}
