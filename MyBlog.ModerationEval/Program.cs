using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyBlog.Services;

var settings = EvalSettings.Parse(args);
if (settings.ShowHelp)
{
    PrintUsage();
    return 0;
}

try
{
    return await RunAsync(settings);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Moderation evaluation failed: {ex.Message}");
    return 1;
}

static async Task<int> RunAsync(EvalSettings settings)
{
    var datasetPath = Path.GetFullPath(settings.DatasetPath, Directory.GetCurrentDirectory());
    if (!File.Exists(datasetPath))
    {
        throw new FileNotFoundException("Dataset file not found.", datasetPath);
    }

    var outputRoot = Path.GetFullPath(settings.OutputDirectory, Directory.GetCurrentDirectory());
    Directory.CreateDirectory(outputRoot);

    var datasetJson = await File.ReadAllTextAsync(datasetPath);
    var cases = JsonSerializer.Deserialize(datasetJson, EvalJsonContext.Default.ListModerationEvalCase)
        ?? throw new InvalidOperationException("Unable to deserialize moderation dataset.");

    if (settings.Limit.HasValue)
    {
        cases = cases.Take(settings.Limit.Value).ToList();
    }

    if (cases.Count == 0)
    {
        throw new InvalidOperationException("Dataset is empty after applying filters.");
    }

    var configuration = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .Build();

    var apiKey = configuration["OPENAI_API_KEY"];
    var usingLiveModeration = !string.IsNullOrWhiteSpace(apiKey);

    using var client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
    };

    var moderationService = new AIModerationService(client, configuration, NullLogger<AIModerationService>.Instance);
    var results = new List<ModerationEvalResult>(cases.Count);

    foreach (var sample in cases)
    {
        var sw = Stopwatch.StartNew();
        var predictedIsSafe = await moderationService.IsCommentSafeAsync(sample.Text);
        sw.Stop();

        var expectedIsUnsafe = !sample.ExpectedIsSafe;
        var predictedIsUnsafe = !predictedIsSafe;
        var outcome = ClassifyOutcome(expectedIsUnsafe, predictedIsUnsafe);

        results.Add(new ModerationEvalResult(
            sample.Id,
            sample.Category,
            sample.ExpectedIsSafe,
            predictedIsSafe,
            outcome,
            sw.ElapsedMilliseconds,
            sample.Text));

        if (settings.DelayMs > 0)
        {
            await Task.Delay(settings.DelayMs);
        }
    }

    var metrics = ComputeMetrics(results);
    var categorySummaries = ComputeCategorySummaries(results);

    var runUtc = DateTime.UtcNow;
    var runId = $"{settings.Label}-{runUtc:yyyyMMdd-HHmmss}";
    var runDirectory = Path.Combine(outputRoot, runId);
    Directory.CreateDirectory(runDirectory);

    var report = new ModerationEvalRunReport(
        runId,
        runUtc,
        settings.Label,
        datasetPath,
        usingLiveModeration ? "OpenAI live moderation" : "Manual-review fallback (OPENAI_API_KEY missing)",
        settings.TimeoutSeconds,
        settings.DelayMs,
        metrics,
        categorySummaries,
        results);

    var jsonPath = Path.Combine(runDirectory, "report.json");
    var markdownPath = Path.Combine(runDirectory, "report.md");

    var reportJson = JsonSerializer.Serialize(report, EvalJsonContext.Default.ModerationEvalRunReport);
    await File.WriteAllTextAsync(jsonPath, reportJson);
    await File.WriteAllTextAsync(markdownPath, BuildMarkdownReport(report));

    Console.WriteLine($"Run ID: {runId}");
    Console.WriteLine($"Mode: {report.EvaluationMode}");
    Console.WriteLine($"Samples: {metrics.TotalSamples}");
    Console.WriteLine($"Accuracy: {ToPercent(metrics.Accuracy)}");
    Console.WriteLine($"Unsafe precision: {ToPercent(metrics.PrecisionUnsafe)}");
    Console.WriteLine($"Unsafe recall: {ToPercent(metrics.RecallUnsafe)}");
    Console.WriteLine($"Unsafe F1: {ToPercent(metrics.F1Unsafe)}");
    Console.WriteLine($"Latency (avg/p50/p95 ms): {metrics.AverageLatencyMs:F1}/{metrics.P50LatencyMs:F1}/{metrics.P95LatencyMs:F1}");
    Console.WriteLine($"Report: {markdownPath}");
    Console.WriteLine($"JSON: {jsonPath}");

    return 0;
}

