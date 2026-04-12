using System.Globalization;
using System.Text;
using MyBlog.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MyBlog.Services;

public sealed class AislePilotExportService : IAislePilotExportService
{
    private const string MealIngredientEstimateLabel = "Meal ingredient estimate";
    private const string ShoppingEstimateDisclaimer = "This estimate covers the ingredients used in these meals. Actual checkout can be higher if shops only sell larger packs or bags.";
    private const string AislePilotMarkSvg = """
        <svg width="128" height="128" viewBox="0 0 512 512" xmlns="http://www.w3.org/2000/svg">
          <g fill="none" stroke="#103F65" stroke-width="24" stroke-linecap="round" stroke-linejoin="round">
            <path d="M120 180 H320 L360 300 H160 Z" />
            <circle cx="200" cy="360" r="20" fill="#103F65" />
            <circle cx="320" cy="360" r="20" fill="#103F65" />
          </g>
          <rect x="98" y="220" width="90" height="12" rx="6" fill="#0F6D78" />
          <rect x="86" y="250" width="108" height="12" rx="6" fill="#0F6D78" />
          <rect x="170" y="220" width="120" height="12" rx="6" fill="#0F6D78" />
          <rect x="180" y="250" width="100" height="12" rx="6" fill="#0F6D78" />
          <path d="M260 260 L360 220 L360 300 Z" fill="#E39C41" />
        </svg>
        """;
    private const string AislePilotMarkSvgDark = """
        <svg width="128" height="128" viewBox="0 0 512 512" xmlns="http://www.w3.org/2000/svg">
          <g fill="none" stroke="#A8DAFF" stroke-width="24" stroke-linecap="round" stroke-linejoin="round">
            <path d="M120 180 H320 L360 300 H160 Z" />
            <circle cx="200" cy="360" r="20" fill="#A8DAFF" />
            <circle cx="320" cy="360" r="20" fill="#A8DAFF" />
          </g>
          <rect x="98" y="220" width="90" height="12" rx="6" fill="#78D0CC" />
          <rect x="86" y="250" width="108" height="12" rx="6" fill="#78D0CC" />
          <rect x="170" y="220" width="120" height="12" rx="6" fill="#78D0CC" />
          <rect x="180" y="250" width="100" height="12" rx="6" fill="#78D0CC" />
          <path d="M260 260 L360 220 L360 300 Z" fill="#F6BE77" />
        </svg>
        """;

