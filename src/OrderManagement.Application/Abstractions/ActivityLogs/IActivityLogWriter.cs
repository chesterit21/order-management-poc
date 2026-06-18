using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogWriter
{
    bool TryWrite(ActivityLogMessage message);

    bool TryWrite(
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
        object? metadata = null);
}