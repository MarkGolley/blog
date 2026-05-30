using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyBlog.Services;

internal static class MyBlogTelemetry
{
    public const string ActivitySourceName = "MyBlog.Web";
    public const string MeterName = "MyBlog.Web";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> CommentSubmissions = Meter.CreateCounter<long>(
        "myblog.comment.submissions",
        unit: "{submission}",
        description: "Number of comment submission attempts by moderation outcome.");

    private static readonly Counter<long> AuthEvents = Meter.CreateCounter<long>(
        "myblog.auth.events",
        unit: "{event}",
        description: "Authentication events, including sign-in and sign-out outcomes.");

    private static readonly Histogram<double> AiRequestDurationSeconds = Meter.CreateHistogram<double>(
        "myblog.ai.request.duration",
        unit: "s",
        description: "AI request duration.");

    private static readonly Counter<long> AiRequests = Meter.CreateCounter<long>(
        "myblog.ai.requests",
        unit: "{request}",
        description: "AI requests by provider/model/operation and outcome.");

    private static readonly Counter<long> AiTokens = Meter.CreateCounter<long>(
        "myblog.ai.tokens",
        unit: "{token}",
        description: "AI token usage estimates or parsed usage.");

    private static readonly Counter<double> AiEstimatedCostUsd = Meter.CreateCounter<double>(
        "myblog.ai.estimated_cost",
        unit: "USD",
        description: "Estimated AI cost in USD.");

    private static readonly UpDownCounter<long> ActiveRequests = Meter.CreateUpDownCounter<long>(
        "myblog.http.active_requests",
        unit: "{request}",
        description: "Current number of in-flight HTTP requests.");

    private static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>(
        "myblog.http.request.duration",
        unit: "ms",
        description: "HTTP request duration by endpoint and status code.");

    public static Activity? StartActivity(string operationName, ActivityKind activityKind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(operationName, activityKind);
    }

    public static void RecordCommentSubmission(
        string outcome,
        bool approved,
        bool hasParentComment)
    {
        CommentSubmissions.Add(1,
        [
            new KeyValuePair<string, object?>("moderation_outcome", SanitizeTagValue(outcome)),
            new KeyValuePair<string, object?>("approved", approved),
            new KeyValuePair<string, object?>("has_parent", hasParentComment)
        ]);
    }

    public static void RecordAuthEvent(string eventName, bool success)
    {
        AuthEvents.Add(1,
        [
            new KeyValuePair<string, object?>("event", SanitizeTagValue(eventName)),
            new KeyValuePair<string, object?>("success", success)
        ]);
    }

    public static void RecordRequestStarted()
    {
        ActiveRequests.Add(1);
    }

    public static void RecordRequestCompleted(
        string endpointName,
        string method,
        int statusCode,
        double durationMs,
        bool isError)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("endpoint", SanitizeTagValue(endpointName)),
            new KeyValuePair<string, object?>("method", SanitizeTagValue(method)),
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("error", isError)
        };

        RequestDurationMs.Record(durationMs, tags);
        ActiveRequests.Add(-1);
    }

    public static void RecordAiRequest(
        string provider,
        string operation,
        string? model,
        TimeSpan duration,
        bool success,
        int? promptTokens = null,
        int? completionTokens = null,
        double? estimatedCostUsd = null,
        string? errorType = null)
    {
        var normalizedProvider = SanitizeTagValue(provider);
        var normalizedOperation = SanitizeTagValue(operation);
        var normalizedModel = SanitizeTagValue(model);
        var normalizedErrorType = SanitizeTagValue(errorType);

        AiRequests.Add(1,
        [
            new KeyValuePair<string, object?>("provider", normalizedProvider),
            new KeyValuePair<string, object?>("operation", normalizedOperation),
            new KeyValuePair<string, object?>("model", normalizedModel),
            new KeyValuePair<string, object?>("success", success),
            new KeyValuePair<string, object?>("error_type", normalizedErrorType)
        ]);

        AiRequestDurationSeconds.Record(
            duration.TotalSeconds,
            [
                new KeyValuePair<string, object?>("provider", normalizedProvider),
                new KeyValuePair<string, object?>("operation", normalizedOperation),
                new KeyValuePair<string, object?>("model", normalizedModel),
                new KeyValuePair<string, object?>("success", success)
            ]);

        if (promptTokens.HasValue && promptTokens.Value > 0)
        {
            AiTokens.Add(promptTokens.Value,
            [
                new KeyValuePair<string, object?>("provider", normalizedProvider),
                new KeyValuePair<string, object?>("operation", normalizedOperation),
                new KeyValuePair<string, object?>("model", normalizedModel),
                new KeyValuePair<string, object?>("token_type", "input")
            ]);
        }

        if (completionTokens.HasValue && completionTokens.Value > 0)
        {
            AiTokens.Add(completionTokens.Value,
            [
                new KeyValuePair<string, object?>("provider", normalizedProvider),
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
                    new KeyValuePair<string, object?>("provider", normalizedProvider),
                    new KeyValuePair<string, object?>("operation", normalizedOperation),
                    new KeyValuePair<string, object?>("model", normalizedModel)
                ]);
        }
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
