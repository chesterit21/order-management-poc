using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogQueue
{
    bool TryEnqueue(ActivityLogMessage message);

    ValueTask EnqueueAsync(
        ActivityLogMessage message,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<ActivityLogMessage>> ReadBatchAsync(
        int maxBatchSize,
        TimeSpan maxWaitTime,
        CancellationToken cancellationToken = default);
}