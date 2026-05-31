using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace MyBlog.Startup;

internal enum LocalRunLogMode
{
    OverwriteSingleFile,
    PerRunFiles
}

internal sealed record LocalRunLogOptions(
    bool Enabled,
    string DirectoryPath,
    LocalRunLogMode Mode,
    string OverwriteFileName,
    int RetainedRunFiles,
    LogLevel MinimumLevel)
{
    public static LocalRunLogOptions From(IConfiguration configuration, string environmentName)
    {
        var defaultEnabled = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
        var enabled = GetBool(
            Environment.GetEnvironmentVariable("OBSERVABILITY__LOCAL_RUN_LOGS__ENABLED")
            ?? configuration["Observability:LocalRunLogs:Enabled"],
            defaultEnabled);
        var directoryPath =
            Environment.GetEnvironmentVariable("OBSERVABILITY__LOCAL_RUN_LOGS__DIRECTORY")
            ?? configuration["Observability:LocalRunLogs:Directory"]
            ?? "artifacts/runtime-logs";
        var modeText =
            Environment.GetEnvironmentVariable("OBSERVABILITY__LOCAL_RUN_LOGS__MODE")
            ?? configuration["Observability:LocalRunLogs:Mode"]
            ?? "per-run";
        var overwriteFileName =
            Environment.GetEnvironmentVariable("OBSERVABILITY__LOCAL_RUN_LOGS__OVERWRITE_FILE_NAME")
            ?? configuration["Observability:LocalRunLogs:OverwriteFileName"]
            ?? "latest-run.log";
        var retainedRunFiles = GetInt(
            Environment.GetEnvironmentVariable("OBSERVABILITY__LOCAL_RUN_LOGS__RETAINED_RUN_FILES")
            ?? configuration["Observability:LocalRunLogs:RetainedRunFiles"],
            defaultValue: 25,
            minimum: 1,
            maximum: 400);
        var minimumLevelText =
            Environment.GetEnvironmentVariable("OBSERVABILITY__LOCAL_RUN_LOGS__MIN_LEVEL")
            ?? configuration["Observability:LocalRunLogs:MinLevel"]
            ?? "Information";
        var minimumLevel = ResolveLogLevel(minimumLevelText);
        var mode = ResolveMode(modeText);

        return new LocalRunLogOptions(
            Enabled: enabled,
            DirectoryPath: directoryPath,
            Mode: mode,
            OverwriteFileName: overwriteFileName,
            RetainedRunFiles: retainedRunFiles,
            MinimumLevel: minimumLevel);
    }

    private static LocalRunLogMode ResolveMode(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "overwrite" => LocalRunLogMode.OverwriteSingleFile,
            "single" => LocalRunLogMode.OverwriteSingleFile,
            "latest" => LocalRunLogMode.OverwriteSingleFile,
            _ => LocalRunLogMode.PerRunFiles
        };
    }

    private static LogLevel ResolveLogLevel(string? value)
    {
        return Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;
    }

    private static bool GetBool(string? value, bool defaultValue)
    {
        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static int GetInt(string? value, int defaultValue, int minimum, int maximum)
    {
        if (!int.TryParse(value, out var parsed))
        {
            return defaultValue;
        }

        return Math.Clamp(parsed, minimum, maximum);
    }
}

