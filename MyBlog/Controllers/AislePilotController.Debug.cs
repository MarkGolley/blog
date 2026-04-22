using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace MyBlog.Controllers;

public partial class AislePilotController
{
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
