using System.Diagnostics;
using Microsoft.Extensions.Logging.Console;
using MyBlog.Services;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyBlog.Startup;

internal static class ObservabilityServiceCollectionExtensions
{
    public static WebApplicationBuilder AddMyBlogObservability(
        this WebApplicationBuilder builder,
        string appVersion,
        AppMode appMode)
    {
        var options = ObservabilityOptions.From(builder.Configuration, builder.Environment.EnvironmentName, appMode, appVersion);
        builder.Services.AddSingleton(options);

        ConfigureStructuredLogging(builder, options);
        ConfigureOpenTelemetry(builder, options);

        return builder;
    }

    private static void ConfigureStructuredLogging(WebApplicationBuilder builder, ObservabilityOptions options)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(consoleOptions =>
        {
            consoleOptions.IncludeScopes = true;
            consoleOptions.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            consoleOptions.UseUtcTimestamp = true;
            consoleOptions.JsonWriterOptions = new System.Text.Json.JsonWriterOptions
            {
                Indented = false
            };
        });
        var localRunLogOptions = LocalRunLogOptions.From(builder.Configuration, builder.Environment.EnvironmentName);
        var localRunLogProvider = LocalRunFileLoggerProvider.TryCreate(localRunLogOptions, builder.Environment.ContentRootPath);
        if (localRunLogProvider is not null)
        {
            builder.Logging.AddProvider(localRunLogProvider);
        }
        builder.Logging.Configure(loggingOptions =>
        {
            loggingOptions.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId |
                ActivityTrackingOptions.SpanId |
                ActivityTrackingOptions.ParentId |
                ActivityTrackingOptions.TraceState |
                ActivityTrackingOptions.TraceFlags |
                ActivityTrackingOptions.Tags |
                ActivityTrackingOptions.Baggage;
        });

        if (!options.OtlpEnabled)
        {
            return;
        }

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            logging.ParseStateValues = true;
            logging.SetResourceBuilder(BuildResourceBuilder(options));

            if (!options.ExportLogs)
            {
                return;
            }

