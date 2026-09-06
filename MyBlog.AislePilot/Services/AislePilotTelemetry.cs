using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace MyBlog.Services;

public static class AislePilotTelemetry
{
    public readonly record struct BackgroundRequestProfile(
        int DietaryConstraintCount,
        int MealSlotCount,
        int PlanDays,
        bool PreferQuickMeals,
        bool IncludeSpecialTreat);

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

    private static readonly Counter<long> BackgroundQueueEvents = Meter.CreateCounter<long>(
        "myblog.aislepilot.background.queue.events",
        unit: "{item}",
        description: "AislePilot managed background queue throughput and outcomes.");

    private static readonly Histogram<double> BackgroundQueueWaitSeconds = Meter.CreateHistogram<double>(
        "myblog.aislepilot.background.queue.wait",
        unit: "s",
        description: "Time AislePilot background jobs spend waiting in the managed queue.");

    private static readonly Counter<long> CacheLookups = Meter.CreateCounter<long>(
        "myblog.aislepilot.cache.lookups",
        unit: "{lookup}",
        description: "AislePilot cache lookups by cache name and hit/miss.");

    private static readonly Counter<long> CacheRefreshes = Meter.CreateCounter<long>(
        "myblog.aislepilot.cache.refreshes",
        unit: "{refresh}",
        description: "AislePilot cache refresh attempts by cache and outcome.");

    private static readonly ConcurrentDictionary<string, long> CacheLastSuccessUnixSeconds = new();
    private static readonly ConcurrentDictionary<string, long> CacheLastFailureUnixSeconds = new();
    private static readonly ObservableGauge<double> CacheRefreshAgeSeconds = Meter.CreateObservableGauge(
        "myblog.aislepilot.cache.refresh.age",
        ObserveCacheRefreshAge,
        unit: "s",
        description: "Seconds since the last successful or failed AislePilot cache refresh.");

    private static readonly Histogram<double> ClientPerformance = Meter.CreateHistogram<double>(
        "myblog.aislepilot.client.performance",
        unit: "ms",
        description: "Privacy-safe AislePilot browser performance and journey durations.");

    private static readonly Histogram<double> ClientLayoutShift = Meter.CreateHistogram<double>(
        "myblog.aislepilot.client.layout_shift",
        unit: "{score}",
        description: "AislePilot browser cumulative layout shift score.");

    private static readonly Histogram<double> PlanStageDuration = Meter.CreateHistogram<double>(
        "myblog.aislepilot.plan.stage.duration",
        unit: "s",
        description: "AislePilot plan-generation stage duration.");

    private static readonly Counter<long> PlansCompleted = Meter.CreateCounter<long>(
        "myblog.aislepilot.plans.completed",
        unit: "{plan}",
        description: "Completed AislePilot plans by bounded source category.");

    private static readonly Counter<long> ClientEvents = Meter.CreateCounter<long>(
        "myblog.aislepilot.client.events",
        unit: "{event}",
        description: "Privacy-safe AislePilot browser journey and error events.");

