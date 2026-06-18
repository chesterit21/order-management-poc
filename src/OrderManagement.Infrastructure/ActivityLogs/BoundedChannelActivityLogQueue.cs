using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Infrastructure.Options;

namespace OrderManagement.Infrastructure.ActivityLogs;

/// <summary>
/// Bounded channel-based activity log queue with priority routing.
///
/// Uses TWO bounded channels to ensure ops-critical logs survive traffic spikes:
///   - High-priority channel: RequestFailed, entries with errorCode, entries with statusCode >= 400.
///     These get their own reserved capacity so they are NOT dropped when the normal channel is full.
///   - Normal-priority channel: everything else (success/info logs).
///     Subject to best-effort drop when full.
///
/// The background worker's ReadBatchAsync drains the high-priority channel FIRST,
/// then fills remaining batch slots from the normal channel. This guarantees that
/// error/failure logs are persisted ahead of success logs during backlog.
/// </summary>
public sealed class BoundedChannelActivityLogQueue(
    IOptions<ActivityLogOptions> options,
    ILogger<BoundedChannelActivityLogQueue> logger) : IActivityLogQueue
{
    private readonly ActivityLogOptions _options = options.Value;
    private readonly ILogger<BoundedChannelActivityLogQueue> _logger = logger;

    private readonly Channel<ActivityLogMessage> _normalChannel =
        Channel.CreateBounded<ActivityLogMessage>(options.Value.QueueCapacity);

    private readonly Channel<ActivityLogMessage> _highPriorityChannel =
        Channel.CreateBounded<ActivityLogMessage>(options.Value.HighPriorityQueueCapacity);

    private long _droppedNormalMessages;
    private long _droppedHighPriorityMessages;

    /// <summary>
    /// Determines whether a message should be routed to the high-priority channel.
    /// High-priority = anything that ops needs for error tracing:
    ///   - ActivityType == "RequestFailed"
    ///   - ErrorCode is non-null (business rule violations, validation errors, etc.)
    ///   - StatusCode >= 400 (client/server errors)
    /// </summary>
    internal static bool IsHighPriority(ActivityLogMessage message)
    {
        return message.ActivityType == ActivityLogTypes.RequestFailed
               || !string.IsNullOrEmpty(message.ErrorCode)
               || (message.StatusCode.HasValue && message.StatusCode.Value >= 400);
    }

    public bool TryEnqueue(ActivityLogMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.Enabled)
        {
            return false;
        }

        var channel = IsHighPriority(message) ? _highPriorityChannel : _normalChannel;
        var written = channel.Writer.TryWrite(message);

        if (!written)
        {
            var isHighPriority = channel == _highPriorityChannel;
            var dropped = Interlocked.Increment(
                ref (isHighPriority ? ref _droppedHighPriorityMessages : ref _droppedNormalMessages));

            // Log every 100th drop to avoid log flooding. High-priority drops are logged
            // at Error level because they indicate the error queue itself is overflowing —
            // a severe condition that ops should investigate.
            if (dropped % 100 == 1)
            {
                if (isHighPriority)
                {
                    _logger.LogError(
                        "High-priority activity log queue is full. Ops-critical logs are being dropped. " +
                        "DroppedHighPriority={DroppedHighPriority}",
                        dropped);
                }
                else
                {
                    _logger.LogWarning(
                        "Normal activity log queue is full. Normal-priority logs are being dropped. " +
                        "DroppedNormal={DroppedNormal}",
                        dropped);
                }
            }
        }

        return written;
    }

    public async ValueTask EnqueueAsync(
        ActivityLogMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.Enabled)
        {
            return;
        }

        var channel = IsHighPriority(message) ? _highPriorityChannel : _normalChannel;
        await channel.Writer.WriteAsync(message, cancellationToken);
    }

    public async ValueTask<IReadOnlyCollection<ActivityLogMessage>> ReadBatchAsync(
        int maxBatchSize,
        TimeSpan maxWaitTime,
        CancellationToken cancellationToken = default)
    {
        if (maxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchSize), "Max batch size must be greater than zero.");
        }

        var batch = new List<ActivityLogMessage>(maxBatchSize);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(maxWaitTime);

        try
        {
            // Phase 1: Non-blocking drain of both channels.
            // High-priority is drained first so error/failure logs are batched ahead.
            DrainNonBlocking(batch, maxBatchSize);

            // Phase 2: If we already have items, return immediately.
            // The background worker will loop and call ReadBatchAsync again for the next batch.
            if (batch.Count > 0)
            {
                return batch;
            }

            // Phase 3: Both channels were empty — wait for data on EITHER channel.
            // Use Task.WhenAny to wake up as soon as either channel has data.
            var highPriorityWait = _highPriorityChannel.Reader.WaitToReadAsync(timeoutCts.Token).AsTask();
            var normalWait = _normalChannel.Reader.WaitToReadAsync(timeoutCts.Token).AsTask();

            await Task.WhenAny(highPriorityWait, normalWait);

            // At least one channel now has data (or timeout fired).
            // Drain both non-blocking again — whichever has data will be read.
            // High-priority is still drained first.
            DrainNonBlocking(batch, maxBatchSize);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout reached. Return whatever has been collected (may be empty).
        }

        return batch;
    }

    /// <summary>
    /// Non-blocking drain: reads all immediately-available items from both channels
    /// into the batch, up to maxBatchSize. High-priority is drained first.
    /// </summary>
    private void DrainNonBlocking(List<ActivityLogMessage> batch, int maxBatchSize)
    {
        // High-priority first
        while (batch.Count < maxBatchSize &&
               _highPriorityChannel.Reader.TryRead(out var message))
        {
            batch.Add(message);
        }

        // Then normal
        while (batch.Count < maxBatchSize &&
               _normalChannel.Reader.TryRead(out var message))
        {
            batch.Add(message);
        }
    }
}