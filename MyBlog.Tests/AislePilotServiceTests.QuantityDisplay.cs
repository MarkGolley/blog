using MyBlog.Utilities;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    [Theory]
    [InlineData(0.38, "tbsp", "1 1/4 tsp")]
    [InlineData(0.2, "tablespoon", "1/2 tsp")]
    [InlineData(1.5, "tbsp", "1 1/2 tbsp")]
    [InlineData(2.2, "teaspoons", "2 1/4 tsp")]
    public void QuantityDisplayFormatter_ShoppingListFormatting_NormalizesDirectSpoonUnits(
        decimal quantity,
        string unit,
        string expected)
    {
        var formatted = QuantityDisplayFormatter.Format(quantity, unit);

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(0.38, "tbsp", "1 1/4 tsp")]
    [InlineData(0.2, "tablespoon", "1/2 tsp")]
    [InlineData(1.5, "tbsp", "1 1/2 tbsp")]
    [InlineData(2.2, "teaspoons", "2 1/4 tsp")]
    public void QuantityDisplayFormatter_RecipeFormatting_NormalizesDirectSpoonUnits(
        decimal quantity,
        string unit,
        string expected)
    {
        var formatted = QuantityDisplayFormatter.FormatForRecipe(quantity, unit);

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(0.38, "pinch", "a pinch of")]
    [InlineData(1.6, "pinches", "2 pinches of")]
    [InlineData(0.49, "dash", "a dash of")]
    public void QuantityDisplayFormatter_RecipeFormatting_NormalizesQualitativeSeasoningUnits(
        decimal quantity,
        string unit,
        string expected)
    {
        var formatted = QuantityDisplayFormatter.FormatForRecipe(quantity, unit);

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(5.63, "ml", "1 1/4 tsp")]
    [InlineData(16.88, "ml", "1 1/4 tbsp")]
    [InlineData(0.02, "l", "1 1/4 tbsp")]
    public void QuantityDisplayFormatter_ShoppingListFormatting_NormalizesLiquidVolumesToSpoonMeasures(
        decimal quantity,
        string unit,
        string expected)
    {
        var formatted = QuantityDisplayFormatter.Format(quantity, unit);

        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(18.75, "g", "20 g")]
    [InlineData(28.13, "grams", "30 g")]
    [InlineData(93.75, "g", "95 g")]
    [InlineData(318.75, "g", "320 g")]
    public void QuantityDisplayFormatter_ShoppingListFormatting_RoundsGramWeightsToShopperFriendlyAmounts(
        decimal quantity,
        string unit,
        string expected)
    {
        var formatted = QuantityDisplayFormatter.Format(quantity, unit);

        Assert.Equal(expected, formatted);
    }
}
