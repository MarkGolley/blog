using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MyBlog.Models;
using MyBlog.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MyBlog.Controllers;

[Route("projects/aisle-pilot")]
public class AislePilotController(IAislePilotService aislePilotService) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        var request = new AislePilotRequestModel();
        return View(BuildPageModel(request));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public IActionResult Index(AislePilotPageViewModel pageModel)
    {
        var request = NormalizeRequest(pageModel.Request);
        ValidateRequest(request);

        if (!ModelState.IsValid)
        {
            return View(BuildPageModel(request));
        }

        var result = aislePilotService.BuildPlan(request);
        return View(BuildPageModel(request, result));
    }

    [HttpPost("suggest-from-pantry")]
    [ValidateAntiForgeryToken]
    public IActionResult SuggestFromPantry(AislePilotPageViewModel pageModel)
    {
        var request = NormalizeRequest(pageModel.Request);
        ValidateRequestForSuggestions(request);

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request));
        }

        var suggestions = aislePilotService.SuggestMealsFromPantry(request, 6);
        if (suggestions.Count == 0)
        {
            ModelState.AddModelError("Request.PantryItems", "No full meals found from your current pantry items. Add more ingredients or generate a full weekly plan.");
            return View("Index", BuildPageModel(request));
        }

        return View("Index", BuildPageModel(request, pantrySuggestions: suggestions));
    }

    [HttpPost("swap-meal")]
    [ValidateAntiForgeryToken]
    public IActionResult SwapMeal(AislePilotPageViewModel pageModel, int dayIndex, string? currentMealName)
    {
        var request = NormalizeRequest(pageModel.Request);
        ValidateRequest(request);
        var cookDays = Math.Clamp(request.CookDays, 1, 7);

        if (dayIndex < 0 || dayIndex >= cookDays)
        {
            ModelState.AddModelError(string.Empty, "Selected day was out of range. Try generating again.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request));
        }

        var result = aislePilotService.SwapMealForDay(request, dayIndex, currentMealName);
        return View("Index", BuildPageModel(request, result));
    }

    [HttpPost("export/plan-pack")]
    [ValidateAntiForgeryToken]
    public IActionResult ExportPlanPack(AislePilotPageViewModel pageModel)
    {
        var request = NormalizeRequest(pageModel.Request);
        ValidateRequest(request);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = aislePilotService.BuildPlan(request);
        var bytes = BuildPlanPackPdf(request, result);
        var fileName = $"aislepilot-plan-pack-{DateTime.UtcNow:yyyyMMdd}.pdf";
        return File(bytes, "application/pdf", fileName);
    }

    [HttpPost("export/checklist")]
    [ValidateAntiForgeryToken]
    public IActionResult ExportChecklist(AislePilotPageViewModel pageModel)
    {
        var request = NormalizeRequest(pageModel.Request);
        ValidateRequest(request);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var result = aislePilotService.BuildPlan(request);
        var content = BuildChecklistText(result);
        var bytes = Encoding.UTF8.GetBytes(content);
        var fileName = $"aislepilot-checklist-{DateTime.UtcNow:yyyyMMdd}.txt";
        return File(bytes, "text/plain; charset=utf-8", fileName);
    }

    private AislePilotPageViewModel BuildPageModel(
        AislePilotRequestModel request,
        AislePilotPlanResultViewModel? result = null,
        IReadOnlyList<AislePilotPantrySuggestionViewModel>? pantrySuggestions = null)
    {
        return new AislePilotPageViewModel
        {
            Request = request,
            Result = result,
            PantrySuggestions = pantrySuggestions ?? [],
            SupermarketOptions = aislePilotService.GetSupportedSupermarkets(),
            DietaryOptions = aislePilotService.GetSupportedDietaryModes()
        };
    }

    private AislePilotRequestModel NormalizeRequest(AislePilotRequestModel? request)
    {
        var normalized = request ?? new AislePilotRequestModel();
        normalized.Supermarket = normalized.Supermarket?.Trim() ?? string.Empty;
        normalized.CustomAisleOrder = normalized.CustomAisleOrder?.Trim() ?? string.Empty;
        normalized.DislikesOrAllergens = normalized.DislikesOrAllergens?.Trim() ?? string.Empty;
        normalized.PantryItems = normalized.PantryItems?.Trim() ?? string.Empty;
        normalized.LeftoverCookDayIndexesCsv = normalized.LeftoverCookDayIndexesCsv?.Trim() ?? string.Empty;
        normalized.DietaryModes = normalized.DietaryModes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        return normalized;
    }

    private void ValidateRequest(AislePilotRequestModel request)
    {
        var supermarkets = aislePilotService.GetSupportedSupermarkets();
        var dietaryModes = aislePilotService.GetSupportedDietaryModes();

        if (!supermarkets.Contains(request.Supermarket, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Request.Supermarket", "Select a supported supermarket.");
        }

        var unsupportedDietaryModes = request.DietaryModes
            .Where(x => !dietaryModes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unsupportedDietaryModes.Count > 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "One or more dietary options were not recognised.");
        }

        if (request.DietaryModes.Count == 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "Choose at least one dietary mode.");
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
        var supermarkets = aislePilotService.GetSupportedSupermarkets();
        var dietaryModes = aislePilotService.GetSupportedDietaryModes();

        if (!supermarkets.Contains(request.Supermarket, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Request.Supermarket", "Select a supported supermarket.");
        }

        var unsupportedDietaryModes = request.DietaryModes
            .Where(x => !dietaryModes.Contains(x, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unsupportedDietaryModes.Count > 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "One or more dietary options were not recognised.");
        }

        if (request.DietaryModes.Count == 0)
        {
            ModelState.AddModelError("Request.DietaryModes", "Choose at least one dietary mode.");
        }

        if (string.IsNullOrWhiteSpace(request.PantryItems))
        {
            ModelState.AddModelError("Request.PantryItems", "Add a few pantry ingredients to get meal suggestions.");
        }
    }

    private static byte[] BuildPlanPackPdf(
        AislePilotRequestModel request,
        AislePilotPlanResultViewModel result)
    {
        var ukCulture = CultureInfo.GetCultureInfo("en-GB");
        const string ink = "#142033";
        const string inkSoft = "#45556D";
        const string white = "#FFFFFF";
        const string brand = "#0F6D78";
        const string brandDeep = "#103F65";
        const string panel = "#FFFFFF";
        const string panelSoft = "#F0F6FF";
        const string line = "#C8D7E8";
        const string lineStrong = "#AFC3DA";
        const string ok = "#166247";
        const string okSoft = "#EAF7EF";
        const string danger = "#92261F";
        const string dangerSoft = "#FCEDEC";

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
        var totalShoppingItems = result.ShoppingItems.Count;

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
            ("Household size", request.HouseholdSize.ToString(ukCulture)),
            ("Cook days", result.CookDays.ToString(ukCulture)),
            ("Leftover days", result.LeftoverDays.ToString(ukCulture)),
            ("Dietary requirements", dietaryModesText),
            ("Weekly budget", result.WeeklyBudget.ToString("C", ukCulture)),
            ("Estimated total", result.EstimatedTotalCost.ToString("C", ukCulture)),
            ("Budget status", budgetStatusText)
        };

        if (!string.IsNullOrWhiteSpace(request.DislikesOrAllergens))
        {
            overviewRows.Add(("Dislikes/allergens", request.DislikesOrAllergens));
        }

        return Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal(26);
                    page.MarginVertical(22);
                    page.DefaultTextStyle(style => style.FontSize(10).FontColor(ink));

                    page.Header().Column(header =>
                    {
                        header.Spacing(0);
                        header.Item()
                            .Border(1)
                            .BorderColor(lineStrong)
                            .Background(panelSoft)
                            .Padding(14)
                            .Row(row =>
                            {
                                row.RelativeItem().Column(column =>
                                {
                                    column.Spacing(3);
                                    column.Item()
                                        .Border(1)
                                        .BorderColor(line)
                                        .Background(panel)
                                        .PaddingVertical(2)
                                        .PaddingHorizontal(7)
                                        .AlignLeft()
                                        .Text("AislePilot Export")
                                        .FontSize(8)
                                        .SemiBold()
                                        .FontColor(brandDeep);

                                    column.Item().Text("AislePilot Plan Pack").FontSize(20).SemiBold().FontColor(brandDeep);
                                    column.Item().Text("Weekly meal plan, aisle-sorted shopping, and practical recipes")
                                        .FontSize(9.5f)
                                        .FontColor(inkSoft);
                                });

                                row.AutoItem().AlignMiddle().Border(1).BorderColor(line).Background(panel).Padding(8).Column(meta =>
                                    {
                                        meta.Spacing(2);
                                        meta.Item().Text("Generated").FontSize(8).SemiBold().FontColor(inkSoft);
                                        meta.Item().Text(generatedAt.ToString("dd MMM yyyy, HH:mm", ukCulture)).FontSize(9).SemiBold().FontColor(brandDeep);
                                    });
                            });

                        header.Item()
                            .BorderLeft(1)
                            .BorderRight(1)
                            .BorderBottom(1)
                            .BorderColor(lineStrong)
                            .Background(panelSoft)
                            .PaddingVertical(6)
                            .PaddingHorizontal(10)
                            .Row(row =>
                            {
                                row.Spacing(6);
                                row.AutoItem()
                                    .Border(1)
                                    .BorderColor(line)
                                    .Background("#E8F3F4")
                                    .PaddingVertical(3)
                                    .PaddingHorizontal(8)
                                    .Text($"Store: {result.Supermarket}")
                                    .FontSize(8.5f)
                                    .SemiBold()
                                    .FontColor(brand);

                                row.AutoItem()
                                    .Border(1)
                                    .BorderColor(line)
                                    .Background("#EAF0F8")
                                    .PaddingVertical(3)
                                    .PaddingHorizontal(8)
                                    .Text($"Cook days: {result.CookDays}")
                                    .FontSize(8.5f)
                                    .SemiBold()
                                    .FontColor(brandDeep);

                                row.AutoItem()
                                    .Border(1)
                                    .BorderColor(line)
                                    .Background(panel)
                                    .PaddingVertical(3)
                                    .PaddingHorizontal(8)
                                    .Text($"Items: {totalShoppingItems}")
                                    .FontSize(8.5f)
                                    .SemiBold()
                                    .FontColor(brandDeep);

                                row.AutoItem()
                                    .Border(1)
                                    .BorderColor(line)
                                    .Background(budgetStatusBackground)
                                    .PaddingVertical(3)
                                    .PaddingHorizontal(8)
                                    .Text(budgetStatusText)
                                    .FontSize(8.5f)
                                    .SemiBold()
                                    .FontColor(budgetStatusColor);
                            });
                    });

                    page.Content().PaddingTop(10).Column(content =>
                    {
                        content.Spacing(10);

                        content.Item().Section("toc").Column(toc =>
                        {
                            toc.Spacing(5);
                            toc.Item().Text("Quick links").FontSize(11.5f).SemiBold().FontColor(brandDeep);
                            toc.Item().Row(row =>
                            {
                                row.Spacing(6);
                                row.AutoItem().Border(1).BorderColor(brandDeep).Background(brandDeep).PaddingVertical(4).PaddingHorizontal(10)
                                    .SectionLink("overview").Text("Overview").FontSize(9.5f).SemiBold().FontColor(white);
                                row.AutoItem().Border(1).BorderColor(brandDeep).Background(brandDeep).PaddingVertical(4).PaddingHorizontal(10)
                                    .SectionLink("shopping").Text("Shopping list").FontSize(9.5f).SemiBold().FontColor(white);
                                row.AutoItem().Border(1).BorderColor(brandDeep).Background(brandDeep).PaddingVertical(4).PaddingHorizontal(10)
                                    .SectionLink("meals").Text("Meals and recipes").FontSize(9.5f).SemiBold().FontColor(white);
                                row.AutoItem().Border(1).BorderColor(brandDeep).Background(brandDeep).PaddingVertical(4).PaddingHorizontal(10)
                                    .SectionLink("budget-notes").Text("Budget notes").FontSize(9.5f).SemiBold().FontColor(white);
                            });
                        });

                        content.Item().Section("overview").Border(1).BorderColor(line).Background(panel).Padding(11).Column(section =>
                        {
                            section.Spacing(7);
                            section.Item().Text("Plan overview").FontSize(13.5f).SemiBold().FontColor(brandDeep);

                            section.Item().Row(row =>
                            {
                                row.Spacing(7);
                                row.RelativeItem().Border(1).BorderColor(line).Background(panelSoft).Padding(8).Column(metric =>
                                {
                                    metric.Spacing(2);
                                    metric.Item().Text("Weekly budget").FontSize(8.5f).SemiBold().FontColor(inkSoft);
                                    metric.Item().Text(result.WeeklyBudget.ToString("C", ukCulture)).FontSize(12).SemiBold().FontColor(brandDeep);
                                });
                                row.RelativeItem().Border(1).BorderColor(line).Background(panelSoft).Padding(8).Column(metric =>
                                {
                                    metric.Spacing(2);
                                    metric.Item().Text("Estimated total").FontSize(8.5f).SemiBold().FontColor(inkSoft);
                                    metric.Item().Text(result.EstimatedTotalCost.ToString("C", ukCulture)).FontSize(12).SemiBold().FontColor(brandDeep);
                                });
                                row.RelativeItem().Border(1).BorderColor(line).Background(budgetStatusBackground).Padding(8).Column(metric =>
                                {
                                    metric.Spacing(2);
                                    metric.Item().Text("Budget status").FontSize(8.5f).SemiBold().FontColor(inkSoft);
                                    metric.Item().Text(budgetStatusText).FontSize(12).SemiBold().FontColor(budgetStatusColor);
                                });
                            });

                            section.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(118);
                                    columns.RelativeColumn();
                                });

                                foreach (var row in overviewRows)
                                {
                                    table.Cell()
                                        .BorderBottom(1)
                                        .BorderColor(line)
                                        .PaddingVertical(4)
                                        .PaddingRight(8)
                                        .Text(row.Label)
                                        .FontSize(9f)
                                        .SemiBold()
                                        .FontColor(inkSoft);

                                    table.Cell()
                                        .BorderBottom(1)
                                        .BorderColor(line)
                                        .PaddingVertical(4)
                                        .Text(row.Value)
                                        .FontSize(9.5f)
                                        .FontColor(ink);
                                }
                            });
                        });

                        content.Item().Section("shopping").Border(1).BorderColor(line).Background(panel).Padding(11).Column(section =>
                        {
                            section.Spacing(7);
                            section.Item().Text("Shopping list (aisle ordered)").FontSize(13.5f).SemiBold().FontColor(brandDeep);
                            section.Item().Text($"Aisle order: {string.Join(" -> ", result.AisleOrderUsed)}").FontSize(9).FontColor(inkSoft);

                            foreach (var department in groupedItems)
                            {
                                section.Item().Border(1).BorderColor(line).Background(panelSoft).Padding(8).Column(group =>
                                {
                                    group.Spacing(5);
                                    group.Item().Row(row =>
                                    {
                                        row.RelativeItem().Text(department.Department).FontSize(10.5f).SemiBold().FontColor(brandDeep);
                                        row.AutoItem().Text(department.Total.ToString("C", ukCulture)).FontSize(9.5f).SemiBold().FontColor(inkSoft);
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
                                                .BorderBottom(1)
                                                .BorderColor(lineStrong)
                                                .PaddingBottom(4)
                                                .Text("Item")
                                                .FontSize(8.5f)
                                                .SemiBold()
                                                .FontColor(inkSoft);
                                            header.Cell()
                                                .BorderBottom(1)
                                                .BorderColor(lineStrong)
                                                .PaddingBottom(4)
                                                .AlignRight()
                                                .Text("Qty")
                                                .FontSize(8.5f)
                                                .SemiBold()
                                                .FontColor(inkSoft);
                                            header.Cell()
                                                .BorderBottom(1)
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
                                            table.Cell().BorderBottom(1).BorderColor(line).PaddingVertical(4)
                                                .Text($"[ ] {item.Name}")
                                                .FontSize(9.2f);
                                            table.Cell().BorderBottom(1).BorderColor(line).PaddingVertical(4).AlignRight()
                                                .Text(item.QuantityDisplay)
                                                .FontSize(9.2f)
                                                .FontColor(inkSoft);
                                            table.Cell().BorderBottom(1).BorderColor(line).PaddingVertical(4).AlignRight()
                                                .Text(item.EstimatedCost.ToString("C", ukCulture))
                                                .FontSize(9.2f)
                                                .SemiBold();
                                        }
                                    });
                                });
                            }

                            section.Item().PaddingTop(2).AlignRight().Text(text =>
                            {
                                text.Span("Grand total: ").SemiBold().FontColor(inkSoft);
                                text.Span(result.EstimatedTotalCost.ToString("C", ukCulture)).SemiBold().FontColor(brandDeep);
                                });
                        });

                        content.Item().PageBreak();

                        content.Item().Section("meals").Column(section =>
                        {
                            section.Spacing(8);
                            section.Item().Text("Weekly meals and recipes").FontSize(14).SemiBold().FontColor(brandDeep);

                            foreach (var meal in result.MealPlan)
                            {
                                section.Item().Border(1).BorderColor(line).Background(panel).Padding(9).Column(card =>
                                {
                                    card.Spacing(5);
                                    card.Item().Text($"{meal.Day} - {meal.MealName}").FontSize(11.5f).SemiBold().FontColor(brandDeep);

                                    card.Item().Row(meta =>
                                    {
                                        meta.Spacing(6);
                                        meta.AutoItem().Border(1).BorderColor(line).Background(panelSoft).PaddingVertical(2).PaddingHorizontal(6)
                                            .Text($"Cost {meal.EstimatedCost.ToString("C", ukCulture)}").FontSize(8.5f).SemiBold().FontColor(inkSoft);
                                        meta.AutoItem().Border(1).BorderColor(line).Background(panelSoft).PaddingVertical(2).PaddingHorizontal(6)
                                            .Text($"{meal.EstimatedPrepMinutes} mins").FontSize(8.5f).SemiBold().FontColor(inkSoft);

                                        if (meal.LeftoverDaysCovered > 0)
                                        {
                                            meta.AutoItem().Border(1).BorderColor(line).Background(okSoft).PaddingVertical(2).PaddingHorizontal(6)
                                                .Text($"Covers {meal.LeftoverDaysCovered} leftover day(s)").FontSize(8.5f).SemiBold().FontColor(ok);
                                        }
                                    });

                                    card.Item().Text(meal.MealReason).FontColor(inkSoft);

                                    card.Item().Row(row =>
                                    {
                                        row.Spacing(8);

                                        row.RelativeItem().Border(1).BorderColor(line).Background(panelSoft).Padding(7).Column(block =>
                                        {
                                            block.Spacing(3);
                                            block.Item().Text("Ingredients").FontSize(9f).SemiBold().FontColor(brandDeep);

                                            foreach (var ingredientLine in meal.IngredientLines)
                                            {
                                                block.Item().Text($"- {ingredientLine}").FontSize(9f);
                                            }
                                        });

                                        row.RelativeItem().Border(1).BorderColor(line).Background(panelSoft).Padding(7).Column(block =>
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

                        content.Item().Section("budget-notes").Border(1).BorderColor(line).Background(panelSoft).Padding(11).Column(section =>
                        {
                            section.Spacing(6);
                            section.Item().Text("Budget notes").FontSize(13).SemiBold().FontColor(brandDeep);
                            section.Item()
                                .Border(1)
                                .BorderColor(line)
                                .Background(budgetStatusBackground)
                                .Padding(8)
                                .Text(budgetStatusText)
                                .SemiBold()
                                .FontColor(budgetStatusColor);

                            if (result.BudgetTips.Count == 0)
                            {
                                section.Item().Text("No additional budget suggestions for this plan.").FontColor(inkSoft);
                            }
                            else
                            {
                                foreach (var tip in result.BudgetTips)
                                {
                                    section.Item().Text($"- {tip}");
                                }
                            }
                        });
                    });

                    page.Footer().Column(footer =>
                    {
                        footer.Item().LineHorizontal(1).LineColor(line);
                        footer.Item().PaddingTop(4).Row(row =>
                        {
                            row.RelativeItem().DefaultTextStyle(style => style.FontSize(8.5f)).Text(text =>
                            {
                                text.Span("AislePilot").SemiBold().FontColor(brandDeep);
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

    private static IReadOnlyList<string> ParseCustomAisles(string? customAisleOrder)
    {
        return (customAisleOrder ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildChecklistText(AislePilotPlanResultViewModel result)
    {
        var ukCulture = CultureInfo.GetCultureInfo("en-GB");
        var builder = new StringBuilder();
        builder.AppendLine("AislePilot Shopping Checklist");
        builder.AppendLine($"Supermarket: {result.Supermarket}");
        builder.AppendLine($"Estimated total: {result.EstimatedTotalCost.ToString("C", ukCulture)}");
        builder.AppendLine();
        builder.AppendLine($"Aisle order: {string.Join(" -> ", result.AisleOrderUsed)}");

        foreach (var department in result.ShoppingItems.GroupBy(item => item.Department))
        {
            builder.AppendLine();
            builder.AppendLine($"[{department.Key}]");
            foreach (var item in department)
            {
                builder.AppendLine($"[ ] {item.Name} - {item.QuantityDisplay} - {item.EstimatedCost.ToString("C", ukCulture)}");
            }
        }

        return builder.ToString();
    }
}
