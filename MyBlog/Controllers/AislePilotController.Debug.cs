using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyBlog.Services;

namespace MyBlog.Controllers;

public partial class AislePilotController
{
    public sealed class AislePilotClientPerformanceRequest
    {
        public string? Metric { get; init; }
        public double ValueMilliseconds { get; init; }
        public string? Event { get; init; }
        public string? NavigationType { get; init; }
        public bool HasResult { get; init; }
    }

    [HttpPost("client-performance")]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting("aislePilotTelemetry")]
    public IActionResult ClientPerformance([FromBody] AislePilotClientPerformanceRequest? request)
    {
        if (request is null)
        {
            return BadRequest();
        }

        var recorded = string.IsNullOrWhiteSpace(request.Event)
            ? AislePilotTelemetry.TryRecordClientPerformance(
                request.Metric,
                request.ValueMilliseconds,
                request.NavigationType,
                request.HasResult)
            : AislePilotTelemetry.TryRecordClientEvent(
                request.Event,
                request.NavigationType,
                request.HasResult);
        if (!recorded)
        {
            return BadRequest();
        }

        if (request.Event is "client_error" or "unhandled_rejection")
        {
            logger.LogWarning(
                "AislePilot privacy-safe client failure event. Event={ClientEvent}, RequestId={RequestId}",
                request.Event,
                HttpContext.TraceIdentifier);
        }

        return NoContent();
    }

    public sealed class AislePilotClientDebugLogRequest
    {
        public string? Stage { get; init; }
        public JsonElement Details { get; init; }
        public string? Href { get; init; }
        public string? UserAgent { get; init; }
        public string? TimestampUtc { get; init; }
    }

    [HttpPost("debug-client-log")]
    [IgnoreAntiforgeryToken]
    public IActionResult DebugClientLog([FromBody] AislePilotClientDebugLogRequest? request)
    {
        logger.LogInformation(
            "AislePilot client debug. Stage={Stage}, TimestampUtc={TimestampUtc}, Href={Href}, UserAgent={UserAgent}, Details={Details}",
            request?.Stage ?? string.Empty,
            request?.TimestampUtc ?? string.Empty,
            request?.Href ?? string.Empty,
            request?.UserAgent ?? string.Empty,
            request?.Details.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? string.Empty
                : request?.Details.GetRawText() ?? string.Empty);
        return NoContent();
    }
}
