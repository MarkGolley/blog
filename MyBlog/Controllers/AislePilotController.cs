using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
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

    [HttpGet("")]
    public IActionResult Index(string? returnUrl = null)
    {
        var request = new AislePilotRequestModel();
        var resolvedReturnUrl = ResolveReturnUrl(returnUrl);
        return View(BuildPageModel(request, returnUrl: resolvedReturnUrl));
    }

    [HttpPost("")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public IActionResult Index(AislePilotPageViewModel pageModel)
    {
        var request = NormalizeRequest(pageModel.Request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        ValidateRequest(request);

        if (!ModelState.IsValid)
        {
            return View(BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        try
        {
            var result = aislePilotService.BuildPlan(request);
            return View(BuildPageModel(request, result, returnUrl: resolvedReturnUrl));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("suggest-from-pantry")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public IActionResult SuggestFromPantry(AislePilotPageViewModel pageModel)
    {
        var request = NormalizeRequest(pageModel.Request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        ValidateRequestForSuggestions(request);

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        var suggestions = aislePilotService.SuggestMealsFromPantry(request, 6);
        if (suggestions.Count == 0)
        {
            ModelState.AddModelError("Request.PantryItems", "No full meals found from your current pantry items. Add more ingredients or generate a full weekly plan.");
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        return View("Index", BuildPageModel(request, pantrySuggestions: suggestions, returnUrl: resolvedReturnUrl));
    }

    [HttpPost("swap-meal")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public IActionResult SwapMeal(AislePilotPageViewModel pageModel, int dayIndex, string? currentMealName, List<string>? currentPlanMealNames)
    {
        var request = NormalizeRequest(pageModel.Request);
        var resolvedReturnUrl = ResolveReturnUrl(pageModel.ReturnUrl);
        ValidateRequest(request);
        var cookDays = Math.Clamp(request.CookDays, 1, 7);

        if (dayIndex < 0 || dayIndex >= cookDays)
        {
            ModelState.AddModelError(string.Empty, "Selected day was out of range. Try generating again.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }

        var seenMealNames = GetSeenMealsForDay(request.SwapHistoryState, dayIndex, currentMealName);

        try
        {
            var result = aislePilotService.SwapMealForDay(request, dayIndex, currentMealName, currentPlanMealNames, seenMealNames);
            request.SwapHistoryState = UpdateSwapHistoryState(
                request.SwapHistoryState,
                dayIndex,
                currentMealName,
                result.MealPlan.ElementAtOrDefault(dayIndex)?.MealName);
            return View("Index", BuildPageModel(request, result, returnUrl: resolvedReturnUrl));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View("Index", BuildPageModel(request, returnUrl: resolvedReturnUrl));
        }
    }

    [HttpPost("export/plan-pack")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public IActionResult ExportPlanPack(AislePilotPageViewModel pageModel)
    {
        var request = NormalizeRequest(pageModel.Request);
        ValidateRequest(request);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var result = aislePilotService.BuildPlan(request);
            var bytes = BuildPlanPackPdf(request, result);
            var fileName = $"aislepilot-plan-pack-{DateTime.UtcNow:yyyyMMdd}.pdf";
            Response.Headers.ContentDisposition = $"inline; filename=\"{fileName}\"";
            return File(bytes, "application/pdf");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "AislePilot AI unavailable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost("export/checklist")]
    [EnableRateLimiting("aislePilotWrites")]
    [ValidateAntiForgeryToken]
    public IActionResult ExportChecklist(AislePilotPageViewModel pageModel)
    {
        var request = NormalizeRequest(pageModel.Request);
        ValidateRequest(request);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var result = aislePilotService.BuildPlan(request);
            var content = BuildChecklistText(result);
            var bytes = Encoding.UTF8.GetBytes(content);
            var fileName = $"aislepilot-checklist-{DateTime.UtcNow:yyyyMMdd}.txt";
            return File(bytes, "text/plain; charset=utf-8", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(title: "AislePilot AI unavailable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private AislePilotPageViewModel BuildPageModel(
        AislePilotRequestModel request,
        AislePilotPlanResultViewModel? result = null,
        IReadOnlyList<AislePilotPantrySuggestionViewModel>? pantrySuggestions = null,
        string returnUrl = "")
    {
        return new AislePilotPageViewModel
        {
            Request = request,
            ReturnUrl = returnUrl,
            Result = result,
            PantrySuggestions = pantrySuggestions ?? [],
            SupermarketOptions = aislePilotService.GetSupportedSupermarkets(),
            PortionSizeOptions = aislePilotService.GetSupportedPortionSizes(),
            DietaryOptions = aislePilotService.GetSupportedDietaryModes()
        };
    }

    private string ResolveReturnUrl(string? returnUrl)
    {
        var fallbackReturnUrl = Url.Action("Index", "Projects") ?? "/projects";

        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            Url.IsLocalUrl(returnUrl) &&
            !IsAislePilotPath(returnUrl))
        {
            return returnUrl;
        }

        var referer = Request.Headers["Referer"].FirstOrDefault();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri) &&
            string.Equals(refererUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            var candidate = $"{refererUri.AbsolutePath}{refererUri.Query}";
            if (Url.IsLocalUrl(candidate) && !IsAislePilotPath(candidate))
            {
                return candidate;
            }
        }

        return fallbackReturnUrl;
    }

    private static bool IsAislePilotPath(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var path = url.Split('?', '#')[0];
        return path.StartsWith("/projects/aisle-pilot", StringComparison.OrdinalIgnoreCase);
    }

    private AislePilotRequestModel NormalizeRequest(AislePilotRequestModel? request)
    {
        var normalized = request ?? new AislePilotRequestModel();
        normalized.Supermarket = normalized.Supermarket?.Trim() ?? string.Empty;
        normalized.PortionSize = normalized.PortionSize?.Trim() ?? string.Empty;
        normalized.CustomAisleOrder = normalized.CustomAisleOrder?.Trim() ?? string.Empty;
        normalized.DislikesOrAllergens = normalized.DislikesOrAllergens?.Trim() ?? string.Empty;
        normalized.PantryItems = normalized.PantryItems?.Trim() ?? string.Empty;
        normalized.LeftoverCookDayIndexesCsv = normalized.LeftoverCookDayIndexesCsv?.Trim() ?? string.Empty;
        normalized.SwapHistoryState = normalized.SwapHistoryState?.Trim() ?? string.Empty;
        normalized.DietaryModes = normalized.DietaryModes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        return normalized;
    }

    private static IReadOnlyList<string> GetSeenMealsForDay(string? swapHistoryState, int dayIndex, string? currentMealName)
    {
        var history = ParseSwapHistoryState(swapHistoryState);
        var seenMeals = history.TryGetValue(dayIndex, out var names)
            ? names
            : [];

        if (string.IsNullOrWhiteSpace(currentMealName))
        {
            return seenMeals;
        }

        return seenMeals
            .Append(currentMealName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string UpdateSwapHistoryState(
        string? currentState,
        int dayIndex,
        string? previousMealName,
        string? nextMealName)
    {
        var history = ParseSwapHistoryState(currentState);
        if (!history.TryGetValue(dayIndex, out var meals))
        {
            meals = [];
            history[dayIndex] = meals;
        }

        if (!string.IsNullOrWhiteSpace(previousMealName) &&
            !meals.Contains(previousMealName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            meals.Add(previousMealName.Trim());
        }

        if (!string.IsNullOrWhiteSpace(nextMealName) &&
            !meals.Contains(nextMealName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            meals.Add(nextMealName.Trim());
        }

        return SerializeSwapHistoryState(history);
    }

    private static Dictionary<int, List<string>> ParseSwapHistoryState(string? swapHistoryState)
    {
        var result = new Dictionary<int, List<string>>();
        if (string.IsNullOrWhiteSpace(swapHistoryState))
        {
            return result;
        }

        var dayEntries = swapHistoryState.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in dayEntries)
        {
            var segments = entry.Split(':', 2, StringSplitOptions.TrimEntries);
            if (segments.Length != 2 || !int.TryParse(segments[0], out var dayIndex) || dayIndex < 0 || dayIndex > 6)
            {
                continue;
            }

            var meals = segments[1]
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(meal => !string.IsNullOrWhiteSpace(meal))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (meals.Count > 0)
            {
                result[dayIndex] = meals;
            }
        }

        return result;
    }

    private static string SerializeSwapHistoryState(Dictionary<int, List<string>> history)
    {
        return string.Join(
            ';',
            history
                .OrderBy(pair => pair.Key)
                .Where(pair => pair.Value.Count > 0)
                .Select(pair => $"{pair.Key}:{string.Join('|', pair.Value.Distinct(StringComparer.OrdinalIgnoreCase))}"));
    }

    private void ValidateRequest(AislePilotRequestModel request)
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
        const string brandDeep = "#103F65";
        const string panel = "#FFFFFF";
        const string panelSoft = "#F0F6FF";
        const string line = "#D8E3EF";
        const string lineStrong = "#C9D8E8";
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
                                    .Svg(AislePilotMarkSvg);

                                row.RelativeItem().Column(column =>
                                {
                                    column.Spacing(1);
                                    column.Item().Text("Aisle Pilot").FontSize(15.5f).SemiBold().FontColor(brandDeep);
                                    column.Item().Text("Weekly meal plan, aisle-sorted shopping, and practical recipes")
                                        .FontSize(8.2f)
                                        .FontColor(inkSoft);
                                });

                                row.AutoItem().AlignMiddle().Border(0.7f).BorderColor(line).Background(panel).PaddingVertical(4).PaddingHorizontal(6).Column(meta =>
                                    {
                                        meta.Spacing(1);
                                        meta.Item().Text(generatedAt.ToString("dd MMM yyyy, HH:mm", ukCulture)).FontSize(8.1f).SemiBold().FontColor(brandDeep);
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
                                row.AutoItem().Text("•").FontSize(9f).FontColor(lineStrong);
                                row.AutoItem().SectionLink("shopping").Text("Shopping list").FontSize(9.5f).SemiBold().FontColor(brandDeep);
                                row.AutoItem().Text("•").FontSize(9f).FontColor(lineStrong);
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
                                    metric.Item().Text("Estimated total").FontSize(8.5f).SemiBold().FontColor(inkSoft);
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
                                text.Span("Grand total: ").SemiBold().FontColor(inkSoft);
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
                                    card.Item().Text($"{meal.Day} - {meal.MealName}").FontSize(11.5f).SemiBold().FontColor(brandDeep);

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
                                .Svg(AislePilotMarkSvg);

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
        builder.AppendLine($"Portion size: {result.PortionSize}");
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
