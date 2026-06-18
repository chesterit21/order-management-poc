using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.DTOs.Common;

namespace OrderManagement.Application.Services;

/// <summary>
/// Application service for querying activity logs.
/// Currently a thin pass-through to the repository, but provides a place for
/// future business logic such as data masking, access-control filtering, or
/// enrichment with related entity names.
/// </summary>
public sealed class ActivityLogService(
    IActivityLogRepository activityLogRepository,
    ILogger<ActivityLogService> logger) : IActivityLogService
{
    private readonly IActivityLogRepository _activityLogRepository = activityLogRepository;
    private readonly ILogger<ActivityLogService> _logger = logger;

    public async Task<PagedResult<ActivityLogListItemDto>> ListAsync(
        ActivityLogListQueryDto query,
        CancellationToken cancellationToken = default)
    {
        // Normalize the query to safe defaults
        var normalizedQuery = new ActivityLogListQueryDto
        {
            ActivityType = string.IsNullOrWhiteSpace(query.ActivityType) ? null : query.ActivityType.Trim(),
            CorrelationId = string.IsNullOrWhiteSpace(query.CorrelationId) ? null : query.CorrelationId.Trim(),
            ActorUserId = query.ActorUserId,
            OrderId = query.OrderId,
            ProductId = query.ProductId,
            PaymentId = query.PaymentId,
            ErrorCode = string.IsNullOrWhiteSpace(query.ErrorCode) ? null : query.ErrorCode.Trim(),
            MinStatusCode = query.MinStatusCode,
            FromDate = query.FromDate,
            ToDate = query.ToDate,
            Page = query.Page < 1 ? 1 : query.Page,
            PageSize = query.PageSize < 1 ? 50 : Math.Min(query.PageSize, 200)
        };

        _logger.LogDebug(
            "Listing activity logs. ActivityType={ActivityType} CorrelationId={CorrelationId} ErrorCode={ErrorCode} Page={Page} PageSize={PageSize}",
            normalizedQuery.ActivityType,
            normalizedQuery.CorrelationId,
            normalizedQuery.ErrorCode,
            normalizedQuery.Page,
            normalizedQuery.PageSize);

        return await _activityLogRepository.ListAsync(normalizedQuery, cancellationToken);
    }
}
