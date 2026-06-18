using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;

namespace OrderManagement.Application.Abstractions.ActivityLogs;

/// <summary>
/// Application service for querying activity logs.
/// Intended for admin/DevOps observability use cases.
/// </summary>
public interface IActivityLogService
{
    /// <summary>
    /// Lists activity log entries with optional filtering and pagination.
    /// </summary>
    Task<PagedResult<ActivityLogListItemDto>> ListAsync(
        ActivityLogListQueryDto query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists activity log entries (v2) with LIKE filtering on CorrelationId, OrderNumber, and ActivityType.
    /// </summary>
    Task<PagedResult<ActivityLogListItemDto>> ListV2Async(
        ActivityLogListV2QueryDto query,
        CancellationToken cancellationToken = default);
}
