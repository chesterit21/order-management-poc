using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Infrastructure.Options;
using System.Threading.Tasks;

namespace OrderManagement.Infrastructure.ActivityLogs;

public sealed class ActivityLogBackgroundWorker : BackgroundService
{
    private readonly IActivityLogQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActivityLogOptions _options;
    private readonly ILogger<ActivityLogBackgroundWorker> _logger;

    public ActivityLogBackgroundWorker(
        IActivityLogQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<ActivityLogOptions> options,
        ILogger<ActivityLogBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Activity log background worker is disabled.");
            return;
        }

        var maxBatchSize = _options.MaxBatchSize <= 0
            ? 100
            : _options.MaxBatchSize;

        var flushInterval = TimeSpan.FromMilliseconds(
            _options.FlushIntervalMilliseconds <= 0
                ? 1_000
                : _options.FlushIntervalMilliseconds);

        _logger.LogInformation(
            "Activity log background worker started. MaxBatchSize={MaxBatchSize} FlushIntervalMs={FlushIntervalMs}",
            maxBatchSize,
            flushInterval.TotalMilliseconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _queue.ReadBatchAsync(
                    maxBatchSize,
                    flushInterval,
                    stoppingToken);

                if (batch.Count == 0)
                {
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();

                var repository = scope.ServiceProvider.GetRequiredService<IActivityLogRepository>();

                await repository.InsertBatchAsync(batch, stoppingToken);

                _logger.LogDebug(
                    "Activity log batch inserted. Count={Count}",
                    batch.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown.
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to process activity log batch.");

                await DelayAfterFailureAsync(stoppingToken);
            }
        }

        _logger.LogInformation("Activity log background worker stopped.");
    }

    private static async Task DelayAfterFailureAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ignore.
        }
    }
}