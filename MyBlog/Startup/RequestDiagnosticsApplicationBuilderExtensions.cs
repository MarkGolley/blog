using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MyBlog.Services;

namespace MyBlog.Startup;

internal static class RequestDiagnosticsApplicationBuilderExtensions
{
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    public static IApplicationBuilder UseRequestDiagnostics(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("MyBlog.RequestDiagnostics");

            var correlationId = ResolveCorrelationId(context.Request.Headers[CorrelationIdHeaderName].FirstOrDefault());
            context.Items[CorrelationIdHeaderName] = correlationId;
            context.Response.Headers[CorrelationIdHeaderName] = correlationId;

            Activity.Current?.SetTag("correlation.id", correlationId);

            var requestId = context.TraceIdentifier;
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? string.Empty;
            var userIdHash = HashIdentifier(context.User?.Identity?.Name);
            var sessionIdHash = ResolveSessionIdHash(context);
            var stopwatch = Stopwatch.StartNew();
            var requestFailed = false;
            Exception? capturedException = null;

            MyBlogTelemetry.RecordRequestStarted();
            using (logger.BeginScope(new Dictionary<string, object?>
                   {
                       ["correlation_id"] = correlationId,
                       ["request_id"] = requestId,
                       ["user_id_hash"] = userIdHash,
                       ["session_id_hash"] = sessionIdHash
                   }))
            {
                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    requestFailed = true;
                    capturedException = ex;
                    throw;
                }
                finally
                {
                    stopwatch.Stop();
                    var endpointName = ResolveEndpointName(context.GetEndpoint(), method, path);
                    var statusCode = requestFailed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
                    var isError = requestFailed || statusCode >= 500;
                    var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;

                    MyBlogTelemetry.RecordRequestCompleted(
                        endpointName,
                        method,
                        statusCode,
                        stopwatch.Elapsed.TotalMilliseconds,
                        isError);

                    logger.LogInformation(
                        "HTTP request completed. Method={Method} Path={Path} Endpoint={Endpoint} StatusCode={StatusCode} LatencyMs={LatencyMs} TraceId={TraceId} CorrelationId={CorrelationId} RequestId={RequestId} UserIdHash={UserIdHash} SessionIdHash={SessionIdHash} ExceptionType={ExceptionType}",
                        method,
                        path,
                        endpointName,
                        statusCode,
                        Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                        traceId,
                        correlationId,
                        requestId,
                        userIdHash,
                        sessionIdHash,
                        capturedException?.GetType().Name ?? string.Empty);
                }
            }
        });
    }

    private static string ResolveEndpointName(Endpoint? endpoint, string method, string path)
    {
        if (!string.IsNullOrWhiteSpace(endpoint?.DisplayName))
        {
            return endpoint.DisplayName!;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path;
        if (normalizedPath.Length > 64)
        {
            normalizedPath = normalizedPath[..64];
        }

        return $"{method.ToUpperInvariant()} {normalizedPath}";
    }

    private static string ResolveCorrelationId(string? suppliedValue)
    {
        if (!string.IsNullOrWhiteSpace(suppliedValue))
        {
            var candidate = suppliedValue.Trim();
            if (candidate.Length <= 64)
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static string ResolveSessionIdHash(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue("__session", out var authSession) &&
            !string.IsNullOrWhiteSpace(authSession))
        {
            return HashIdentifier(authSession);
        }

        if (context.Request.Cookies.TryGetValue("myblog_visitor_id", out var visitorId) &&
            !string.IsNullOrWhiteSpace(visitorId))
        {
            return HashIdentifier(visitorId);
        }

        return string.Empty;
    }

    private static string HashIdentifier(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawValue.Trim()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
