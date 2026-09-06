using System.Threading.Channels;
using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MyBlog.Services;

public interface IAislePilotBackgroundTaskQueue
{
    int Capacity { get; }
    int Depth { get; }
    AislePilotBackgroundQueueSnapshot GetSnapshot();
    bool TryEnqueue(
        string jobName,
        Func<CancellationToken, ValueTask> workItem,
        Action? onFinalized = null);
}

public sealed record AislePilotBackgroundQueueSnapshot(
    int Capacity,
    int Concurrency,
    int MaxAttempts,
    int QueuedDepth,
    int ActiveCount,
    long AcceptedCount,
    long RejectedCount,
    long CompletedCount,
    long CancelledCount,
    long FaultedCount,
    IReadOnlyDictionary<string, int> QueuedByJob);

public sealed class AislePilotBackgroundTaskQueue : BackgroundService, IAislePilotBackgroundTaskQueue
{
    private const int DefaultCapacity = 32;
    private readonly Channel<BackgroundWorkItem> _channel;
    private readonly ILogger<AislePilotBackgroundTaskQueue> _logger;
    private readonly ConcurrentDictionary<string, int> _queuedByJob = new(StringComparer.Ordinal);
    private int _depth;
    private int _activeCount;
    private long _acceptedCount;
    private long _rejectedCount;
    private long _completedCount;
    private long _cancelledCount;
    private long _faultedCount;

    public AislePilotBackgroundTaskQueue(
        IConfiguration configuration,
        ILogger<AislePilotBackgroundTaskQueue> logger)
    {
        _logger = logger;
        Capacity = Math.Clamp(
            configuration.GetValue("AislePilot:BackgroundQueueCapacity", DefaultCapacity),
            1,
            256);
        Concurrency = Math.Clamp(
            configuration.GetValue("AislePilot:BackgroundQueueConcurrency", 3),
            1,
            8);
        MaxAttempts = Math.Clamp(
            configuration.GetValue("AislePilot:BackgroundQueueMaxAttempts", 2),
            1,
            4);
        RetryBaseDelay = TimeSpan.FromMilliseconds(Math.Clamp(
            configuration.GetValue("AislePilot:BackgroundQueueRetryBaseDelayMs", 250),
            10,
            5000));
        _channel = Channel.CreateBounded<BackgroundWorkItem>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = Concurrency == 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        AislePilotTelemetry.ConfigureManagedQueueDepthObserver(() => Depth);
    }

    public int Capacity { get; }
    public int Concurrency { get; }
    public int MaxAttempts { get; }
    public TimeSpan RetryBaseDelay { get; }
    public int Depth => Math.Max(0, Volatile.Read(ref _depth));

    public bool TryEnqueue(
        string jobName,
        Func<CancellationToken, ValueTask> workItem,
        Action? onFinalized = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentNullException.ThrowIfNull(workItem);
        if (!AislePilotBackgroundJobCatalog.TryGet(jobName, out _))
        {
            throw new ArgumentException(
                "Background job must have an explicit restart-recovery policy.",
                nameof(jobName));
        }

        if (!_channel.Writer.TryWrite(new BackgroundWorkItem(
                jobName,
                Stopwatch.GetTimestamp(),
                workItem,
                onFinalized)))
        {
            Interlocked.Increment(ref _rejectedCount);
            AislePilotTelemetry.RecordBackgroundQueueEvent(jobName, "rejected");
            return false;
        }

        Interlocked.Increment(ref _depth);
        Interlocked.Increment(ref _acceptedCount);
        _queuedByJob.AddOrUpdate(jobName, 1, (_, count) => count + 1);
        AislePilotTelemetry.RecordBackgroundQueueEvent(jobName, "accepted");
        return true;
    }

    public AislePilotBackgroundQueueSnapshot GetSnapshot()
    {
        return new AislePilotBackgroundQueueSnapshot(
            Capacity,
            Concurrency,
            MaxAttempts,
            Depth,
            Math.Max(0, Volatile.Read(ref _activeCount)),
            Interlocked.Read(ref _acceptedCount),
            Interlocked.Read(ref _rejectedCount),
            Interlocked.Read(ref _completedCount),
            Interlocked.Read(ref _cancelledCount),
            Interlocked.Read(ref _faultedCount),
            _queuedByJob
                .Where(item => item.Value > 0)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, Concurrency)
            .Select(_ => ConsumeAsync(stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            Interlocked.Decrement(ref _depth);
            _queuedByJob.AddOrUpdate(item.JobName, 0, (_, count) => Math.Max(0, count - 1));
            Interlocked.Increment(ref _activeCount);
            AislePilotTelemetry.RecordBackgroundQueueWait(
                item.JobName,
                Stopwatch.GetElapsedTime(item.EnqueuedTimestamp));
            AislePilotTelemetry.RecordBackgroundQueueEvent(item.JobName, "dequeued");
            try
            {
                await ExecuteWithRetryAsync(item, stoppingToken);
                Interlocked.Increment(ref _completedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                AislePilotTelemetry.RecordBackgroundQueueEvent(item.JobName, "cancelled");
                Interlocked.Increment(ref _cancelledCount);
                return;
            }
            catch (Exception ex)
            {
                AislePilotTelemetry.RecordBackgroundQueueEvent(item.JobName, "faulted");
                Interlocked.Increment(ref _faultedCount);
                _logger.LogError(ex, "AislePilot queued background job failed. Job={Job}", item.JobName);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
                item.OnFinalized?.Invoke();
            }
        }
    }

    private async Task ExecuteWithRetryAsync(BackgroundWorkItem item, CancellationToken stoppingToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await item.WorkItem(stoppingToken);
                AislePilotTelemetry.RecordBackgroundQueueEvent(item.JobName, "completed");
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                AislePilotTelemetry.RecordBackgroundQueueEvent(item.JobName, "retry_scheduled");
                var delayMultiplier = 1 << (attempt - 1);
                var delay = TimeSpan.FromMilliseconds(Math.Min(
                    RetryBaseDelay.TotalMilliseconds * delayMultiplier,
                    5000));
                _logger.LogWarning(
                    ex,
                    "AislePilot queued background job will be retried. Job={Job} Attempt={Attempt} MaxAttempts={MaxAttempts} DelayMs={DelayMs}",
                    item.JobName,
                    attempt,
                    MaxAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        return base.StopAsync(cancellationToken);
    }

    private sealed record BackgroundWorkItem(
        string JobName,
        long EnqueuedTimestamp,
        Func<CancellationToken, ValueTask> WorkItem,
        Action? OnFinalized);
}
