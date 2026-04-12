using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyBlog.Models;
using MyBlog.Utilities;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private static string NormalizeModelJson(string rawJson)
    {
        var trimmed = rawJson.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                trimmed = trimmed[..fenceEnd];
            }
        }

        return trimmed.Trim();
    }

    private static AislePilotAiPlanPayload? ParseAiPlanPayload(string normalizedJson)
    {
        using var doc = JsonDocument.Parse(normalizedJson);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("meals", out var mealsElement) &&
            mealsElement.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<AislePilotAiPlanPayload>(normalizedJson, JsonOptions);
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("meal", out var mealElement) &&
            mealElement.ValueKind == JsonValueKind.Object)
        {
            var meal = JsonSerializer.Deserialize<AislePilotAiMealPayload>(mealElement.GetRawText(), JsonOptions);
            if (meal is null)
            {
                return null;
            }

            return new AislePilotAiPlanPayload
            {
                Meals = [meal]
            };
        }

        if (root.ValueKind == JsonValueKind.Object &&
            (root.TryGetProperty("name", out _) || root.TryGetProperty("ingredients", out _)))
        {
            var meal = JsonSerializer.Deserialize<AislePilotAiMealPayload>(normalizedJson, JsonOptions);
            if (meal is null)
            {
                return null;
            }

            return new AislePilotAiPlanPayload
            {
                Meals = [meal]
            };
        }

        return JsonSerializer.Deserialize<AislePilotAiPlanPayload>(normalizedJson, JsonOptions);
    }

    private static bool TryParseAiPlanPayloadWithRecovery(
        string normalizedJson,
        out AislePilotAiPlanPayload? aiPayload,
        out string? repairedJson)
    {
        aiPayload = null;
        repairedJson = null;

        try
        {
            aiPayload = ParseAiPlanPayload(normalizedJson);
            return aiPayload is not null;
        }
        catch (JsonException)
        {
            if (!TryRepairMalformedJson(normalizedJson, out var repaired))
            {
                return false;
            }

            try
            {
                aiPayload = ParseAiPlanPayload(repaired);
                repairedJson = repaired;
                return aiPayload is not null;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    private static bool TryParseAiMealPayloadWithRecovery(
        string normalizedJson,
        out AislePilotAiMealPayload? aiPayload)
    {
        aiPayload = null;

        try
        {
            aiPayload = JsonSerializer.Deserialize<AislePilotAiMealPayload>(normalizedJson, JsonOptions);
            return aiPayload is not null;
        }
        catch (JsonException)
        {
            if (!TryRepairMalformedJson(normalizedJson, out var repaired))
            {
                return false;
            }

            try
            {
                aiPayload = JsonSerializer.Deserialize<AislePilotAiMealPayload>(repaired, JsonOptions);
                return aiPayload is not null;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    private static bool TryRepairMalformedJson(string input, out string repaired)
    {
        repaired = input;
        var updated = input;

        var trailingCommaFixed = TrailingCommaRegex.Replace(updated, string.Empty);
        if (!ReferenceEquals(trailingCommaFixed, updated))
        {
            updated = trailingCommaFixed;
        }

        var leadingZeroFixed = NormalizeLeadingZeroNumbers(updated);
        if (!string.Equals(leadingZeroFixed, updated, StringComparison.Ordinal))
        {
            updated = leadingZeroFixed;
        }

        if (string.Equals(updated, input, StringComparison.Ordinal))
        {
            return false;
        }

        repaired = updated;
        return true;
    }

    private static string NormalizeLeadingZeroNumbers(string json)
    {
        var result = new StringBuilder(json.Length);
        var inString = false;
        var isEscaped = false;

        for (var i = 0; i < json.Length; i++)
        {
            var ch = json[i];
            if (inString)
            {
                result.Append(ch);
                if (isEscaped)
                {
                    isEscaped = false;
                }
                else if (ch == '\\')
                {
                    isEscaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                result.Append(ch);
                continue;
            }

            if ((ch == '-' || char.IsDigit(ch)) &&
                IsJsonNumberTokenStart(json, i) &&
                TryReadJsonNumberToken(json, i, out var tokenLength, out var normalizedToken))
            {
                result.Append(normalizedToken);
                i += tokenLength - 1;
                continue;
            }

            result.Append(ch);
        }

        return result.ToString();
    }

    private static string NormalizeAiDepartment(string? department)
    {
        var normalized = ClampAndNormalize(department, MaxAiDepartmentLength);
        return DefaultAisleOrder.FirstOrDefault(item =>
                   item.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? string.Empty;
    }

    private static string ClampAndNormalizeDepartmentName(string? department)
    {
        var normalized = ClampAndNormalize(department, MaxAiDepartmentLength);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var normalizedKey = NormalizePantryText(normalized);
        if (!string.IsNullOrWhiteSpace(normalizedKey) &&
            AisleOrderAliases.TryGetValue(normalizedKey, out var mappedAlias))
        {
            return mappedAlias;
        }

        return DefaultAisleOrder.FirstOrDefault(item =>
                   item.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? normalized;
    }

    private static string ClampAndNormalize(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = string.Join(
            ' ',
            input.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength].TrimEnd();
    }

}
