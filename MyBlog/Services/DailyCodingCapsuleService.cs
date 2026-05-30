using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Diagnostics;
using Google.Cloud.Firestore;
using System.Net.Http.Headers;
using System.Text.Json;
using MyBlog.Models;

namespace MyBlog.Services;

public sealed class DailyCodingCapsuleService : IDailyCodingCapsuleProvider
{
    private const string DailyCapsulesCollection = "dailyCapsules";
    private const int OpenAiMaxAttempts = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly CapsuleSeed[] FallbackCapsules =
    {
        new(
            "Insight",
            "Bias for readability",
            "Code gets read far more than it gets written. Name things so intent is obvious on first pass.",
            "Rename method `Handle()` to `HandleSubscriptionRenewal()`."),
        new(
            "Tip",
            "Shrink the blast radius",
            "Before a risky refactor, wrap old behavior behind one interface so rollback is a single switch.",
            "Create `IInvoicePricer` and toggle old/new implementations with config."),
        new(
            "Fact",
            "Latency compounds quickly",
            "Three 120ms sequential calls already cost about 360ms before rendering. Parallelize independent IO.",
            "Use `Task.WhenAll(profileTask, settingsTask, alertsTask)`."),
        new(
            "Snippet",
            "Guard clause mindset",
            "Guard clauses reduce nesting and keep the happy path readable.",
            "if (!isValid) return;"),
        new(
            "Insight",
            "Tests are design feedback",
            "If a unit test is painful, your production API is often doing too much in one place.",
            "Split one class with 7 ctor dependencies into orchestrator + focused services."),
        new(
            "Quote",
            "Simple survives scale",
            "Complexity has an ongoing tax. Boring, understandable systems usually fail less and recover faster.",
            "Prefer one queue + clear retries over a bespoke event choreography.")
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<DailyCodingCapsuleService> _logger;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly bool _enableAiGeneration;
    private readonly bool _enableHistoryFallback;
    private readonly TimeZoneInfo _ukTimeZone;
    private readonly FirestoreDb? _db;
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static readonly ConcurrentDictionary<DateOnly, CapsuleCacheEntry> CacheByDate = new();
    private static readonly object OperationalStateLock = new();
    private static DateTimeOffset? _aiRateLimitBackoffUntilUtc;
    private static DateTime? _lastGenerationAttemptUtc;
    private static DateTime? _lastGenerationSuccessUtc;
    private static DateTime? _lastGenerationFailureUtc;
    private static string? _lastGenerationFailureReason;
    private static DateTime? _lastPersistFailureUtc;
    private static string? _lastPersistFailureReason;
    private static string? _lastServedUkDate;
    private static string? _lastServedSource;

    public DailyCodingCapsuleService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<DailyCodingCapsuleService> logger,
        FirestoreDb? db = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _db = db;
        _apiKey = configuration["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        _model = configuration["DailyCapsule:Model"] ?? "gpt-4.1-mini";
        _enableAiGeneration = GetBool(configuration["DailyCapsule:EnableAiGeneration"], defaultValue: true);
        _enableHistoryFallback = GetBool(configuration["DailyCapsule:EnableHistoryFallback"], defaultValue: false);
        _ukTimeZone = ResolveUkTimeZone(logger);
    }

    public async Task<DailyCodingCapsuleViewModel> GetCapsuleForCurrentDayAsync(CancellationToken cancellationToken = default)
    {
        return await GetCapsuleForOffsetDaysAsync(0, cancellationToken);
    }

    public async Task<DailyCodingCapsuleViewModel> GetCapsuleForOffsetDaysAsync(int offsetDays, CancellationToken cancellationToken = default)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var ukNow = TimeZoneInfo.ConvertTime(nowUtc, _ukTimeZone);
        var todayUkDate = DateOnly.FromDateTime(ukNow.DateTime);
        var boundedOffset = Math.Clamp(offsetDays, -7, 0);
        var targetUkDate = todayUkDate.AddDays(boundedOffset);
        var nextResetUtc = GetNextResetUtc(targetUkDate);

        if (TryGetCachedCapsule(targetUkDate, out var cachedCapsule))
        {
            return ToViewModelAndRecordServe(cachedCapsule, nextResetUtc);
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCachedCapsule(targetUkDate, out cachedCapsule))
            {
                return ToViewModelAndRecordServe(cachedCapsule, nextResetUtc);
            }

            var persisted = await TryGetPersistedCapsuleAsync(targetUkDate, cancellationToken);
            if (persisted is not null)
            {
                CacheByDate[targetUkDate] = persisted;
                return ToViewModelAndRecordServe(persisted, nextResetUtc);
            }

            var capsule = await CreateCapsuleAsync(targetUkDate, cancellationToken);

            if (targetUkDate == todayUkDate)
            {
                capsule = await PersistTodayCapsuleAtomicallyAsync(targetUkDate, capsule);
            }

            CacheByDate[targetUkDate] = capsule;
            return ToViewModelAndRecordServe(capsule, nextResetUtc);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    public async Task<DailyCodingCapsuleViewModel?> TryGetStoredCapsuleForOffsetDaysAsync(
        int offsetDays,
        CancellationToken cancellationToken = default)
    {
        if (offsetDays > 0)
        {
            return null;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var ukNow = TimeZoneInfo.ConvertTime(nowUtc, _ukTimeZone);
        var todayUkDate = DateOnly.FromDateTime(ukNow.DateTime);
        var boundedOffset = Math.Clamp(offsetDays, -7, 0);
        var targetUkDate = todayUkDate.AddDays(boundedOffset);

        if (!TryGetCachedCapsule(targetUkDate, out var cachedCapsule))
        {
            var persisted = _db is null
                ? null
                : await TryGetPersistedCapsuleAsync(targetUkDate, cancellationToken);

            if (persisted is not null)
            {
                CacheByDate[targetUkDate] = persisted;
                cachedCapsule = persisted;
            }
            else if (_enableHistoryFallback && boundedOffset < 0)
            {
                return await GetCapsuleForOffsetDaysAsync(boundedOffset, cancellationToken);
            }
            else
            {
                return null;
            }
        }

        return ToViewModel(cachedCapsule, GetNextResetUtc(targetUkDate));
    }

    private async Task<CapsuleCacheEntry> CreateCapsuleAsync(
        DateOnly targetUkDate,
        CancellationToken cancellationToken)
    {
        var aiCapsule = await TryGenerateCapsuleWithAiAsync(targetUkDate, cancellationToken);
        if (aiCapsule is not null)
        {
            return aiCapsule;
        }

        var fallback = GetFallbackCapsule(targetUkDate);
        _logger.LogInformation(
            "Serving fallback daily capsule for UK date {UkDate}.",
            targetUkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return fallback;
    }

    private async Task<CapsuleCacheEntry?> TryGenerateCapsuleWithAiAsync(DateOnly ukDate, CancellationToken cancellationToken)
    {
        using var activity = MyBlogTelemetry.StartActivity(
            "ai.daily_capsule.generate",
            ActivityKind.Client);
        activity?.SetTag("ai.provider", "openai");
        activity?.SetTag("ai.operation", "daily_capsule");
        activity?.SetTag("ai.model", _model);

        if (!_enableAiGeneration)
        {
            _logger.LogInformation("Daily capsule AI generation is disabled by configuration.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("OPENAI_API_KEY is missing. Daily capsule will use fallback content.");
            RecordGenerationFailure("OPENAI_API_KEY missing.");
            return null;
        }

        if (IsRateLimitBackoffActive(out var rateLimitBackoff))
        {
            _logger.LogInformation(
                "Skipping daily capsule AI generation while OpenAI rate-limit backoff is active for another {RemainingSeconds}s.",
                Math.Max(1, (int)Math.Ceiling(rateLimitBackoff.TotalSeconds)));
            return null;
        }

        try
        {
            RecordGenerationAttempt();

            var prompt = BuildPrompt(ukDate);
            var requestBody = new
            {
                model = _model,
                temperature = 0.85,
                max_tokens = 280,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "You generate one concise coding capsule for an engineering blog homepage. Always use UK English."
                    },
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            var serializedRequestBody = JsonSerializer.Serialize(requestBody);

            HttpResponseMessage? response = null;
            var successfulRequestDuration = TimeSpan.Zero;
            for (var attempt = 1; attempt <= OpenAiMaxAttempts; attempt++)
            {
                var requestStopwatch = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
                {
                    Content = new StringContent(
                        serializedRequestBody,
                        System.Text.Encoding.UTF8,
                        "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    successfulRequestDuration = requestStopwatch.Elapsed;
                    break;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests &&
                    TryGetRetryAfterDelay(response.Headers, out var retryAfterDelay))
                {
                    MyBlogTelemetry.RecordAiRequest(
                        provider: "openai",
                        operation: "daily_capsule",
                        model: _model,
                        duration: requestStopwatch.Elapsed,
                        success: false,
                        errorType: "http_429");
                    var clampedDelay = ClampRateLimitBackoffDelay(retryAfterDelay);
                    RecordRateLimitBackoff(clampedDelay);
                    RecordGenerationFailure(
                        $"OpenAI rate limited daily capsule generation (429). Retry after {Math.Max(1, (int)Math.Ceiling(clampedDelay.TotalSeconds))}s.");
                    _logger.LogWarning(
                        "Daily capsule AI generation hit OpenAI rate limits (429). Using fallback and backing off AI attempts for {RetryAfterSeconds}s.",
                        Math.Max(1, (int)Math.Ceiling(clampedDelay.TotalSeconds)));
                    response.Dispose();
                    return null;
                }

                if (attempt >= OpenAiMaxAttempts || !IsTransientStatus(response.StatusCode))
                {
                    MyBlogTelemetry.RecordAiRequest(
                        provider: "openai",
                        operation: "daily_capsule",
                        model: _model,
                        duration: requestStopwatch.Elapsed,
                        success: false,
                        errorType: $"http_{(int)response.StatusCode}");
                    RecordGenerationFailure(
                        $"OpenAI returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "Unknown"}).");
                    _logger.LogWarning(
                        "Daily capsule AI generation returned non-success status {StatusCode} ({ReasonPhrase}). Using fallback.",
                        (int)response.StatusCode,
                        response.ReasonPhrase ?? "Unknown");
                    response.Dispose();
                    return null;
                }

                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt), cancellationToken);
                response = null;
            }

            if (response is null)
            {
                RecordGenerationFailure("OpenAI response unavailable after retries.");
                return null;
            }

            using (response)
            {
                var responseContent = await response!.Content.ReadAsStringAsync(cancellationToken);
                var payload = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent, JsonOptions);
                var rawJson = payload?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(rawJson))
                {
                    MyBlogTelemetry.RecordAiRequest(
                        provider: "openai",
                        operation: "daily_capsule",
                        model: _model,
                        duration: successfulRequestDuration,
                        success: false,
                        errorType: "empty_content");
                    _logger.LogWarning("Daily capsule generation returned an empty response body.");
                    RecordGenerationFailure("AI response was empty.");
                    return null;
                }

                var capsulePayload = JsonSerializer.Deserialize<CapsulePayload>(rawJson, JsonOptions);
                if (capsulePayload is null)
                {
                    MyBlogTelemetry.RecordAiRequest(
                        provider: "openai",
                        operation: "daily_capsule",
                        model: _model,
                        duration: successfulRequestDuration,
                        success: false,
                        errorType: "invalid_json");
                    _logger.LogWarning("Daily capsule generation returned unparsable JSON content.");
                    RecordGenerationFailure("AI response JSON was invalid.");
                    return null;
                }

                var capsule = CreateValidatedCapsule(ukDate, capsulePayload);
                if (capsule is null)
                {
                    MyBlogTelemetry.RecordAiRequest(
                        provider: "openai",
                        operation: "daily_capsule",
                        model: _model,
                        duration: successfulRequestDuration,
                        success: false,
                        errorType: "validation_failed");
                    _logger.LogWarning("Daily capsule generation returned content that did not pass validation.");
                    RecordGenerationFailure("AI response did not pass validation.");
                    return null;
                }

                var requestId = response.Headers.TryGetValues("x-request-id", out var values)
                    ? values.FirstOrDefault()
                    : null;
                TryExtractTokenUsage(responseContent, out var promptTokens, out var completionTokens);
                var estimatedCostUsd = EstimateTokenCostUsd(_model, promptTokens, completionTokens);
                MyBlogTelemetry.RecordAiRequest(
                    provider: "openai",
                    operation: "daily_capsule",
                    model: _model,
                    duration: successfulRequestDuration,
                    success: true,
                    promptTokens: promptTokens,
                    completionTokens: completionTokens,
                    estimatedCostUsd: estimatedCostUsd);
                activity?.SetTag("ai.request_id", requestId ?? string.Empty);
                activity?.SetTag("ai.prompt_tokens", promptTokens ?? 0);
                activity?.SetTag("ai.completion_tokens", completionTokens ?? 0);
                activity?.SetTag("ai.estimated_cost_usd", estimatedCostUsd ?? 0d);

                _logger.LogInformation(
                    "Daily capsule generated via AI for UK date {UkDate}. OpenAIRequestId={OpenAIRequestId}",
                    ukDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    requestId ?? "n/a");
                RecordGenerationSuccess();

                return capsule;
            }
        }
        catch (Exception ex)
        {
            MyBlogTelemetry.RecordAiRequest(
                provider: "openai",
                operation: "daily_capsule",
                model: _model,
                duration: TimeSpan.Zero,
                success: false,
                errorType: ex.GetType().Name);
            _logger.LogError(ex, "Daily capsule AI generation failed.");
            RecordGenerationFailure(ex.Message);
            return null;
        }
    }

    private static string BuildPrompt(DateOnly ukDate)
    {
        var daySeed = ukDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $$"""
Create exactly one short coding capsule for a software engineering blog.
Date seed (for variety): {{daySeed}}

Rules:
- capsuleType must be one of: Quote, Fact, Insight, Snippet, Tip.
- title max 48 characters.
- body max 170 characters.
- example max 120 characters.
- Keep it practical, high-signal, and professional.
- Use UK English spelling and phrasing (for example, optimise, behaviour, colour).
- No markdown formatting, no emojis, no hashtags.
- Provide one concrete one-line example in plain text.

Return JSON only with this schema:
{"capsuleType":"", "title":"", "body":"", "example":""}
""";
    }

    private static CapsuleCacheEntry? CreateValidatedCapsule(DateOnly ukDate, CapsulePayload payload)
    {
        var capsuleType = NormalizeType(payload.CapsuleType);
        var title = ClampAndNormalize(payload.Title, 48);
        var body = ClampAndNormalize(payload.Body, 170);
        var example = EnsureExample(capsuleType, payload.Example, body);

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(example))
        {
            return null;
        }

        return new CapsuleCacheEntry(ukDate, capsuleType, title, body, example, "ai");
    }

