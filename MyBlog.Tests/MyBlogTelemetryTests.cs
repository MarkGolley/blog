using System.Diagnostics.Metrics;
using MyBlog.Services;

namespace MyBlog.Tests;

public sealed class MyBlogTelemetryTests
{
    [Fact]
    public void RecordRequestLifecycle_EmitsBalancedActiveRequestMeasurementsWithoutEndpointTag()
    {
        var activeRequestMeasurements = new List<(long Value, string? EndpointTag)>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == MyBlogTelemetry.MeterName &&
                    string.Equals(instrument.Name, "myblog.http.active_requests", StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (!string.Equals(instrument.Name, "myblog.http.active_requests", StringComparison.Ordinal))
            {
                return;
            }

            string? endpointTag = null;
            foreach (var tag in tags)
            {
                if (string.Equals(tag.Key, "endpoint", StringComparison.Ordinal))
                {
                    endpointTag = tag.Value?.ToString();
                    break;
                }
            }

            activeRequestMeasurements.Add((measurement, endpointTag));
        });
        listener.Start();

        MyBlogTelemetry.RecordRequestStarted();
        MyBlogTelemetry.RecordRequestCompleted("GET /health", "GET", 200, durationMs: 12.4, isError: false);

        Assert.Contains(activeRequestMeasurements, measurement => measurement.Value == 1);
        Assert.Contains(activeRequestMeasurements, measurement => measurement.Value == -1);
        Assert.DoesNotContain(activeRequestMeasurements, measurement => !string.IsNullOrWhiteSpace(measurement.EndpointTag));
    }

    [Fact]
    public void RecordRequestCompleted_EmitsRequestDurationWithMethodStatusAndErrorTags()
    {
        (double Value, string? Endpoint, string? Method, int? StatusCode, bool? Error)? measurement = null;

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == MyBlogTelemetry.MeterName &&
                    string.Equals(instrument.Name, "myblog.http.request.duration", StringComparison.Ordinal))
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, recordedValue, tags, _) =>
        {
            if (!string.Equals(instrument.Name, "myblog.http.request.duration", StringComparison.Ordinal))
            {
                return;
            }

            string? endpoint = null;
            string? method = null;
            int? statusCode = null;
            bool? error = null;

            foreach (var tag in tags)
            {
                switch (tag.Key)
                {
                    case "endpoint":
                        endpoint = tag.Value?.ToString();
                        break;
                    case "method":
                        method = tag.Value?.ToString();
                        break;
                    case "status_code" when tag.Value is int parsedStatusCode:
                        statusCode = parsedStatusCode;
                        break;
                    case "error" when tag.Value is bool parsedError:
                        error = parsedError;
                        break;
                }
            }

            measurement = (recordedValue, endpoint, method, statusCode, error);
        });
        listener.Start();

        MyBlogTelemetry.RecordRequestCompleted(
            "MyBlog.Controllers.AislePilotController.Index (MyBlog)",
            "POST",
            503,
            durationMs: 31.2,
            isError: true);

        Assert.NotNull(measurement);
        Assert.Equal("MyBlog.Controllers.AislePilotController.Index (MyBlog)", measurement.Value.Endpoint);
        Assert.Equal("POST", measurement.Value.Method);
        Assert.Equal(503, measurement.Value.StatusCode);
        Assert.True(measurement.Value.Error);
    }
}
