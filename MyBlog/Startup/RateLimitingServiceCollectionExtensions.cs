using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace MyBlog.Startup;

internal static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddMyBlogRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var request = context.HttpContext.Request;
                var isAjaxRequest = string.Equals(
                    request.Headers["X-Requested-With"],
                    "XMLHttpRequest",
                    StringComparison.OrdinalIgnoreCase);

                if (isAjaxRequest)
                {
                    context.HttpContext.Response.ContentType = "application/json";
                    await context.HttpContext.Response.WriteAsync(
                        "{\"success\":false,\"error\":\"Too many requests. Please try again shortly.\"}",
                        cancellationToken);
                    return;
                }

                context.HttpContext.Response.ContentType = "text/plain";
                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Please try again shortly.",
                    cancellationToken);
            };

            options.AddPolicy("commentWrites", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 6,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("likeWrites", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 50,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("contactWrites", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromMinutes(10),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("subscriptionWrites", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(10),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("aislePilotWrites", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 45,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy("aislePilotAdminWarmupWrites", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: GetRateLimitPartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 2,
                        Window = TimeSpan.FromMinutes(10),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    private static string GetRateLimitPartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString();

        if (string.IsNullOrWhiteSpace(ip))
        {
            ip = "unknown-ip";
        }

        var userAgentBucket = GetUserAgentBucket(userAgent);

        return $"{ip}|{userAgentBucket}";
    }

    private static string GetUserAgentBucket(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "unknown-ua";
        }

        var ua = userAgent.ToLowerInvariant();
        if (ua.Contains("bot", StringComparison.Ordinal) || ua.Contains("crawler", StringComparison.Ordinal))
        {
            return "bot";
        }

        if (ua.Contains("postmanruntime/", StringComparison.Ordinal))
        {
            return "postman";
        }

        if (ua.Contains("curl/", StringComparison.Ordinal))
        {
            return "curl";
        }

        if (ua.Contains("python-requests", StringComparison.Ordinal))
        {
            return "python-requests";
        }

        if (ua.Contains("edg/", StringComparison.Ordinal))
        {
            return "edge";
        }

        if (ua.Contains("opr/", StringComparison.Ordinal) || ua.Contains("opera/", StringComparison.Ordinal))
        {
            return "opera";
        }

        if (ua.Contains("firefox/", StringComparison.Ordinal))
        {
            return "firefox";
        }

        if (ua.Contains("chrome/", StringComparison.Ordinal))
        {
            return "chrome";
        }

        if (ua.Contains("safari/", StringComparison.Ordinal) && ua.Contains("version/", StringComparison.Ordinal))
        {
            return "safari";
        }

        return "other";
    }
}