static ModerationOutcome ClassifyOutcome(bool expectedIsUnsafe, bool predictedIsUnsafe)
{
    if (expectedIsUnsafe && predictedIsUnsafe)
    {
        return ModerationOutcome.TruePositive;
    }

    if (!expectedIsUnsafe && !predictedIsUnsafe)
    {
        return ModerationOutcome.TrueNegative;
    }

    if (!expectedIsUnsafe && predictedIsUnsafe)
    {
        return ModerationOutcome.FalsePositive;
    }

    return ModerationOutcome.FalseNegative;
}

static ModerationMetrics ComputeMetrics(List<ModerationEvalResult> results)
{
    var tp = results.Count(r => r.Outcome == ModerationOutcome.TruePositive);
    var tn = results.Count(r => r.Outcome == ModerationOutcome.TrueNegative);
    var fp = results.Count(r => r.Outcome == ModerationOutcome.FalsePositive);
    var fn = results.Count(r => r.Outcome == ModerationOutcome.FalseNegative);

    var total = results.Count;
    var accuracy = SafeDivide(tp + tn, total);
    var precisionUnsafe = SafeDivide(tp, tp + fp);
    var recallUnsafe = SafeDivide(tp, tp + fn);
    var f1Unsafe = precisionUnsafe + recallUnsafe == 0
        ? 0
        : 2 * (precisionUnsafe * recallUnsafe) / (precisionUnsafe + recallUnsafe);

    var latencies = results.Select(r => r.LatencyMs).OrderBy(x => x).ToList();
    var averageLatency = latencies.Count == 0 ? 0 : latencies.Average();
    var p50Latency = Percentile(latencies, 50);
    var p95Latency = Percentile(latencies, 95);

    return new ModerationMetrics(
        total,
        tp,
        tn,
        fp,
        fn,
        accuracy,
        precisionUnsafe,
        recallUnsafe,
        f1Unsafe,
        averageLatency,
        p50Latency,
        p95Latency);
}

static List<CategorySummary> ComputeCategorySummaries(List<ModerationEvalResult> results)
{
    return results
        .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(group =>
        {
            var total = group.Count();
            var correct = group.Count(x => x.IsCorrect);
            return new CategorySummary(group.Key, total, correct, SafeDivide(correct, total));
        })
        .ToList();
}

static double Percentile(List<long> orderedAscendingValues, int percentile)
{
    if (orderedAscendingValues.Count == 0)
    {
        return 0;
    }

    var rank = (percentile / 100d) * (orderedAscendingValues.Count - 1);
    var lower = (int)Math.Floor(rank);
    var upper = (int)Math.Ceiling(rank);

    if (lower == upper)
    {
        return orderedAscendingValues[lower];
    }

    var weight = rank - lower;
    return orderedAscendingValues[lower] + ((orderedAscendingValues[upper] - orderedAscendingValues[lower]) * weight);
}

static double SafeDivide(double numerator, double denominator)
{
    return denominator == 0 ? 0 : numerator / denominator;
}

