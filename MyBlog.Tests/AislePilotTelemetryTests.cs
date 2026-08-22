using System.Diagnostics.Metrics;
using MyBlog.Services;

namespace MyBlog.Tests;

public sealed class AislePilotTelemetryTests
{
    [Fact]
    public void RecordCacheRefresh_TracksBoundedOutcomeAndFreshnessAge()
    {
        var refreshes = new List<(string Cache, string Outcome)>();
        var ages = new List<(string Cache, string Outcome, double Age)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AislePilotTelemetry.MeterName &&
                    instrument.Name is "myblog.aislepilot.cache.refreshes" or
                        "myblog.aislepilot.cache.refresh.age")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            refreshes.Add((values["cache"]?.ToString() ?? string.Empty, values["outcome"]?.ToString() ?? string.Empty));
        });
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            ages.Add((
                values["cache"]?.ToString() ?? string.Empty,
                values["outcome"]?.ToString() ?? string.Empty,
                value));
        });
        listener.Start();

        AislePilotTelemetry.RecordCacheRefresh("ai_meal_pool", success: true);
        AislePilotTelemetry.RecordCacheRefresh("user-entered-cache-name", success: false);
        listener.RecordObservableInstruments();

        Assert.Contains(("ai_meal_pool", "success"), refreshes);
        Assert.Contains(("other", "failure"), refreshes);
        Assert.Contains(ages, item => item.Cache == "ai_meal_pool" && item.Outcome == "success" && item.Age >= 0);
        Assert.Contains(ages, item => item.Cache == "other" && item.Outcome == "failure" && item.Age >= 0);
    }

    [Fact]
    public void RecordBackgroundQueueTelemetry_UsesBoundedJobAndEventTags()
    {
        var events = new List<(string Job, string Event)>();
        var waits = new List<(string Job, double Seconds)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AislePilotTelemetry.MeterName &&
                    instrument.Name is "myblog.aislepilot.background.queue.events" or
                        "myblog.aislepilot.background.queue.wait")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            events.Add((values["job"]?.ToString() ?? string.Empty, values["event"]?.ToString() ?? string.Empty));
        });
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            waits.Add((values["job"]?.ToString() ?? string.Empty, value));
        });
        listener.Start();

        AislePilotTelemetry.RecordBackgroundQueueEvent("meal_image_generation", "accepted");
        AislePilotTelemetry.RecordBackgroundQueueEvent("meal_image_generation", "free-form-event");
        AislePilotTelemetry.RecordBackgroundQueueWait("meal_image_generation", TimeSpan.FromMilliseconds(250));

        Assert.Equal(("meal_image_generation", "accepted"), events[0]);
        Assert.Equal(("meal_image_generation", "unknown"), events[1]);
        var wait = Assert.Single(waits);
        Assert.Equal("meal_image_generation", wait.Job);
        Assert.Equal(0.25, wait.Seconds, 3);
    }

    [Fact]
    public void RecordBackgroundJob_UsesOnlyBoundedRequestProfileTags()
    {
        Dictionary<string, object?>? recordedTags = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AislePilotTelemetry.MeterName &&
                    instrument.Name == "myblog.aislepilot.background.jobs")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            recordedTags = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
        });
        listener.Start();

        AislePilotTelemetry.RecordBackgroundJob(
            "plan_pool_replenishment",
            TimeSpan.FromSeconds(1),
            success: true,
            requestProfile: new AislePilotTelemetry.BackgroundRequestProfile(
                DietaryConstraintCount: 2,
                MealSlotCount: 7,
                PlanDays: 7,
                PreferQuickMeals: true,
                IncludeSpecialTreat: false));

        Assert.NotNull(recordedTags);
        Assert.Equal("multiple_constraints", recordedTags["dietary_complexity"]);
        Assert.Equal(3, recordedTags["meal_slot_count"]);
        Assert.Equal("standard", recordedTags["plan_length"]);
        Assert.Equal(true, recordedTags["prefer_quick_meals"]);
        Assert.Equal(false, recordedTags["include_special_treat"]);
        Assert.DoesNotContain(recordedTags.Keys, key =>
            key.Contains("allergen", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("pantry", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("supermarket", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("meal_name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryRecordClientPerformance_RecordsAllowlistedMetricWithBoundedTags()
    {
        var measurements = new List<(double Value, string Metric, string NavigationType, bool HasResult)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AislePilotTelemetry.MeterName &&
                    instrument.Name == "myblog.aislepilot.client.performance")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            measurements.Add((
                value,
                values["metric"]?.ToString() ?? string.Empty,
                values["navigation_type"]?.ToString() ?? string.Empty,
                values["has_result"] is true));
        });
        listener.Start();

        var recorded = AislePilotTelemetry.TryRecordClientPerformance("lcp", 1234.5, "navigate", true);

        Assert.True(recorded);
        var measurement = Assert.Single(measurements);
        Assert.Equal(1234.5, measurement.Value);
        Assert.Equal("lcp", measurement.Metric);
        Assert.Equal("navigate", measurement.NavigationType);
        Assert.True(measurement.HasResult);
    }

    [Theory]
    [InlineData("unknown", 10)]
    [InlineData("lcp", -1)]
    [InlineData("lcp", 300001)]
    public void TryRecordClientPerformance_RejectsInvalidPayload(string metric, double value)
    {
        Assert.False(AislePilotTelemetry.TryRecordClientPerformance(metric, value, "navigate", false));
    }

    [Theory]
    [InlineData("image_placeholder_to_image")]
    [InlineData("swap_latency")]
    [InlineData("pantry_latency")]
    [InlineData("save_latency")]
    [InlineData("export_latency")]
    public void TryRecordClientPerformance_AcceptsActionLatencyMetrics(string metric)
    {
        Assert.True(AislePilotTelemetry.TryRecordClientPerformance(metric, 125, "navigate", true));
    }

    [Fact]
    public void RecordPlanStage_RecordsLowCardinalityStageAndSource()
    {
        var measurements = new List<(double Value, string Stage, string Source)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AislePilotTelemetry.MeterName &&
                    instrument.Name == "myblog.aislepilot.plan.stage.duration")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            measurements.Add((
                value,
                values["stage"]?.ToString() ?? string.Empty,
                values["source"]?.ToString() ?? string.Empty));
        });
        listener.Start();

        AislePilotTelemetry.RecordPlanStage("pool_lookup", TimeSpan.FromMilliseconds(250), "pool");

        var measurement = Assert.Single(measurements);
        Assert.Equal(0.25, measurement.Value, 3);
        Assert.Equal("pool_lookup", measurement.Stage);
        Assert.Equal("pool", measurement.Source);
    }

    [Fact]
    public void TryRecordClientEvent_RecordsAllowlistedEventWithoutFreeFormTags()
    {
        var measurements = new List<(long Value, string Event)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AislePilotTelemetry.MeterName &&
                    instrument.Name == "myblog.aislepilot.client.events")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            measurements.Add((value, values["event"]?.ToString() ?? string.Empty));
            Assert.DoesNotContain(values.Keys, key => key.Contains("url", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(values.Keys, key => key.Contains("error", StringComparison.OrdinalIgnoreCase));
        });
        listener.Start();

        var recorded = AislePilotTelemetry.TryRecordClientEvent("setup_abandoned", "navigate", false);

        Assert.True(recorded);
        Assert.Equal((1L, "setup_abandoned"), Assert.Single(measurements));
        Assert.False(AislePilotTelemetry.TryRecordClientEvent("arbitrary-event", "navigate", false));
    }

    [Theory]
    [InlineData("AI meal pool", "memory_pool")]
    [InlineData("Personalised meal plan", "memory_pool")]
    [InlineData("Template fallback", "template")]
    [InlineData("AislePilot recipe plan", "template")]
    [InlineData("OpenAI", "ai")]
    [InlineData("AI/template mix", "mixed")]
    [InlineData("AI/template mix (special treat pending)", "mixed")]
    [InlineData("unexpected free-form label", "other")]
    public void RecordPlanSource_MapsDisplayLabelsToBoundedCategories(string label, string expectedSource)
    {
        var sources = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AislePilotTelemetry.MeterName &&
                    instrument.Name == "myblog.aislepilot.plans.completed")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            sources.Add(values["source"]?.ToString() ?? string.Empty);
        });
        listener.Start();

        AislePilotTelemetry.RecordPlanSource(label);

        Assert.Equal(expectedSource, Assert.Single(sources));
    }
}
