using MyBlog.Services;

namespace MyBlog.Services;

internal sealed class AislePilotCacheWarmupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AislePilotCacheWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refreshInterval = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue("AislePilot:CacheWarmupIntervalMinutes", 10),
            1,
            1440));
        var maximumJitter = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("AislePilot:CacheWarmupJitterSeconds", 60),
            0,
            600));
        var initialDelay = AislePilotCacheWarmupSchedule.CalculateInitialDelay(
            maximumJitter,
            Random.Shared.NextDouble());
        if (initialDelay > TimeSpan.Zero)
        {
            await Task.Delay(initialDelay, stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IAislePilotService>();
                await service.WarmRuntimeCachesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AislePilot runtime cache warmup failed.");
            }

            var nextDelay = AislePilotCacheWarmupSchedule.CalculateRecurringDelay(
                refreshInterval,
                maximumJitter,
                Random.Shared.NextDouble());
            await Task.Delay(nextDelay, stoppingToken);
        }
    }
}
