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
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private async Task<string?> SendOpenAiRequestWithRetryAsync(
        object requestBody,
        CancellationToken cancellationToken,
        string operation = "chat_completions",
        string? model = null)
    {
        if (_httpClient is null || string.IsNullOrWhiteSpace(_apiKey))
        {
            return null;
        }

        var serializedBody = JsonSerializer.Serialize(requestBody);
        var resolvedModel = string.IsNullOrWhiteSpace(model) ? _model : model!;
        var maxAttempts = OpenAiMaxAttempts;
        using var activity = AislePilotTelemetry.StartActivity("ai.aislepilot.request", ActivityKind.Client);
        activity?.SetTag("ai.provider", "openai");
        activity?.SetTag("ai.operation", operation);
        activity?.SetTag("ai.model", resolvedModel);
        activity?.SetTag("ai.max_attempts", maxAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var requestStopwatch = Stopwatch.StartNew();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(OpenAiRequestTimeout);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, OpenAiChatCompletionsEndpoint)
            {
                Content = new StringContent(serializedBody, Encoding.UTF8, "application/json")
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            try
            {
                using var response = await _httpClient.SendAsync(requestMessage, timeoutCts.Token);
                var responseContent = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                if (response.IsSuccessStatusCode)
                {
                    RecordAislePilotAiRequest(
                        operation,
                        resolvedModel,
                        requestStopwatch.Elapsed,
                        success: true,
                        responseContent: responseContent,
                        promptText: serializedBody);
                    activity?.SetTag("ai.success", true);
                    activity?.SetTag("ai.attempt", attempt);
                    _logger?.LogInformation(
                        "AislePilot OpenAI call completed in {ElapsedMs}ms. Attempt={Attempt}/{MaxAttempts}. Operation={Operation}, Model={Model}",
                        requestStopwatch.ElapsedMilliseconds,
                        attempt,
                        maxAttempts,
                        operation,
                        resolvedModel);
                    return responseContent;
                }

                var shouldRetry = attempt < maxAttempts && IsTransientOpenAiStatus(response.StatusCode);
                var errorSample = responseContent.Length <= 220 ? responseContent : responseContent[..220];
                RecordAislePilotAiRequest(
                    operation,
                    resolvedModel,
                    requestStopwatch.Elapsed,
                    success: false,
                    responseContent: responseContent,
                    promptText: serializedBody,
                    errorType: $"http_{(int)response.StatusCode}");
                _logger?.LogWarning(
                    "AislePilot OpenAI call failed with status {StatusCode} after {ElapsedMs}ms. Attempt={Attempt}/{MaxAttempts}. Operation={Operation}, Model={Model}, ResponseSample={ResponseSample}",
                    (int)response.StatusCode,
                    requestStopwatch.ElapsedMilliseconds,
                    attempt,
                    maxAttempts,
                    operation,
                    resolvedModel,
                    errorSample);

                if (!shouldRetry)
                {
                    activity?.SetTag("ai.success", false);
                    activity?.SetTag("ai.error_type", $"http_{(int)response.StatusCode}");
                    return null;
                }

                var delay = GetRetryDelay(response, attempt);
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                RecordAislePilotAiRequest(
                    operation,
                    resolvedModel,
                    requestStopwatch.Elapsed,
                    success: false,
                    promptText: serializedBody,
                    errorType: "timeout");
                _logger?.LogWarning(
                    "AislePilot OpenAI call timed out after {TimeoutSeconds}s. Attempt={Attempt}/{MaxAttempts}. Operation={Operation}, Model={Model}, ElapsedMs={ElapsedMs}",
                    OpenAiRequestTimeout.TotalSeconds,
                    attempt,
                    maxAttempts,
                    operation,
                    resolvedModel,
                    requestStopwatch.ElapsedMilliseconds);

                if (attempt >= maxAttempts)
                {
                    activity?.SetTag("ai.success", false);
                    activity?.SetTag("ai.error_type", "timeout");
                    return null;
                }
            }
            catch (HttpRequestException ex)
            {
                RecordAislePilotAiRequest(
                    operation,
                    resolvedModel,
                    requestStopwatch.Elapsed,
                    success: false,
                    promptText: serializedBody,
                    errorType: ex.GetType().Name);
                _logger?.LogWarning(
                    ex,
                    "AislePilot OpenAI HTTP request failed after {ElapsedMs}ms. Attempt={Attempt}/{MaxAttempts}. Operation={Operation}, Model={Model}.",
                    requestStopwatch.ElapsedMilliseconds,
                    attempt,
                    maxAttempts,
                    operation,
                    resolvedModel);

                if (attempt >= maxAttempts)
                {
                    activity?.SetTag("ai.success", false);
                    activity?.SetTag("ai.error_type", ex.GetType().Name);
                    return null;
                }
            }
        }

        activity?.SetTag("ai.success", false);
        activity?.SetTag("ai.error_type", "exhausted_retries");
        return null;
    }

    private static bool IsTransientOpenAiStatus(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests ||
               statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout ||
               (int)statusCode >= 500;
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta <= MaxOpenAiRetryAfterDelay ? delta : MaxOpenAiRetryAfterDelay;
        }

        if (retryAfter?.Date is DateTimeOffset date)
        {
            var computed = date - DateTimeOffset.UtcNow;
            if (computed > TimeSpan.Zero)
            {
                return computed <= MaxOpenAiRetryAfterDelay ? computed : MaxOpenAiRetryAfterDelay;
            }
        }

        return attempt == 1 ? TimeSpan.FromSeconds(1.5) : TimeSpan.FromSeconds(3);
    }

    private static bool IsJsonNumberTokenStart(string json, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var ch = json[i];
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            return ch is ':' or '[' or ',';
        }

        return true;
    }

    private static bool TryReadJsonNumberToken(
        string json,
        int index,
        out int tokenLength,
        out string normalizedToken)
    {
        tokenLength = 0;
        normalizedToken = string.Empty;
        var cursor = index;
        var sign = string.Empty;

        if (cursor < json.Length && json[cursor] == '-')
        {
            sign = "-";
            cursor++;
        }

        var integralStart = cursor;
        while (cursor < json.Length && char.IsDigit(json[cursor]))
        {
            cursor++;
        }

        if (cursor == integralStart)
        {
            return false;
        }

        var integralDigits = json[integralStart..cursor];
        var fractionalPart = string.Empty;
        var exponentPart = string.Empty;

        if (cursor < json.Length && json[cursor] == '.')
        {
            var fractionalStart = cursor;
            cursor++;
            var fractionalDigitsStart = cursor;
            while (cursor < json.Length && char.IsDigit(json[cursor]))
            {
                cursor++;
            }

            if (cursor == fractionalDigitsStart)
            {
                return false;
            }

            fractionalPart = json[fractionalStart..cursor];
        }

        if (cursor < json.Length && (json[cursor] == 'e' || json[cursor] == 'E'))
        {
            var exponentStart = cursor;
            cursor++;
            if (cursor < json.Length && (json[cursor] == '+' || json[cursor] == '-'))
            {
                cursor++;
            }

            var exponentDigitsStart = cursor;
            while (cursor < json.Length && char.IsDigit(json[cursor]))
            {
                cursor++;
            }

            if (cursor == exponentDigitsStart)
            {
                return false;
            }

            exponentPart = json[exponentStart..cursor];
        }

        var normalizedIntegral = integralDigits;
        if (integralDigits.Length > 1 && integralDigits[0] == '0')
        {
            normalizedIntegral = integralDigits.TrimStart('0');
            if (normalizedIntegral.Length == 0)
            {
                normalizedIntegral = "0";
            }
        }

        tokenLength = cursor - index;
        normalizedToken = sign + normalizedIntegral + fractionalPart + exponentPart;
        return true;
    }

}
