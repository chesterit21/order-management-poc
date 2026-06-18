using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Contracts.Dashboard;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Dashboard;
using OrderManagement.Application.DTOs.Dashboard;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StoreBackofficeUser)]
[Route("api/v1/backoffice/dashboard")]
public sealed class BackofficeDashboardController : ControllerBase
{
    private readonly IBackofficeDashboardService _dashboardService;

    public BackofficeDashboardController(IBackofficeDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(BackofficeDashboardSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackofficeDashboardSummaryResponse>> GetSummary(
        [FromQuery] BackofficeDashboardSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetSummaryAsync(
            new BackofficeDashboardSummaryQueryDto
            {
                StoreId = query.StoreId,
                LowStockThreshold = query.LowStockThreshold
            },
            cancellationToken);

        return Ok(new BackofficeDashboardSummaryResponse
        {
            StoreId = result.StoreId,
            StoreName = result.StoreName,
            TotalProducts = result.TotalProducts,
            ActiveProducts = result.ActiveProducts,
            InactiveProducts = result.InactiveProducts,
            LowStockProducts = result.LowStockProducts,
            PendingOrders = result.PendingOrders,
            ConfirmedOrders = result.ConfirmedOrders,
            ShippedOrders = result.ShippedOrders,
            CancelledOrders = result.CancelledOrders,
            TodayOrders = result.TodayOrders,
            TodayRevenue = result.TodayRevenue,
            GeneratedAt = result.GeneratedAt
        });
    }
}