    public AislePilotExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] BuildPlanPackPdf(
        AislePilotRequestModel request,
        AislePilotPlanResultViewModel result,
        bool useDarkTheme)
    {
        var ukCulture = CultureInfo.GetCultureInfo("en-GB");
        var ink = useDarkTheme ? "#E7EFFD" : "#142033";
        var inkSoft = useDarkTheme ? "#B7C7DE" : "#45556D";
        var brandDeep = useDarkTheme ? "#9ED4FF" : "#103F65";
        var panel = useDarkTheme ? "#131D2A" : "#FFFFFF";
        var panelSoft = useDarkTheme ? "#1A2738" : "#F0F6FF";
        var line = useDarkTheme ? "#2B3D55" : "#D8E3EF";
        var lineStrong = useDarkTheme ? "#35506D" : "#C9D8E8";
        var ok = useDarkTheme ? "#8AE5BB" : "#166247";
        var okSoft = useDarkTheme ? "#1B3A2D" : "#EAF7EF";
        var danger = useDarkTheme ? "#FF9E98" : "#92261F";
        var dangerSoft = useDarkTheme ? "#3A2125" : "#FCEDEC";
        var pageSurface = useDarkTheme ? "#0B1320" : "#FFFFFF";
        var markSvg = useDarkTheme ? AislePilotMarkSvgDark : AislePilotMarkSvg;

        var generatedAt = DateTime.Now;
        var dietaryModesText = result.AppliedDietaryModes.Count == 0
            ? "None selected"
            : string.Join(", ", result.AppliedDietaryModes);
        var budgetStatusText = result.IsOverBudget
            ? $"Over budget by {Math.Abs(result.BudgetDelta).ToString("C", ukCulture)}"
            : result.BudgetDelta < 0
                ? $"Under budget by {Math.Abs(result.BudgetDelta).ToString("C", ukCulture)}"
                : "On budget";
        var budgetStatusColor = result.IsOverBudget ? danger : ok;
        var budgetStatusBackground = result.IsOverBudget ? dangerSoft : okSoft;

        var aisleRank = result.AisleOrderUsed
            .Select((department, index) => new { department, index })
            .ToDictionary(x => x.department, x => x.index, StringComparer.OrdinalIgnoreCase);

        var groupedItems = result.ShoppingItems
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Department) ? "Other" : item.Department)
            .Select(group => new
            {
                Department = group.Key,
                Items = group.OrderBy(item => item.Name).ToList(),
                Total = group.Sum(item => item.EstimatedCost)
            })
            .OrderBy(group => aisleRank.GetValueOrDefault(group.Department, int.MaxValue))
            .ThenBy(group => group.Department)
            .ToList();

        var overviewRows = new List<(string Label, string Value)>
        {
            ("Supermarket", result.Supermarket),
            ("Portion size", result.PortionSize),
            ("Household size", request.HouseholdSize.ToString(ukCulture)),
            ("Plan length", result.PlanDays.ToString(ukCulture)),
            ("Cook days", result.CookDays.ToString(ukCulture)),
            ("Meals per day", result.MealsPerDay.ToString(ukCulture)),
            ("Leftover days", result.LeftoverDays.ToString(ukCulture)),
            ("Dietary requirements", dietaryModesText),
            ("Weekly budget", result.WeeklyBudget.ToString("C", ukCulture)),
            (MealIngredientEstimateLabel, result.EstimatedTotalCost.ToString("C", ukCulture)),
            ("Budget status", budgetStatusText)
        };

        if (!string.IsNullOrWhiteSpace(request.DislikesOrAllergens))
        {
            overviewRows.Add(("Dislikes/allergens", request.DislikesOrAllergens));
        }

        var leftOverviewRows = overviewRows
            .Where((_, index) => index % 2 == 0)
            .ToList();
        var rightOverviewRows = overviewRows
            .Where((_, index) => index % 2 == 1)
            .ToList();

        return Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.PageColor(pageSurface);
                    page.MarginHorizontal(26);
                    page.MarginVertical(22);
                    page.DefaultTextStyle(style => style.FontSize(10).FontColor(ink));

                    page.Header().Column(header =>
                    {
                        header.Spacing(0);
                        header.Item()
                            .Border(0.7f)
                            .BorderColor(lineStrong)
                            .Background(panelSoft)
                            .PaddingHorizontal(10)
                            .PaddingVertical(7)
                            .Row(row =>
                            {
                                row.Spacing(6);
                                row.AutoItem()
                                    .Width(34)
                                    .Height(34)
                                    .AlignMiddle()
                                    .Svg(markSvg);

                                row.RelativeItem().Column(column =>
                                {
                                    column.Spacing(1);
                                    column.Item().Text("Aisle Pilot").FontSize(15.5f).SemiBold().FontColor(brandDeep);
                                    column.Item().Text("Weekly meal plan, aisle-sorted shopping, and practical recipes")
                                        .FontSize(8.2f)
                                        .FontColor(inkSoft);
                                });

                                row.AutoItem().AlignMiddle().Border(0.7f).BorderColor(line).Background(panel)
                                    .PaddingVertical(4).PaddingHorizontal(6).Column(meta =>
                                    {
                                        meta.Spacing(1);
                                        meta.Item().Text(generatedAt.ToString("dd MMM yyyy, HH:mm", ukCulture)).FontSize(8.1f)
                                            .SemiBold().FontColor(brandDeep);
                                    });
                            });
                    });

                    page.Content().PaddingTop(10).Column(content =>
                    {
                        content.Spacing(10);

                        content.Item().Section("toc").Column(toc =>
                        {
                            toc.Spacing(4);
                            toc.Item().Text("Quick links").FontSize(11.5f).SemiBold().FontColor(brandDeep);
                            toc.Item().Row(row =>
                            {
                                row.Spacing(9);
                                row.AutoItem().SectionLink("overview").Text("Overview").FontSize(9.5f).SemiBold().FontColor(brandDeep);
                                row.AutoItem().Text("|").FontSize(9f).FontColor(lineStrong);
                                row.AutoItem().SectionLink("shopping").Text("Shopping list").FontSize(9.5f).SemiBold().FontColor(brandDeep);
                                row.AutoItem().Text("|").FontSize(9f).FontColor(lineStrong);
                                row.AutoItem().SectionLink("meals").Text("Meals and recipes").FontSize(9.5f).SemiBold().FontColor(brandDeep);
                            });
                        });

                        content.Item().Section("overview").Border(0.7f).BorderColor(line).Background(panel).Padding(11).Column(section =>
                        {
                            section.Spacing(7);
                            section.Item().Text("Plan overview").FontSize(13.5f).SemiBold().FontColor(brandDeep);

                            section.Item().Row(row =>
                            {
                                row.Spacing(7);
                                row.RelativeItem().Border(0.7f).BorderColor(line).Background(panelSoft).Padding(8).Column(metric =>
                                {
                                    metric.Spacing(2);
                                    metric.Item().Text("Weekly budget").FontSize(8.5f).SemiBold().FontColor(inkSoft);
                                    metric.Item().Text(result.WeeklyBudget.ToString("C", ukCulture)).FontSize(12).SemiBold().FontColor(brandDeep);
                                });
                                row.RelativeItem().Border(0.7f).BorderColor(line).Background(panelSoft).Padding(8).Column(metric =>
                                {
                                    metric.Spacing(2);
                                    metric.Item().Text(MealIngredientEstimateLabel).FontSize(8.5f).SemiBold().FontColor(inkSoft);
                                    metric.Item().Text(result.EstimatedTotalCost.ToString("C", ukCulture)).FontSize(12).SemiBold().FontColor(brandDeep);
                                });
                                row.RelativeItem().Border(0.7f).BorderColor(line).Background(budgetStatusBackground).Padding(8).Column(metric =>
                                {
                                    metric.Spacing(2);
                                    metric.Item().Text("Budget status").FontSize(8.5f).SemiBold().FontColor(inkSoft);
                                    metric.Item().Text(budgetStatusText).FontSize(12).SemiBold().FontColor(budgetStatusColor);
                                });
                            });

                            section.Item().Row(grid =>
                            {
                                grid.Spacing(10);

                                grid.RelativeItem().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.ConstantColumn(112);
                                        columns.RelativeColumn();
                                    });

                                    foreach (var row in leftOverviewRows)
                                    {
                                        table.Cell()
                                            .BorderBottom(0.6f)
                                            .BorderColor(line)
                                            .PaddingVertical(4)
                                            .PaddingRight(8)
                                            .Text(row.Label)
                                            .FontSize(9f)
                                            .SemiBold()
                                            .FontColor(inkSoft);

                                        table.Cell()
                                            .BorderBottom(0.6f)
                                            .BorderColor(line)
                                            .PaddingVertical(4)
                                            .Text(row.Value)
                                            .FontSize(9.5f)
                                            .FontColor(ink);
                                    }
                                });

                                grid.RelativeItem().Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.ConstantColumn(112);
                                        columns.RelativeColumn();
                                    });

                                    foreach (var row in rightOverviewRows)
                                    {
                                        table.Cell()
                                            .BorderBottom(0.6f)
                                            .BorderColor(line)
                                            .PaddingVertical(4)
                                            .PaddingRight(8)
                                            .Text(row.Label)
                                            .FontSize(9f)
                                            .SemiBold()
                                            .FontColor(inkSoft);

                                        table.Cell()
                                            .BorderBottom(0.6f)
                                            .BorderColor(line)
                                            .PaddingVertical(4)
                                            .Text(row.Value)
                                            .FontSize(9.5f)
                                            .FontColor(ink);
                                    }
                                });
                            });
                        });

                        content.Item().Section("shopping").Border(0.7f).BorderColor(line).Background(panel).Padding(11).Column(section =>
                        {
                            section.Spacing(7);
                            section.Item().Text("Shopping list").FontSize(13.5f).SemiBold().FontColor(brandDeep);
                            section.Item().Text(ShoppingEstimateDisclaimer).FontSize(8.8f).FontColor(inkSoft);

                            section.Item().MultiColumn(columns =>
                            {
                                columns.Columns(2);
                                columns.Spacing(10);
                                columns.BalanceHeight();

                                columns.Content().Column(list =>
                                {
                                    list.Spacing(7);

                                    foreach (var department in groupedItems)
                                    {
                                        list.Item().Border(0.7f).BorderColor(line).Background(panelSoft).Padding(8).Column(group =>
                                        {
                                            group.Spacing(5);
                                            group.Item().Row(row =>
                                            {
                                                row.RelativeItem().Text(department.Department).FontSize(10.5f).SemiBold().FontColor(brandDeep);
                                                row.AutoItem().Text($"Est. {department.Total.ToString("C", ukCulture)}").FontSize(9.5f).SemiBold().FontColor(inkSoft);
                                            });

                                            group.Item().Table(table =>
                                            {
                                                table.ColumnsDefinition(columns =>
                                                {
                                                    columns.RelativeColumn(6);
                                                    columns.RelativeColumn(2);
                                                    columns.RelativeColumn(2);
                                                });

                                                table.Header(header =>
                                                {
                                                    header.Cell()
                                                        .BorderBottom(0.7f)
                                                        .BorderColor(lineStrong)
                                                        .PaddingBottom(4)
                                                        .Text("Item")
                                                        .FontSize(8.5f)
                                                        .SemiBold()
                                                        .FontColor(inkSoft);
                                                    header.Cell()
                                                        .BorderBottom(0.7f)
                                                        .BorderColor(lineStrong)
                                                        .PaddingBottom(4)
                                                        .AlignRight()
                                                        .Text("Qty")
                                                        .FontSize(8.5f)
                                                        .SemiBold()
                                                        .FontColor(inkSoft);
                                                    header.Cell()
                                                        .BorderBottom(0.7f)
                                                        .BorderColor(lineStrong)
                                                        .PaddingBottom(4)
                                                        .AlignRight()
                                                        .Text("Est.")
                                                        .FontSize(8.5f)
                                                        .SemiBold()
                                                        .FontColor(inkSoft);
                                                });

                                                foreach (var item in department.Items)
                                                {
                                                    table.Cell().PaddingVertical(4)
                                                        .Text($"[ ] {item.Name}")
                                                        .FontSize(9.2f);
                                                    table.Cell().PaddingVertical(4).AlignRight()
                                                        .Text(item.QuantityDisplay)
                                                        .FontSize(9.2f)
                                                        .FontColor(inkSoft);
                                                    table.Cell().PaddingVertical(4).AlignRight()
                                                        .Text(item.EstimatedCost.ToString("C", ukCulture))
                                                        .FontSize(9.2f)
                                                        .SemiBold();
                                                }
                                            });
                                        });
                                    }
                                });
                            });

                            section.Item().PaddingTop(2).AlignRight().Text(text =>
                            {
                                text.Span($"{MealIngredientEstimateLabel}: ").SemiBold().FontColor(inkSoft);
                                text.Span(result.EstimatedTotalCost.ToString("C", ukCulture)).SemiBold().FontColor(brandDeep);
                            });
                        });

                        content.Item().Section("meals").Column(section =>
                        {
                            section.Spacing(8);
                            section.Item().Text("Weekly meals and recipes").FontSize(14).SemiBold().FontColor(brandDeep);

                            foreach (var meal in result.MealPlan)
                            {
                                section.Item().Border(0.7f).BorderColor(line).Background(panel).Padding(9).Column(card =>
                                {
                                    card.Spacing(5);
                                    var mealHeading = meal.IsIgnored
                                        ? $"{meal.Day} - {meal.MealType}: {meal.MealName} (Ignored)"
                                        : $"{meal.Day} - {meal.MealType}: {meal.MealName}";
                                    card.Item().Text(mealHeading).FontSize(11.5f).SemiBold().FontColor(brandDeep);

                                    card.Item().Row(meta =>
                                    {
                                        meta.Spacing(6);
                                        meta.AutoItem().Background(panelSoft).PaddingVertical(2).PaddingHorizontal(6)
                                            .Text($"Cost {meal.EstimatedCost.ToString("C", ukCulture)}").FontSize(8.5f).SemiBold().FontColor(inkSoft);
                                        meta.AutoItem().Background(panelSoft).PaddingVertical(2).PaddingHorizontal(6)
                                            .Text($"{meal.EstimatedPrepMinutes} mins").FontSize(8.5f).SemiBold().FontColor(inkSoft);

                                        if (meal.LeftoverDaysCovered > 0)
                                        {
                                            meta.AutoItem().Background(okSoft).PaddingVertical(2).PaddingHorizontal(6)
                                                .Text($"Covers {meal.LeftoverDaysCovered} leftover day(s)").FontSize(8.5f).SemiBold().FontColor(ok);
                                        }
                                    });

                                    card.Item().Text(meal.MealReason).FontColor(inkSoft);

                                    card.Item().Row(row =>
                                    {
                                        row.Spacing(8);

                                        row.RelativeItem().Background(panelSoft).Padding(7).Column(block =>
                                        {
                                            block.Spacing(3);
                                            block.Item().Text("Ingredients").FontSize(9f).SemiBold().FontColor(brandDeep);

                                            foreach (var ingredientLine in meal.IngredientLines)
                                            {
                                                block.Item().Text($"- {ingredientLine}").FontSize(9f);
                                            }
                                        });

                                        row.RelativeItem().Background(panelSoft).Padding(7).Column(block =>
                                        {
                                            block.Spacing(3);
                                            block.Item().Text("Method").FontSize(9f).SemiBold().FontColor(brandDeep);

                                            for (var i = 0; i < meal.RecipeSteps.Count; i++)
                                            {
                                                block.Item().Text($"{i + 1}. {meal.RecipeSteps[i]}").FontSize(9f);
                                            }
                                        });
                                    });
                                });
                            }
                        });
                    });

                    page.Footer().Column(footer =>
                    {
                        footer.Item().LineHorizontal(1).LineColor(line);
                        footer.Item().PaddingTop(4).Row(row =>
                        {
                            row.Spacing(6);
                            row.AutoItem()
                                .Width(11)
                                .Height(11)
                                .AlignMiddle()
                                .Svg(markSvg);

                            row.RelativeItem().DefaultTextStyle(style => style.FontSize(8.5f)).Text(text =>
                            {
                                text.Span("Aisle Pilot").SemiBold().FontColor(brandDeep);
                                text.Span(" | ").FontColor(inkSoft);
                                text.Span(result.Supermarket).FontColor(inkSoft);
                            });

                            row.AutoItem().DefaultTextStyle(style => style.FontSize(8.5f).FontColor(inkSoft)).Text(text =>
                            {
                                text.Span("Page ");
                                text.CurrentPageNumber();
                                text.Span(" / ");
                                text.TotalPages();
                            });
                        });
                    });
                });
            })
            .GeneratePdf();
    }

    public string BuildChecklistText(AislePilotPlanResultViewModel result)
    {
        var ukCulture = CultureInfo.GetCultureInfo("en-GB");
        var builder = new StringBuilder();
        builder.AppendLine("AislePilot Shopping Checklist");
        builder.AppendLine($"Supermarket: {result.Supermarket}");
        builder.AppendLine($"Portion size: {result.PortionSize}");
        builder.AppendLine($"{MealIngredientEstimateLabel}: {result.EstimatedTotalCost.ToString("C", ukCulture)}");
        builder.AppendLine(ShoppingEstimateDisclaimer);
        builder.AppendLine();
        builder.AppendLine($"Aisle order: {string.Join(" -> ", result.AisleOrderUsed)}");

        foreach (var department in result.ShoppingItems.GroupBy(item => item.Department))
        {
            builder.AppendLine();
            builder.AppendLine($"[{department.Key}]");
            foreach (var item in department)
            {
                builder.AppendLine($"[ ] {item.Name} - {item.QuantityDisplay} - Est. {item.EstimatedCost.ToString("C", ukCulture)}");
            }
        }

        return builder.ToString();
    }
}
