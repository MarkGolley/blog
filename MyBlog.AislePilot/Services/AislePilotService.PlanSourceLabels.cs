namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    internal const string CustomerBudgetTrimPlanSourceLabel = "Budget-friendly swaps";

    internal static string ToCustomerPlanSourceLabel(string? sourceLabel, bool usedAiGeneratedMeals)
    {
        if (string.IsNullOrWhiteSpace(sourceLabel))
        {
            return usedAiGeneratedMeals ? "Personalised meal plan" : "Your meal plan";
        }

        return sourceLabel.Trim().ToLowerInvariant() switch
        {
            "template fallback" => "AislePilot recipe plan",
            "ai meal pool" => "Personalised meal plan",
            "openai" => "Fresh personalised plan",
            "template + ai special treat" => "Personalised plan with a special treat",
            "ai/template mix" => "Personalised recipe mix",
            "ai/template mix (special treat pending)" => "Personalised recipe mix — special treat being prepared",
            "current plan" => "Updated meal plan",
            "current plan + template top-up" => "Updated meal plan",
            "template swap" => "Updated meal choice",
            "openai swap" => "Fresh meal suggestion",
            "budget trim swaps" => CustomerBudgetTrimPlanSourceLabel,
            "budget floor" or "budget rebalance" => "Budget-friendly meal plan",
            _ => "Your meal plan"
        };
    }
}