internal sealed class LocalRunFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private LocalRunLogFileSession? _session;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    private LocalRunFileLoggerProvider(LocalRunLogFileSession session)
    {
        _session = session;
    }

    internal string LogFilePath => _session?.LogFilePath ?? string.Empty;

    public static LocalRunFileLoggerProvider? TryCreate(LocalRunLogOptions options, string contentRootPath)
    {
        var session = LocalRunLogFileSession.TryCreate(options, contentRootPath);
        return session is null ? null : new LocalRunFileLoggerProvider(session);
    }

    public ILogger CreateLogger(string categoryName)
    {
        var session = _session;
        return session is null
            ? LocalRunNoOpLogger.Instance
            : new LocalRunFileLogger(categoryName, session, () => _scopeProvider);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }

    private sealed class LocalRunFileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly LocalRunLogFileSession _session;
        private readonly Func<IExternalScopeProvider> _scopeProviderAccessor;

        public LocalRunFileLogger(
            string categoryName,
            LocalRunLogFileSession session,
            Func<IExternalScopeProvider> scopeProviderAccessor)
        {
            _categoryName = categoryName;
            _session = session;
            _scopeProviderAccessor = scopeProviderAccessor;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return _scopeProviderAccessor().Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _session.IsEnabled(logLevel);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(formatter);
            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null)
            {
                return;
            }

            var scopes = CaptureScopes(_scopeProviderAccessor());
            var stateValues = CaptureStructuredState(state);
            var activity = Activity.Current;
            var entry = new LocalRunLogEntry(
                TimestampUtc: DateTime.UtcNow,
                Level: logLevel.ToString(),
                Category: _categoryName,
                EventId: eventId.Id,
                EventName: eventId.Name,
                Message: message,
                Exception: exception?.ToString(),
                TraceId: activity?.TraceId.ToString(),
                SpanId: activity?.SpanId.ToString(),
                State: stateValues,
                Scopes: scopes);
            _session.Write(entry);
        }

        private static IReadOnlyDictionary<string, string?>? CaptureStructuredState<TState>(TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> structuredState)
            {
                return null;
            }

            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var item in structuredState)
            {
                if (string.Equals(item.Key, "{OriginalFormat}", StringComparison.Ordinal))
                {
                    continue;
                }

                values[item.Key] = ConvertStateValue(item.Value);
            }

            return values.Count == 0 ? null : values;
        }

        private static string? ConvertStateValue(object? value)
        {
            if (value is null)
            {
                return null;
            }

            return value switch
            {
                string text => text,
                DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
        }

        private static IReadOnlyList<string>? CaptureScopes(IExternalScopeProvider scopeProvider)
        {
            var scopes = new List<string>();
            scopeProvider.ForEachScope((scope, capturedScopes) =>
            {
                capturedScopes.Add(scope?.ToString() ?? string.Empty);
            }, scopes);

            return scopes.Count == 0 ? null : scopes;
        }
    }

    private sealed class LocalRunNoOpLogger : ILogger
    {
        public static readonly LocalRunNoOpLogger Instance = new();
        private LocalRunNoOpLogger()
        {
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}

internal sealed class LocalRunLogFileSession : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static int _runFileSequence;
    private readonly StreamWriter _writer;
    private readonly object _lock = new();
    private readonly LogLevel _minimumLevel;
    private bool _disposed;

    private LocalRunLogFileSession(
        StreamWriter writer,
        string logFilePath,
        LogLevel minimumLevel)
    {
        _writer = writer;
        _minimumLevel = minimumLevel;
        LogFilePath = logFilePath;
    }

    internal string LogFilePath { get; }

    public static LocalRunLogFileSession? TryCreate(LocalRunLogOptions options, string contentRootPath)
    {
        if (!options.Enabled)
        {
            return null;
        }

        try
        {
            var absoluteDirectoryPath = ResolveDirectoryPath(options.DirectoryPath, contentRootPath);
            Directory.CreateDirectory(absoluteDirectoryPath);
            var logFilePath = ResolveLogFilePath(options, absoluteDirectoryPath);
            var fileMode = options.Mode == LocalRunLogMode.OverwriteSingleFile
                ? FileMode.Create
                : FileMode.CreateNew;
            var fileStream = new FileStream(logFilePath, fileMode, FileAccess.Write, FileShare.ReadWrite);
            var writer = new StreamWriter(fileStream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            var session = new LocalRunLogFileSession(writer, logFilePath, options.MinimumLevel);
            session.Write(new LocalRunLogEntry(
                TimestampUtc: DateTime.UtcNow,
                Level: "Information",
                Category: "MyBlog.LocalRunLogs",
                EventId: 0,
                EventName: "run_started",
                Message: $"Run log started. Mode={options.Mode} Path={logFilePath}",
                Exception: null,
                TraceId: null,
                SpanId: null,
                State: null,
                Scopes: null));

            if (options.Mode == LocalRunLogMode.PerRunFiles)
            {
                DeleteExpiredRunFiles(absoluteDirectoryPath, options.RetainedRunFiles, logFilePath);
            }

            return session;
        }
        catch
        {
            return null;
        }
    }

    internal bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None && logLevel >= _minimumLevel;
    }

    internal void Write(LocalRunLogEntry entry)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _writer.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
            }
            catch
            {
                // Log write failures should never crash request handling.
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    private static string ResolveDirectoryPath(string configuredDirectoryPath, string contentRootPath)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredDirectoryPath)
            ? "artifacts/runtime-logs"
            : configuredDirectoryPath.Trim();
        return Path.IsPathRooted(candidate)
            ? candidate
            : Path.GetFullPath(Path.Combine(contentRootPath, candidate));
    }

    private static string ResolveLogFilePath(LocalRunLogOptions options, string absoluteDirectoryPath)
    {
        if (options.Mode == LocalRunLogMode.OverwriteSingleFile)
        {
            var fileName = string.IsNullOrWhiteSpace(options.OverwriteFileName)
                ? "latest-run.log"
                : options.OverwriteFileName.Trim();
            return Path.Combine(absoluteDirectoryPath, fileName);
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var sequence = Interlocked.Increment(ref _runFileSequence);
        var fileNameWithRunId = $"run-{timestamp}-pid{Environment.ProcessId}-n{sequence}.log";
        return Path.Combine(absoluteDirectoryPath, fileNameWithRunId);
    }

    private static void DeleteExpiredRunFiles(
        string directoryPath,
        int retainedRunFiles,
        string currentLogFilePath)
    {
        var keepCount = Math.Max(1, retainedRunFiles);
        try
        {
            var files = Directory.GetFiles(directoryPath, "run-*.log", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(path, currentLogFilePath, StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count < keepCount)
            {
                return;
            }

            foreach (var expiredFile in files.Skip(keepCount - 1))
            {
                try
                {
                    expiredFile.Delete();
                }
                catch
                {
                    // Ignore cleanup failures so logging can continue.
                }
            }
        }
        catch
        {
            // Ignore cleanup failures so logging can continue.
        }
    }
}

internal sealed record LocalRunLogEntry(
    DateTime TimestampUtc,
    string Level,
    string Category,
    int EventId,
    string? EventName,
    string Message,
    string? Exception,
    string? TraceId,
    string? SpanId,
    IReadOnlyDictionary<string, string?>? State,
    IReadOnlyList<string>? Scopes);