static string BuildMarkdownReport(ModerationEvalRunReport report)
{
    var sb = new StringBuilder();
    sb.AppendLine("# AI Moderation Baseline Report");
    sb.AppendLine();
    sb.AppendLine($"- Run ID: `{report.RunId}`");
    sb.AppendLine($"- UTC timestamp: `{report.RunUtc:yyyy-MM-dd HH:mm:ss}`");
    sb.AppendLine($"- Dataset: `{report.DatasetPath}`");
    sb.AppendLine($"- Evaluation mode: `{report.EvaluationMode}`");
    sb.AppendLine($"- Timeout per request: `{report.TimeoutSeconds}` seconds");
    sb.AppendLine($"- Delay between requests: `{report.DelayMs}` ms");
    sb.AppendLine();

    sb.AppendLine("## Headline Metrics");
    sb.AppendLine();
    sb.AppendLine($"- Total samples: `{report.Metrics.TotalSamples}`");
    sb.AppendLine($"- Accuracy: `{ToPercent(report.Metrics.Accuracy)}`");
    sb.AppendLine($"- Unsafe precision: `{ToPercent(report.Metrics.PrecisionUnsafe)}`");
    sb.AppendLine($"- Unsafe recall: `{ToPercent(report.Metrics.RecallUnsafe)}`");
    sb.AppendLine($"- Unsafe F1: `{ToPercent(report.Metrics.F1Unsafe)}`");
    sb.AppendLine(
        $"- Latency (avg / p50 / p95 ms): `{report.Metrics.AverageLatencyMs:F1} / {report.Metrics.P50LatencyMs:F1} / {report.Metrics.P95LatencyMs:F1}`");
    sb.AppendLine();

    sb.AppendLine("## Confusion Matrix");
    sb.AppendLine();
    sb.AppendLine("|  | Predicted Unsafe | Predicted Safe |");
    sb.AppendLine("|---|---:|---:|");
    sb.AppendLine($"| Actual Unsafe | {report.Metrics.TruePositives} | {report.Metrics.FalseNegatives} |");
    sb.AppendLine($"| Actual Safe | {report.Metrics.FalsePositives} | {report.Metrics.TrueNegatives} |");
    sb.AppendLine();

    sb.AppendLine("## Category Accuracy");
    sb.AppendLine();
    sb.AppendLine("| Category | Correct | Total | Accuracy |");
    sb.AppendLine("|---|---:|---:|---:|");
    foreach (var summary in report.CategorySummaries)
    {
        sb.AppendLine(
            $"| {summary.Category} | {summary.Correct} | {summary.Total} | {ToPercent(summary.Accuracy)} |");
    }

    sb.AppendLine();
    sb.AppendLine("## Misclassifications");
    sb.AppendLine();

    var misses = report.Results
        .Where(x => !x.IsCorrect)
        .ToList();

    if (misses.Count == 0)
    {
        sb.AppendLine("No misclassifications in this run.");
    }
    else
    {
        sb.AppendLine("| ID | Category | Expected | Predicted | Latency ms |");
        sb.AppendLine("|---|---|---|---|---:|");
        foreach (var miss in misses)
        {
            sb.AppendLine(
                $"| {miss.Id} | {miss.Category} | {(miss.ExpectedIsSafe ? "Safe" : "Unsafe")} | {(miss.PredictedIsSafe ? "Safe" : "Unsafe")} | {miss.LatencyMs} |");
        }
    }

    sb.AppendLine();
    sb.AppendLine("## Notes");
    sb.AppendLine();
    sb.AppendLine("- `False positive` means safe content was blocked.");
    sb.AppendLine("- `False negative` means unsafe content was allowed.");
    sb.AppendLine("- Compare this report with future runs to quantify quality improvements.");

    return sb.ToString();
}

