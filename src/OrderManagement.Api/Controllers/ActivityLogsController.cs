using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.ActivityLogs;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.ActivityLogs;

namespace OrderManagement.Api.Controllers;

/// <summary>
/// Read-only activity log browsing endpoint for Application Admin and DevOps.
///
/// This fulfills the observability requirement: ops teams can query the
/// fire-and-forget activity log entries (order created, payment processed,
/// request failed, login failed, etc.) to trace errors and audit user actions.
///
/// Write path remains internal (IActivityLogWriter → bounded channel →
/// background worker → batch insert) and is not exposed via this controller.
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ApplicationAdminOrDevOps)]
[Route("api/v1/internal/activity-logs")]
public sealed class ActivityLogsController : ControllerBase
{
    private readonly IActivityLogService _activityLogService;

    public ActivityLogsController(IActivityLogService activityLogService)
    {
        _activityLogService = activityLogService;
    }

    /// <summary>
    /// List activity log entries with optional filtering and pagination.
    /// Results are ordered by created_at DESC (newest first).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ActivityLogResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ActivityLogResponse>>> List(
        [FromQuery] ActivityLogQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _activityLogService.ListAsync(
            new ActivityLogListQueryDto
            {
                ActivityType = query.ActivityType,
                CorrelationId = query.CorrelationId,
                ActorUserId = query.ActorUserId,
                OrderId = query.OrderId,
                ProductId = query.ProductId,
                PaymentId = query.PaymentId,
                ErrorCode = query.ErrorCode,
                MinStatusCode = query.MinStatusCode,
                FromDate = query.FromDate,
                ToDate = query.ToDate,
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
