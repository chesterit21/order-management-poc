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
}