using System.Reflection;
using MyBlog.Services;

namespace MyBlog.Tests;

public partial class AislePilotServiceTests
{
    [Theory]
    [InlineData("Template fallback", false, "AislePilot recipe plan")]
    [InlineData("AI meal pool", true, "Personalised meal plan")]
    [InlineData("OpenAI", true, "Fresh personalised plan")]
    [InlineData("Template + AI special treat", true, "Personalised plan with a special treat")]
    [InlineData("AI/template mix", true, "Personalised recipe mix")]
    [InlineData("AI/template mix (special treat pending)", true, "Personalised recipe mix — special treat being prepared")]
    [InlineData("Current plan", false, "Updated meal plan")]
    [InlineData("Current plan + template top-up", false, "Updated meal plan")]
    [InlineData("Template swap", false, "Updated meal choice")]
    [InlineData("OpenAI swap", true, "Fresh meal suggestion")]
    [InlineData("Budget trim swaps", false, "Budget-friendly swaps")]
    [InlineData("Budget floor", false, "Budget-friendly meal plan")]
    [InlineData(null, true, "Personalised meal plan")]
    [InlineData(null, false, "Your meal plan")]
    public void PlanSourceLabel_InternalSources_AreConvertedToPlainCustomerLanguage(
        string? internalSource,
        bool usedAiGeneratedMeals,
        string expected)
    {
        var formatter = typeof(AislePilotService).GetMethod(
            "ToCustomerPlanSourceLabel",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(formatter);

        var label = Assert.IsType<string>(formatter!.Invoke(null, [internalSource, usedAiGeneratedMeals]));

        Assert.Equal(expected, label);
        Assert.DoesNotContain("fallback", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("template", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenAI", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AI pool", label, StringComparison.OrdinalIgnoreCase);
    }
}