            logging.AddOtlpExporter(otlpOptions =>
            {
                ConfigureOtlpOptionsForSignal(otlpOptions, options, "logs");
            });
        });
    }

    private static void ConfigureOpenTelemetry(WebApplicationBuilder builder, ObservabilityOptions options)
    {
        var telemetry = builder.Services.AddOpenTelemetry();
        telemetry.ConfigureResource(resourceBuilder =>
        {
            resourceBuilder
                .AddService(
                    serviceName: options.ServiceName,
                    serviceVersion: options.ServiceVersion,
                    serviceInstanceId: options.ServiceInstanceId)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment.name", options.EnvironmentName),
                    new KeyValuePair<string, object>("service.namespace", "markgolley.dev")
                ]);
        });

        telemetry.WithTracing(traceBuilder =>
        {
            traceBuilder
                .AddAspNetCoreInstrumentation(traceOptions =>
                {
                    traceOptions.RecordException = true;
                    traceOptions.Filter = context => !context.Request.Path.StartsWithSegments("/favicon.ico");
                })
                .AddHttpClientInstrumentation(httpOptions =>
                {
                    httpOptions.RecordException = true;
                })
                .AddSource(MyBlogTelemetry.ActivitySourceName, AislePilotTelemetry.ActivitySourceName);

            if (options.OtlpEnabled && options.ExportTraces)
            {
                traceBuilder.AddOtlpExporter(otlpOptions =>
                {
                    ConfigureOtlpOptionsForSignal(otlpOptions, options, "traces");
                });
            }
        });

        telemetry.WithMetrics(metricBuilder =>
        {
            metricBuilder
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(MyBlogTelemetry.MeterName, AislePilotTelemetry.MeterName)
                .AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10]
                });

            if (options.OtlpEnabled && options.ExportMetrics)
            {
                metricBuilder.AddOtlpExporter(otlpOptions =>
                {
                    ConfigureOtlpOptionsForSignal(otlpOptions, options, "metrics");
                });
            }
        });
    }

    private static ResourceBuilder BuildResourceBuilder(ObservabilityOptions options)
    {
        return ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: options.ServiceName,
                serviceVersion: options.ServiceVersion,
                serviceInstanceId: options.ServiceInstanceId)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment.name", options.EnvironmentName),
                new KeyValuePair<string, object>("service.namespace", "markgolley.dev")
            ]);
    }

    private static void ConfigureOtlpOptionsForSignal(
        OtlpExporterOptions otlpOptions,
        ObservabilityOptions options,
        string signalName)
    {
        otlpOptions.Protocol = options.OtlpProtocol;
        otlpOptions.Endpoint = options.BuildSignalEndpoint(signalName);
    }

    private sealed record ObservabilityOptions(
        string ServiceName,
        string ServiceVersion,
        string ServiceInstanceId,
        string EnvironmentName,
        bool OtlpEnabled,
        OtlpExportProtocol OtlpProtocol,
        Uri OtlpEndpoint,
        bool ExportTraces,
        bool ExportMetrics,
        bool ExportLogs)
    {
        public static ObservabilityOptions From(
            IConfiguration configuration,
            string environmentName,
            AppMode appMode,
            string appVersion)
        {
            var configuredEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
                                   ?? configuration["Observability:OtlpEndpoint"];
            var otlpEndpoint = ResolveEndpoint(configuredEndpoint);
            var otlpEnabled = GetBool(
                                  Environment.GetEnvironmentVariable("OBSERVABILITY__ENABLE_OTLP")
                                  ?? configuration["Observability:EnableOtlp"],
                                  defaultValue: !string.IsNullOrWhiteSpace(configuredEndpoint))
                              && otlpEndpoint is not null;
            var exportTraces = GetBool(
                Environment.GetEnvironmentVariable("OBSERVABILITY__EXPORT_TRACES")
                ?? configuration["Observability:ExportTraces"],
                defaultValue: true);
            var exportMetrics = GetBool(
                Environment.GetEnvironmentVariable("OBSERVABILITY__EXPORT_METRICS")
                ?? configuration["Observability:ExportMetrics"],
                defaultValue: true);
            var exportLogs = GetBool(
                Environment.GetEnvironmentVariable("OBSERVABILITY__EXPORT_LOGS")
                ?? configuration["Observability:ExportLogs"],
                defaultValue: true);
            var protocolText = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL")
                               ?? configuration["Observability:OtlpProtocol"]
                               ?? "http/protobuf";
            var protocol = ResolveProtocol(protocolText);

            return new ObservabilityOptions(
                ServiceName: ResolveServiceName(appMode),
                ServiceVersion: string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion,
                ServiceInstanceId: Environment.GetEnvironmentVariable("K_REVISION")
                                   ?? Environment.GetEnvironmentVariable("HOSTNAME")
                                   ?? Environment.MachineName,
                EnvironmentName: string.IsNullOrWhiteSpace(environmentName) ? "unknown" : environmentName,
                OtlpEnabled: otlpEnabled,
                OtlpProtocol: protocol,
                OtlpEndpoint: otlpEndpoint ?? new Uri("http://localhost:4318"),
                ExportTraces: exportTraces,
                ExportMetrics: exportMetrics,
                ExportLogs: exportLogs);
        }

        public Uri BuildSignalEndpoint(string signalName)
        {
            if (OtlpProtocol == OtlpExportProtocol.Grpc)
            {
                return OtlpEndpoint;
            }

            var normalizedSignal = signalName.Trim().ToLowerInvariant();
            var builder = new UriBuilder(OtlpEndpoint);
            var path = builder.Path.TrimEnd('/');
            if (path.EndsWith($"/v1/{normalizedSignal}", StringComparison.OrdinalIgnoreCase))
            {
                return builder.Uri;
            }

            builder.Path = $"{path}/v1/{normalizedSignal}".Replace("//", "/");
            return builder.Uri;
        }

        private static string ResolveServiceName(AppMode appMode)
        {
            return appMode switch
            {
                AppMode.BlogOnly => "myblog-blog",
                AppMode.AislePilotOnly => "myblog-aislepilot",
                _ => "myblog-app"
            };
        }

        private static bool GetBool(string? value, bool defaultValue)
        {
            return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
        }

        private static OtlpExportProtocol ResolveProtocol(string? rawValue)
        {
            if (string.Equals(rawValue, "grpc", StringComparison.OrdinalIgnoreCase))
            {
                return OtlpExportProtocol.Grpc;
            }

            return OtlpExportProtocol.HttpProtobuf;
        }

        private static Uri? ResolveEndpoint(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            var trimmed = rawValue.Trim();
            return Uri.TryCreate(trimmed, UriKind.Absolute, out var endpoint)
                ? endpoint
                : null;
        }
    }
}