    private CapsuleCacheEntry GetFallbackCapsule(DateOnly ukDate)
    {
        var fallbackIndex = Math.Abs(ukDate.DayNumber) % FallbackCapsules.Length;
        var fallback = FallbackCapsules[fallbackIndex];
        return new CapsuleCacheEntry(
            ukDate,
            fallback.CapsuleType,
            fallback.Title,
            fallback.Body,
            fallback.Example,
            "fallback");
    }

    private bool TryGetCachedCapsule(DateOnly ukDate, out CapsuleCacheEntry capsule)
    {
        if (CacheByDate.TryGetValue(ukDate, out var cached))
        {
            capsule = cached;
            return true;
        }

        capsule = default!;
        return false;
    }

    private async Task<CapsuleCacheEntry?> TryGetPersistedCapsuleAsync(DateOnly ukDate, CancellationToken cancellationToken)
    {
        if (_db is null)
        {
            return null;
        }

        try
        {
            var docRef = _db.Collection(DailyCapsulesCollection).Document(ToDateKey(ukDate));
            var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
            if (!snapshot.Exists)
            {
                return null;
            }

            var doc = snapshot.ConvertTo<FirestoreDailyCapsule>();
            var mapped = FromFirestoreDocument(ukDate, doc);
            if (string.IsNullOrWhiteSpace(mapped.Title) ||
                string.IsNullOrWhiteSpace(mapped.Body) ||
                string.IsNullOrWhiteSpace(mapped.Example))
            {
                _logger.LogWarning(
                    "Persisted daily capsule for date {UkDate} is invalid and will be ignored.",
                    ToDateKey(ukDate));
                return null;
            }

            return mapped;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read persisted daily capsule for {UkDate}.", ToDateKey(ukDate));
            return null;
        }
    }