    private static Func<IEnumerable<Measurement<long>>>? _queueDepthMeasurementsFactory;
    private static Func<long>? _managedQueueDepthFactory;
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
        string? errorType = null,
        BackgroundRequestProfile? requestProfile = null)
    {
        var normalizedJobName = SanitizeTagValue(jobName);
        var normalizedErrorType = SanitizeTagValue(errorType);
        var tags = new List<KeyValuePair<string, object?>>
        {
            new KeyValuePair<string, object?>("job", normalizedJobName),
            new KeyValuePair<string, object?>("success", success),
            new KeyValuePair<string, object?>("error_type", normalizedErrorType)
        };
        if (requestProfile is { } profile)
        {
            tags.Add(new KeyValuePair<string, object?>(
                "dietary_complexity",
                profile.DietaryConstraintCount switch
                {
                    <= 0 => "balanced_only",
                    1 => "single_constraint",
                    _ => "multiple_constraints"
                }));
            tags.Add(new KeyValuePair<string, object?>(
                "meal_slot_count",
                Math.Clamp(profile.MealSlotCount, 1, 3)));
            tags.Add(new KeyValuePair<string, object?>(
                "plan_length",
                profile.PlanDays switch
                {
                    <= 3 => "short",
                    <= 7 => "standard",
                    _ => "extended"
                }));
            tags.Add(new KeyValuePair<string, object?>("prefer_quick_meals", profile.PreferQuickMeals));
            tags.Add(new KeyValuePair<string, object?>("include_special_treat", profile.IncludeSpecialTreat));
        }

        BackgroundJobs.Add(1, tags.ToArray());
        BackgroundJobDurationSeconds.Record(duration.TotalSeconds, tags.ToArray());
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

    public static void RecordCacheRefresh(string cacheName, bool success)
    {
        var normalizedCache = NormalizeCacheName(cacheName);
        var outcome = success ? "success" : "failure";
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestamps = success ? CacheLastSuccessUnixSeconds : CacheLastFailureUnixSeconds;
        timestamps[normalizedCache] = nowUnixSeconds;
        CacheRefreshes.Add(
            1,
            new KeyValuePair<string, object?>("cache", normalizedCache),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    public static void RecordBackgroundQueueEvent(string jobName, string eventName)
    {
        var normalizedEvent = eventName switch
        {
            "accepted" or "rejected" or "dequeued" or "retry_scheduled" or
                "completed" or "cancelled" or "faulted" => eventName,
            _ => "unknown"
        };
        BackgroundQueueEvents.Add(
            1,
            new KeyValuePair<string, object?>("job", SanitizeTagValue(jobName)),
            new KeyValuePair<string, object?>("event", normalizedEvent));
    }

    public static void RecordBackgroundQueueWait(string jobName, TimeSpan duration)
    {
        BackgroundQueueWaitSeconds.Record(
            Math.Max(0, duration.TotalSeconds),
            new KeyValuePair<string, object?>("job", SanitizeTagValue(jobName)));
    }

    public static void ConfigureManagedQueueDepthObserver(Func<long> depthFactory)
    {
        _managedQueueDepthFactory = depthFactory;
    }

    public static bool TryRecordClientPerformance(
        string? metric,
        double valueMilliseconds,
        string? navigationType,
        bool hasResult)
    {
        var normalizedMetric = metric?.Trim().ToLowerInvariant();
        if (normalizedMetric is not (
                "ttfb" or "lcp" or "inp" or "cls" or
                "setup_to_submit" or "submit_to_plan_visible" or "page_usable" or
                "image_placeholder_to_image" or "swap_latency" or "pantry_latency" or
                "save_latency" or "export_latency") ||
            !double.IsFinite(valueMilliseconds) ||
            valueMilliseconds < 0 ||
            valueMilliseconds > 300_000)
        {
            return false;
        }

        var tags = new[]
        {
            new KeyValuePair<string, object?>("navigation_type", SanitizeNavigationType(navigationType)),
            new KeyValuePair<string, object?>("has_result", hasResult)
        };
        if (normalizedMetric == "cls")
        {
            ClientLayoutShift.Record(valueMilliseconds, tags);
        }
        else
        {
            ClientPerformance.Record(
                valueMilliseconds,
                [new KeyValuePair<string, object?>("metric", normalizedMetric), .. tags]);
        }
        return true;
    }

    public static void RecordPlanStage(string stage, TimeSpan duration, string? source = null)
    {
        PlanStageDuration.Record(
            Math.Max(0, duration.TotalSeconds),
            [
                new KeyValuePair<string, object?>("stage", SanitizeTagValue(stage)),
                new KeyValuePair<string, object?>("source", SanitizeTagValue(source))
            ]);
    }

    public static void RecordPlanSource(string? planSourceLabel)
    {
        var source = planSourceLabel?.Trim().ToLowerInvariant() switch
        {
            "ai meal pool" or "personalised meal plan" => "memory_pool",
            "template fallback" or "aislepilot recipe plan" => "template",
            "openai" or "fresh personalised plan" => "ai",
            "template + ai special treat" or
            "ai/template mix" or
            "ai/template mix (special treat pending)" or
            "personalised plan with a special treat" or
            "personalised recipe mix" or
            "personalised recipe mix — special treat being prepared" => "mixed",
            "current plan" or "current plan + template top-up" or "updated meal plan" => "existing_plan",
            _ => "other"
        };

        PlansCompleted.Add(1, new KeyValuePair<string, object?>("source", source));
    }

    public static bool TryRecordClientEvent(
        string? eventName,
        string? navigationType,
        bool hasResult)
    {
        var normalizedEvent = eventName?.Trim().ToLowerInvariant();
        if (normalizedEvent is not (
                "setup_started" or "setup_submitted" or "setup_abandoned" or
                "client_error" or "unhandled_rejection"))
        {
            return false;
        }

        ClientEvents.Add(
            1,
            [
                new KeyValuePair<string, object?>("event", normalizedEvent),
                new KeyValuePair<string, object?>("navigation_type", SanitizeNavigationType(navigationType)),
                new KeyValuePair<string, object?>("has_result", hasResult)
            ]);
        return true;
    }

    private static IEnumerable<Measurement<long>> ObserveQueueDepth()
    {
        var existing = _queueDepthMeasurementsFactory?.Invoke() ?? [];
        return _managedQueueDepthFactory is null
            ? existing
            : existing.Append(new Measurement<long>(
                Math.Max(0, _managedQueueDepthFactory()),
                new KeyValuePair<string, object?>("queue", "managed_background")));
    }

    private static IEnumerable<Measurement<double>> ObserveCacheRefreshAge()
    {
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var item in CacheLastSuccessUnixSeconds)
        {
            yield return new Measurement<double>(
                Math.Max(0, nowUnixSeconds - item.Value),
                new KeyValuePair<string, object?>("cache", item.Key),
                new KeyValuePair<string, object?>("outcome", "success"));
        }
        foreach (var item in CacheLastFailureUnixSeconds)
        {
            yield return new Measurement<double>(
                Math.Max(0, nowUnixSeconds - item.Value),
                new KeyValuePair<string, object?>("cache", item.Key),
                new KeyValuePair<string, object?>("outcome", "failure"));
        }
    }

    private static string NormalizeCacheName(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "ai_meal_pool" => "ai_meal_pool",
            "dessert_addon_pool" => "dessert_addon_pool",
            "supermarket_layouts" => "supermarket_layouts",
            "meal_images" => "meal_images",
            _ => "other"
        };
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

    private static string SanitizeNavigationType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "navigate" or "reload" or "back_forward" or "prerender"
            ? normalized
            : "unknown";
    }
}
