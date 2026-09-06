using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyBlog.Services;

namespace MyBlog.Tests;

public sealed class AislePilotBackgroundTaskQueueTests
{
    [Fact]
    public void TryEnqueue_RejectsWorkWhenConfiguredCapacityIsFull()
    {
        using var queue = CreateQueue(capacity: 1, concurrency: 1);

        var firstAccepted = queue.TryEnqueue("meal_image_generation", _ => ValueTask.CompletedTask);
        var secondAccepted = queue.TryEnqueue("plan_pool_replenishment", _ => ValueTask.CompletedTask);

        Assert.True(firstAccepted);
        Assert.False(secondAccepted);
        Assert.Equal(1, queue.Depth);
        Assert.Equal(1, queue.Capacity);
        Assert.Equal(1, queue.Concurrency);
        Assert.Equal(2, queue.MaxAttempts);
        var snapshot = queue.GetSnapshot();
        Assert.Equal(1, snapshot.QueuedDepth);
        Assert.Equal(1, snapshot.AcceptedCount);
        Assert.Equal(1, snapshot.RejectedCount);
        Assert.Equal(1, snapshot.QueuedByJob["meal_image_generation"]);
    }

    [Fact]
    public async Task HostedQueue_ExecutesWorkAndCancelsActiveJobDuringShutdown()
    {
        using var queue = CreateQueue(capacity: 2, concurrency: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(queue.TryEnqueue("meal_image_generation", async stoppingToken =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                cancelled.TrySetResult();
                throw;
            }
        }));

        await queue.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queue.StopAsync(CancellationToken.None);

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, queue.Depth);
    }

    [Fact]
    public async Task HostedQueue_RunsOnlyTheConfiguredNumberOfWorkersConcurrently()
    {
        using var queue = CreateQueue(capacity: 3, concurrency: 2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var twoWorkersStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeWorkers = 0;

        var jobNames = new[]
        {
            "meal_image_generation",
            "plan_pool_replenishment",
            "dessert_addon_recovery"
        };
        foreach (var jobName in jobNames)
        {
            Assert.True(queue.TryEnqueue(jobName, async stoppingToken =>
            {
                if (Interlocked.Increment(ref activeWorkers) == 2)
                {
                    twoWorkersStarted.TrySetResult();
                }
                await release.Task.WaitAsync(stoppingToken);
                Interlocked.Decrement(ref activeWorkers);
            }));
        }

        await queue.StartAsync(CancellationToken.None);
        await twoWorkersStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, Volatile.Read(ref activeWorkers));

        release.TrySetResult();
        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HostedQueue_RetriesFailureWithBoundedBackoffAndFinalizesOnce()
    {
        using var queue = CreateQueue(capacity: 1, concurrency: 1);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var finalized = 0;
        Assert.True(queue.TryEnqueue(
            "ai_meal_persistence",
            _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new InvalidOperationException("transient");
                }
                completed.TrySetResult();
                return ValueTask.CompletedTask;
            },
            () => Interlocked.Increment(ref finalized)));

        await queue.StartAsync(CancellationToken.None);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(1, finalized);
    }

    [Fact]
    public void TryEnqueue_RejectsJobWithoutExplicitRestartRecoveryPolicy()
    {
        using var queue = CreateQueue(capacity: 1, concurrency: 1);

        var exception = Assert.Throws<ArgumentException>(() =>
            queue.TryEnqueue("new_unclassified_job", _ => ValueTask.CompletedTask));

        Assert.Equal("jobName", exception.ParamName);
    }

    [Fact]
    public void BackgroundJobCatalog_DocumentsRecoveryForEveryNonDurableJob()
    {
        Assert.NotEmpty(AislePilotBackgroundJobCatalog.All);
        Assert.All(AislePilotBackgroundJobCatalog.All, policy =>
        {
            Assert.False(policy.RequiresDurableQueue);
            Assert.False(string.IsNullOrWhiteSpace(policy.RecoveryTrigger));
        });
    }

    private static AislePilotBackgroundTaskQueue CreateQueue(int capacity, int concurrency)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AislePilot:BackgroundQueueCapacity"] = capacity.ToString(),
                ["AislePilot:BackgroundQueueConcurrency"] = concurrency.ToString(),
                ["AislePilot:BackgroundQueueMaxAttempts"] = "2",
                ["AislePilot:BackgroundQueueRetryBaseDelayMs"] = "10"
            })
            .Build();
        return new AislePilotBackgroundTaskQueue(
            configuration,
            NullLogger<AislePilotBackgroundTaskQueue>.Instance);
    }
}
