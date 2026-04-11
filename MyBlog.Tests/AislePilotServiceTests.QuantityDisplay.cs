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
}
