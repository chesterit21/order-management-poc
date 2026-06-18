using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;

namespace OrderManagement.Application.Abstractions.ActivityLogs;

public interface IActivityLogRepository
{
    Task InsertBatchAsync(
        IReadOnlyCollection<ActivityLogMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists activity log entries with optional filtering and pagination.
    /// Results are ordered by created_at DESC (newest first).
    /// </summary>
    Task<PagedResult<ActivityLogListItemDto>> ListAsync(
        ActivityLogListQueryDto query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists activity log entries (v2) with LIKE filtering on CorrelationId, OrderNumber, and ActivityType.
    /// Results are ordered by created_at DESC (newest first).
    /// </summary>
    Task<PagedResult<ActivityLogListItemDto>> ListV2Async(
        ActivityLogListV2QueryDto query,
        CancellationToken cancellationToken = default);
}