static string ToPercent(double value)
{
    return value.ToString("P2", CultureInfo.InvariantCulture);
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine(
        "  dotnet run --project MyBlog.ModerationEval -- [--dataset <path>] [--outdir <path>] [--label <value>] [--limit <n>] [--timeout <seconds>] [--delay-ms <ms>]");
    Console.WriteLine();
    Console.WriteLine("Defaults:");
    Console.WriteLine("  --dataset docs/ai-moderation-v2/datasets/baseline-v1.json");
    Console.WriteLine("  --outdir  artifacts/moderation-eval/reports");
    Console.WriteLine("  --label   baseline-v1");
    Console.WriteLine("  --timeout 30");
    Console.WriteLine("  --delay-ms 0");
}

internal sealed record EvalSettings(
    string DatasetPath,
    string OutputDirectory,
    string Label,
    int? Limit,
    int TimeoutSeconds,
    int DelayMs,
    bool ShowHelp)
{
    public static EvalSettings Parse(string[] args)
    {
        var dataset = Path.Combine("docs", "ai-moderation-v2", "datasets", "baseline-v1.json");
        var outdir = Path.Combine("artifacts", "moderation-eval", "reports");
        var label = "baseline-v1";
        int? limit = null;
        var timeoutSeconds = 30;
        var delayMs = 0;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--dataset":
                    dataset = GetValue(args, ref i, arg);
                    break;
                case "--outdir":
                    outdir = GetValue(args, ref i, arg);
                    break;
                case "--label":
                    label = GetValue(args, ref i, arg);
                    break;
                case "--limit":
                    limit = int.Parse(GetValue(args, ref i, arg), CultureInfo.InvariantCulture);
                    if (limit <= 0)
                    {
                        throw new ArgumentException("--limit must be greater than 0.");
                    }

                    break;
                case "--timeout":
                    timeoutSeconds = int.Parse(GetValue(args, ref i, arg), CultureInfo.InvariantCulture);
                    if (timeoutSeconds <= 0)
                    {
                        throw new ArgumentException("--timeout must be greater than 0.");
                    }

                    break;
                case "--delay-ms":
                    delayMs = int.Parse(GetValue(args, ref i, arg), CultureInfo.InvariantCulture);
                    if (delayMs < 0)
                    {
                        throw new ArgumentException("--delay-ms must be 0 or greater.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return new EvalSettings(dataset, outdir, label, limit, timeoutSeconds, delayMs, showHelp);
    }

    private static string GetValue(string[] args, ref int index, string argName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {argName}.");
        }

        index++;
        return args[index];
    }
}

internal sealed record ModerationEvalCase(
    string Id,
    string Category,
    bool ExpectedIsSafe,
    string Text);

internal sealed record ModerationEvalResult(
    string Id,
    string Category,
    bool ExpectedIsSafe,
    bool PredictedIsSafe,
    ModerationOutcome Outcome,
    long LatencyMs,
    string Text)
{
    public bool IsCorrect => Outcome is ModerationOutcome.TruePositive or ModerationOutcome.TrueNegative;
}

internal sealed record ModerationMetrics(
    int TotalSamples,
    int TruePositives,
    int TrueNegatives,
    int FalsePositives,
    int FalseNegatives,
    double Accuracy,
    double PrecisionUnsafe,
    double RecallUnsafe,
    double F1Unsafe,
    double AverageLatencyMs,
    double P50LatencyMs,
    double P95LatencyMs);

internal sealed record CategorySummary(
    string Category,
    int Total,
    int Correct,
    double Accuracy);

internal sealed record ModerationEvalRunReport(
    string RunId,
    DateTime RunUtc,
    string Label,
    string DatasetPath,
    string EvaluationMode,
    int TimeoutSeconds,
    int DelayMs,
    ModerationMetrics Metrics,
    List<CategorySummary> CategorySummaries,
    List<ModerationEvalResult> Results);

internal enum ModerationOutcome
{
    TruePositive,
    TrueNegative,
    FalsePositive,
    FalseNegative
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(List<ModerationEvalCase>))]
[JsonSerializable(typeof(ModerationEvalRunReport))]
internal sealed partial class EvalJsonContext : JsonSerializerContext;
