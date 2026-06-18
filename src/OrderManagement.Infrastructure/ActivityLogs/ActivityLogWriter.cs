using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Infrastructure.ActivityLogs;

public sealed class ActivityLogWriter(
    IActivityLogQueue queue,
    IActivityLogContextAccessor contextAccessor,
    IClock clock,
    ILogger<ActivityLogWriter> logger) : IActivityLogWriter
{
    private readonly IActivityLogQueue _queue = queue;
    private readonly IActivityLogContextAccessor _contextAccessor = contextAccessor;
    private readonly IClock _clock = clock;
    private readonly ILogger<ActivityLogWriter> _logger = logger;

    public bool TryWrite(ActivityLogMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var written = _queue.TryEnqueue(message);

        if (!written)
        {
            _logger.LogWarning(
                "Failed to enqueue activity log. ActivityType={ActivityType} CorrelationId={CorrelationId}",
                message.ActivityType,
                message.CorrelationId);
        }

        return written;
    }

    public bool TryWrite(
        string activityType,
        Guid? orderId = null,
        string? orderNumber = null,
        Guid? productId = null,
        Guid? paymentId = null,
        int? statusCode = null,
        long? elapsedMs = null,
        string? errorCode = null,
        object? beforeState = null,
        object? afterState = null,
        object? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(activityType))
        {
            throw new ArgumentException("Activity type is required.", nameof(activityType));
        }

        var message = new ActivityLogMessage
        {
            CorrelationId = _contextAccessor.CorrelationId,
            ActivityType = activityType.Trim(),
            ActorUserId = _contextAccessor.UserId,
            ActorUsername = _contextAccessor.Username,
            ActorRole = _contextAccessor.Role,
            OrderId = orderId,
            OrderNumber = orderNumber,
            ProductId = productId,
            PaymentId = paymentId,
            RequestPath = _contextAccessor.RequestPath,
            HttpMethod = _contextAccessor.HttpMethod,
            StatusCode = statusCode,
            ElapsedMs = elapsedMs,
            ErrorCode = errorCode,
            BeforeStateJson = ActivityLogJson.SerializeOrNull(beforeState),
            AfterStateJson = ActivityLogJson.SerializeOrNull(afterState),
            MetadataJson = ActivityLogJson.SerializeOrNull(metadata),
            CreatedAt = _clock.UtcNow
        };

        return TryWrite(message);
    }
}