    private async Task<CapsuleCacheEntry> PersistTodayCapsuleAtomicallyAsync(DateOnly ukDate, CapsuleCacheEntry generatedCapsule)
    {
        if (_db is null)
        {
            return generatedCapsule;
        }

        try
        {
            var docRef = _db.Collection(DailyCapsulesCollection).Document(ToDateKey(ukDate));
            var generatedDoc = ToFirestoreDocument(ukDate, generatedCapsule);

            var persistedDoc = await _db.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (snapshot.Exists)
                {
                    return snapshot.ConvertTo<FirestoreDailyCapsule>();
                }

                transaction.Create(docRef, generatedDoc);
                return generatedDoc;
            });

            return FromFirestoreDocument(ukDate, persistedDoc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to persist daily capsule for {UkDate}. Using generated capsule.", ToDateKey(ukDate));
            RecordPersistFailure(ex.Message);
            return generatedCapsule;
        }
    }

    private static FirestoreDailyCapsule ToFirestoreDocument(DateOnly ukDate, CapsuleCacheEntry capsule)
    {
        return new FirestoreDailyCapsule
        {
            UkDate = ToDateKey(ukDate),
            CapsuleType = capsule.CapsuleType,
            Title = capsule.Title,
            Body = capsule.Body,
            Example = capsule.Example,
            Source = capsule.Source,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static CapsuleCacheEntry FromFirestoreDocument(DateOnly ukDate, FirestoreDailyCapsule doc)
    {
        var capsuleType = NormalizeType(doc.CapsuleType);
        var title = ClampAndNormalize(doc.Title, 48);
        var body = ClampAndNormalize(doc.Body, 170);
        var example = EnsureExample(capsuleType, doc.Example, body);
        var source = ClampAndNormalize(doc.Source, 24);
        if (string.IsNullOrWhiteSpace(source))
        {
            source = "persisted";
        }

        return new CapsuleCacheEntry(ukDate, capsuleType, title, body, example, source);
    }

    private static DailyCodingCapsuleViewModel ToViewModel(CapsuleCacheEntry capsule, DateTimeOffset nextResetUtc)
    {
        return new DailyCodingCapsuleViewModel
        {
            CapsuleType = capsule.CapsuleType,
            Title = capsule.Title,
            Body = capsule.Body,
            Example = capsule.Example,
            Source = capsule.Source,
            NextResetUtcIso = nextResetUtc.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    private static DailyCodingCapsuleViewModel ToViewModelAndRecordServe(CapsuleCacheEntry capsule, DateTimeOffset nextResetUtc)
    {
        RecordServe(capsule);
        return ToViewModel(capsule, nextResetUtc);
    }

    public CapsuleOperationalStatus GetOperationalStatus()
    {
        DateTime? lastGenerationAttemptUtc;
        DateTime? lastGenerationSuccessUtc;
        DateTime? lastGenerationFailureUtc;
        string? lastGenerationFailureReason;
        DateTime? lastPersistFailureUtc;
        string? lastPersistFailureReason;
        string? lastServedUkDate;
        string? lastServedSource;

        lock (OperationalStateLock)
        {
            lastGenerationAttemptUtc = _lastGenerationAttemptUtc;
            lastGenerationSuccessUtc = _lastGenerationSuccessUtc;
            lastGenerationFailureUtc = _lastGenerationFailureUtc;
            lastGenerationFailureReason = _lastGenerationFailureReason;
            lastPersistFailureUtc = _lastPersistFailureUtc;
            lastPersistFailureReason = _lastPersistFailureReason;
            lastServedUkDate = _lastServedUkDate;
            lastServedSource = _lastServedSource;
        }

        var nowUtc = DateTime.UtcNow;
        var generationFailureOutstanding =
            lastGenerationFailureUtc.HasValue &&
            (!lastGenerationSuccessUtc.HasValue || lastGenerationSuccessUtc.Value < lastGenerationFailureUtc.Value) &&
            nowUtc - lastGenerationFailureUtc.Value <= TimeSpan.FromDays(2);

        var persistFailureRecent =
            lastPersistFailureUtc.HasValue &&
            nowUtc - lastPersistFailureUtc.Value <= TimeSpan.FromDays(2);

        var status = "ok";
        if (!_enableAiGeneration)
        {
            status = "disabled";
        }
        else if (generationFailureOutstanding || persistFailureRecent)
        {
            status = "degraded";
        }

        return new CapsuleOperationalStatus
        {
            Status = status,
            AiGenerationEnabled = _enableAiGeneration,
            PersistenceEnabled = _db is not null,
            LastGenerationAttemptUtc = lastGenerationAttemptUtc,
            LastGenerationSuccessUtc = lastGenerationSuccessUtc,
            LastGenerationFailureUtc = lastGenerationFailureUtc,
            LastGenerationFailureReason = lastGenerationFailureReason,
            LastPersistFailureUtc = lastPersistFailureUtc,
            LastPersistFailureReason = lastPersistFailureReason,
            LastServedUkDate = lastServedUkDate,
            LastServedSource = lastServedSource
        };
    }

    private DateTimeOffset GetNextResetUtc(DateOnly ukDate)
    {
        var nextUkMidnight = ukDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextUkMidnight, _ukTimeZone);
        return new DateTimeOffset(nextUtc, TimeSpan.Zero);
    }

    private static string NormalizeType(string? capsuleType)
    {
        var type = ClampAndNormalize(capsuleType, 18);
        return type switch
        {
            "Quote" => "Quote",
            "Fact" => "Fact",
            "Insight" => "Insight",
            "Snippet" => "Snippet",
            "Tip" => "Tip",
            _ => "Insight"
        };
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

    private static string EnsureExample(string capsuleType, string? example, string body)
    {
        var normalized = ClampAndNormalize(example, 120);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        return capsuleType switch
        {
            "Snippet" => "if (!isValid) return;",
            "Tip" => "feat(auth): add token expiry validation to password reset flow",
            "Fact" => "Task.WhenAll(userTask, permissionsTask, flagsTask)",
            _ => ClampAndNormalize(body, 120)
        };
    }

    private static bool GetBool(string? value, bool defaultValue)
    {
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests ||
               statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout ||
               (int)statusCode >= 500;
    }

    private static void TryExtractTokenUsage(
        string responseContent,
        out int? promptTokens,
        out int? completionTokens)
    {
        promptTokens = null;
        completionTokens = null;
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return;
        }

        try
        {
            using var payload = JsonDocument.Parse(responseContent);
            if (!payload.RootElement.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (usage.TryGetProperty("prompt_tokens", out var promptTokenElement) &&
                promptTokenElement.TryGetInt32(out var parsedPromptTokens))
            {
                promptTokens = parsedPromptTokens;
            }

            if (usage.TryGetProperty("completion_tokens", out var completionTokenElement) &&
                completionTokenElement.TryGetInt32(out var parsedCompletionTokens))
            {
                completionTokens = parsedCompletionTokens;
            }
        }
        catch (JsonException)
        {
            // Ignore usage parsing failures and continue without usage metrics.
        }
    }

    private static double? EstimateTokenCostUsd(
        string? model,
        int? promptTokens,
        int? completionTokens)
    {
        if ((!promptTokens.HasValue || promptTokens.Value <= 0) &&
            (!completionTokens.HasValue || completionTokens.Value <= 0))
        {
            return null;
        }

        var normalizedModel = (model ?? string.Empty).Trim().ToLowerInvariant();
        var rates = normalizedModel switch
        {
            "gpt-4.1-mini" => (inputUsdPerMillion: 0.40d, outputUsdPerMillion: 1.60d),
            "gpt-4.1" => (inputUsdPerMillion: 2.00d, outputUsdPerMillion: 8.00d),
            _ => (inputUsdPerMillion: 0.40d, outputUsdPerMillion: 1.60d)
        };

        var inputCost = ((double)(promptTokens ?? 0) / 1_000_000d) * rates.inputUsdPerMillion;
        var outputCost = ((double)(completionTokens ?? 0) / 1_000_000d) * rates.outputUsdPerMillion;
        return Math.Round(inputCost + outputCost, 8, MidpointRounding.AwayFromZero);
    }

    private static bool TryGetRetryAfterDelay(HttpResponseHeaders headers, out TimeSpan retryAfterDelay)
    {
        retryAfterDelay = TimeSpan.Zero;
        var retryAfter = headers.RetryAfter;
        if (retryAfter is null)
        {
            return false;
        }

        if (retryAfter.Delta.HasValue && retryAfter.Delta.Value > TimeSpan.Zero)
        {
            retryAfterDelay = retryAfter.Delta.Value;
            return true;
        }

        if (!retryAfter.Date.HasValue)
        {
            return false;
        }

        var calculatedDelay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
        if (calculatedDelay <= TimeSpan.Zero)
        {
            return false;
        }

        retryAfterDelay = calculatedDelay;
        return true;
    }

    private static TimeSpan ClampRateLimitBackoffDelay(TimeSpan requestedDelay)
    {
        if (requestedDelay <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(30);
        }

        var maxDelay = TimeSpan.FromMinutes(15);
        return requestedDelay > maxDelay ? maxDelay : requestedDelay;
    }

    private static void RecordRateLimitBackoff(TimeSpan backoffDelay)
    {
        lock (OperationalStateLock)
        {
            _aiRateLimitBackoffUntilUtc = DateTimeOffset.UtcNow + backoffDelay;
        }
    }

    private static bool IsRateLimitBackoffActive(out TimeSpan remainingDelay)
    {
        lock (OperationalStateLock)
        {
            if (!_aiRateLimitBackoffUntilUtc.HasValue)
            {
                remainingDelay = TimeSpan.Zero;
                return false;
            }

            var remaining = _aiRateLimitBackoffUntilUtc.Value - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                remainingDelay = remaining;
                return true;
            }

            _aiRateLimitBackoffUntilUtc = null;
            remainingDelay = TimeSpan.Zero;
            return false;
        }
    }

    private static void RecordGenerationAttempt()
    {
        lock (OperationalStateLock)
        {
            _lastGenerationAttemptUtc = DateTime.UtcNow;
        }
    }

    private static void RecordGenerationSuccess()
    {
        lock (OperationalStateLock)
        {
            _lastGenerationSuccessUtc = DateTime.UtcNow;
            _lastGenerationFailureReason = null;
            _aiRateLimitBackoffUntilUtc = null;
        }
    }

    private static void RecordGenerationFailure(string reason)
    {
        lock (OperationalStateLock)
        {
            _lastGenerationFailureUtc = DateTime.UtcNow;
            _lastGenerationFailureReason = ClampAndNormalize(reason, 260);
        }
    }

    private static void RecordPersistFailure(string reason)
    {
        lock (OperationalStateLock)
        {
            _lastPersistFailureUtc = DateTime.UtcNow;
            _lastPersistFailureReason = ClampAndNormalize(reason, 260);
        }
    }

    private static void RecordServe(CapsuleCacheEntry capsule)
    {
        lock (OperationalStateLock)
        {
            _lastServedUkDate = ToDateKey(capsule.UkDate);
            _lastServedSource = capsule.Source;
        }
    }

    private static string ToDateKey(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static TimeZoneInfo ResolveUkTimeZone(ILogger<DailyCodingCapsuleService> logger)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "UK timezone lookup failed. Falling back to UTC.");
                return TimeZoneInfo.Utc;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "UK timezone lookup failed. Falling back to UTC.");
            return TimeZoneInfo.Utc;
        }
    }

    private sealed class ChatCompletionResponse
    {
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        public string? Content { get; set; }
    }

    private sealed class CapsulePayload
    {
        public string? CapsuleType { get; set; }
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? Example { get; set; }
    }

    [FirestoreData]
    private sealed class FirestoreDailyCapsule
    {
        [FirestoreProperty]
        public string UkDate { get; set; } = string.Empty;

        [FirestoreProperty]
        public string CapsuleType { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Title { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Body { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Example { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Source { get; set; } = string.Empty;

        [FirestoreProperty]
        public DateTime CreatedAtUtc { get; set; }
    }

    private sealed record CapsuleCacheEntry(
        DateOnly UkDate,
        string CapsuleType,
        string Title,
        string Body,
        string Example,
        string Source);

    private sealed record CapsuleSeed(
        string CapsuleType,
        string Title,
        string Body,
        string Example);

    public sealed class CapsuleOperationalStatus
    {
        public string Status { get; init; } = "ok";
        public bool AiGenerationEnabled { get; init; }
        public bool PersistenceEnabled { get; init; }
        public DateTime? LastGenerationAttemptUtc { get; init; }
        public DateTime? LastGenerationSuccessUtc { get; init; }
        public DateTime? LastGenerationFailureUtc { get; init; }
        public string? LastGenerationFailureReason { get; init; }
        public DateTime? LastPersistFailureUtc { get; init; }
        public string? LastPersistFailureReason { get; init; }
        public string? LastServedUkDate { get; init; }
        public string? LastServedSource { get; init; }
    }
}
