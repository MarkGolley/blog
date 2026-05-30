using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyBlog.Services;

public static class AislePilotTelemetry
{
    public const string ActivitySourceName = "MyBlog.AislePilot";
    public const string MeterName = "MyBlog.AislePilot";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    private static readonly Histogram<double> AiRequestDurationSeconds = Meter.CreateHistogram<double>(
        "myblog.aislepilot.ai.request.duration",
        unit: "s",
        description: "AislePilot AI request duration.");

    private static readonly Counter<long> AiRequests = Meter.CreateCounter<long>(
        "myblog.aislepilot.ai.requests",
        unit: "{request}",
        description: "AislePilot AI requests by operation/model and outcome.");

    private static readonly Counter<long> AiTokens = Meter.CreateCounter<long>(
        "myblog.aislepilot.ai.tokens",
        unit: "{token}",
        description: "AislePilot AI token usage estimates or parsed usage.");

    private static readonly Counter<double> AiEstimatedCostUsd = Meter.CreateCounter<double>(
        "myblog.aislepilot.ai.estimated_cost",
        unit: "USD",
        description: "AislePilot estimated AI cost in USD.");

    private static readonly Counter<long> BackgroundJobs = Meter.CreateCounter<long>(
        "myblog.aislepilot.background.jobs",
        unit: "{job}",
        description: "AislePilot background jobs by job type and outcome.");

    private static readonly Histogram<double> BackgroundJobDurationSeconds = Meter.CreateHistogram<double>(
        "myblog.aislepilot.background.job.duration",
        unit: "s",
        description: "AislePilot background job duration.");

    private static readonly Counter<long> CacheLookups = Meter.CreateCounter<long>(
        "myblog.aislepilot.cache.lookups",
        unit: "{lookup}",
        description: "AislePilot cache lookups by cache name and hit/miss.");

    private static Func<IEnumerable<Measurement<long>>>? _queueDepthMeasurementsFactory;
    private static readonly ObservableGauge<long> QueueDepth = Meter.CreateObservableGauge(
        "myblog.aislepilot.queue.depth",
        ObserveQueueDepth,
        unit: "{item}",
        description: "Approximate queued or in-flight background work per queue.");

    public static Activity? StartActivity(
        string operationName,
        ActivityKind activityKind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(operationName, activityKind);
    }

    public static void RecordAiRequest(
        string operation,
        string? model,
        TimeSpan duration,
        bool success,
        int? promptTokens = null,
        int? completionTokens = null,
        double? estimatedCostUsd = null,
        string? errorType = null)
    {
        var normalizedOperation = SanitizeTagValue(operation);
        var normalizedModel = SanitizeTagValue(model);
        var normalizedErrorType = SanitizeTagValue(errorType);

        AiRequests.Add(1,
        [
            new KeyValuePair<string, object?>("operation", normalizedOperation),
            new KeyValuePair<string, object?>("model", normalizedModel),
            new KeyValuePair<string, object?>("success", success),
            new KeyValuePair<string, object?>("error_type", normalizedErrorType)
        ]);

        AiRequestDurationSeconds.Record(
            duration.TotalSeconds,
            [
                new KeyValuePair<string, object?>("operation", normalizedOperation),
                new KeyValuePair<string, object?>("model", normalizedModel),
                new KeyValuePair<string, object?>("success", success)
            ]);

        if (promptTokens.HasValue && promptTokens.Value > 0)
        {
            AiTokens.Add(promptTokens.Value,
            [
                new KeyValuePair<string, object?>("operation", normalizedOperation),
                new KeyValuePair<string, object?>("model", normalizedModel),
                new KeyValuePair<string, object?>("token_type", "input")
            ]);
        }

        if (completionTokens.HasValue && completionTokens.Value > 0)
        {
            AiTokens.Add(completionTokens.Value,
            [
                new KeyValuePair<string, object?>("operation", normalizedOperation),
                new KeyValuePair<string, object?>("model", normalizedModel),
                new KeyValuePair<string, object?>("token_type", "output")
            ]);
        }

        if (estimatedCostUsd.HasValue && estimatedCostUsd.Value > 0)
        {
            AiEstimatedCostUsd.Add(
                estimatedCostUsd.Value,
                [
                    new KeyValuePair<string, object?>("operation", normalizedOperation),
                    new KeyValuePair<string, object?>("model", normalizedModel)
                ]);
        }
    }

    public static void RecordBackgroundJob(
        string jobName,
        TimeSpan duration,
        bool success,
        string? errorType = null)
    {
        var normalizedJobName = SanitizeTagValue(jobName);
        var normalizedErrorType = SanitizeTagValue(errorType);
        var tags = new[]
        {
            new KeyValuePair<string, object?>("job", normalizedJobName),
            new KeyValuePair<string, object?>("success", success),
            new KeyValuePair<string, object?>("error_type", normalizedErrorType)
        };

        BackgroundJobs.Add(1, tags);
        BackgroundJobDurationSeconds.Record(duration.TotalSeconds, tags);
    }

    public static void RecordCacheLookup(string cacheName, bool hit)
    {
        CacheLookups.Add(1,
        [
            new KeyValuePair<string, object?>("cache", SanitizeTagValue(cacheName)),
            new KeyValuePair<string, object?>("result", hit ? "hit" : "miss")
        ]);
    }

    public static void ConfigureQueueDepthObserver(Func<IEnumerable<Measurement<long>>> measurementsFactory)
    {
        _queueDepthMeasurementsFactory = measurementsFactory;
    }

    private static IEnumerable<Measurement<long>> ObserveQueueDepth()
    {
        return _queueDepthMeasurementsFactory?.Invoke() ?? [];
    }

    private static string SanitizeTagValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var normalized = value.Trim();
        return normalized.Length <= 72
            ? normalized
            : normalized[..72];
    }
}
