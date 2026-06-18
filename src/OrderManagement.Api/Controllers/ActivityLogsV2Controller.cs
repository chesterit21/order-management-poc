using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.ActivityLogs;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Api.Controllers;

/// <summary>
/// Read-only activity log browsing endpoint (v2) for Application Admin and DevOps.
/// Adds LIKE filtering on CorrelationId, OrderNumber, and ActivityType.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ApplicationAdminOrDevOps)]
[Route("api/v2/internal/activity-logs")]
public sealed class ActivityLogsV2Controller : ControllerBase
{
    private readonly IActivityLogService _activityLogService;

    public ActivityLogsV2Controller(IActivityLogService activityLogService)
    {
        _activityLogService = activityLogService;
    }

    /// <summary>
    /// List activity log entries with LIKE filtering and pagination.
    /// Results are ordered by created_at DESC (newest first).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ActivityLogResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ActivityLogResponse>>> List(
        [FromQuery] ActivityLogV2Query query,
        CancellationToken cancellationToken)
    {
        var result = await _activityLogService.ListV2Async(
            new ActivityLogListV2QueryDto
            {
                CorrelationId = query.CorrelationId,
                OrderNumber = query.OrderNumber,
                ActivityType = query.ActivityType,
                Page = query.Page,
                PageSize = query.PageSize
            },
            cancellationToken);

        return Ok(new PagedResponse<ActivityLogResponse>
        {
            Items = result.Items
                .Select(MapResponse)
                .ToArray(),
            Pagination = new PaginationResponse
            {
                Page = result.Page,
                PageSize = result.PageSize,
                TotalItems = result.TotalItems,
                TotalPages = result.TotalPages
            }
        });
    }

    private static ActivityLogResponse MapResponse(ActivityLogListItemDto item)
    {
        return new ActivityLogResponse
        {
            Id = item.Id,
            CorrelationId = item.CorrelationId,
            ActivityType = item.ActivityType,
            ActorUserId = item.ActorUserId,
            ActorUsername = item.ActorUsername,
            ActorRole = item.ActorRole,
            OrderId = item.OrderId,
            OrderNumber = item.OrderNumber,
            ProductId = item.ProductId,
            PaymentId = item.PaymentId,
            RequestPath = item.RequestPath,
            HttpMethod = item.HttpMethod,
            StatusCode = item.StatusCode,
            ElapsedMs = item.ElapsedMs,
            ErrorCode = item.ErrorCode,
            BeforeStateJson = item.BeforeStateJson,
            AfterStateJson = item.AfterStateJson,
            MetadataJson = item.MetadataJson,
            CreatedAt = item.CreatedAt
        };
    }
}
