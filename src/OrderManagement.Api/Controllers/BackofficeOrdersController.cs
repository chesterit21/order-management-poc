using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Contracts.Orders;
using OrderManagement.Api.Contracts.Orders.Backoffice;
using OrderManagement.Api.Extensions;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.DTOs.Orders.Backoffice;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.StoreBackofficeUser)]
[Route("api/v1/backoffice/orders")]
public sealed class BackofficeOrdersController : ControllerBase
{
    private readonly IBackofficeOrderService _backofficeOrderService;

    public BackofficeOrdersController(IBackofficeOrderService backofficeOrderService)
    {
        _backofficeOrderService = backofficeOrderService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<BackofficeOrderListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<BackofficeOrderListItemResponse>>> List(
        [FromQuery] BackofficeOrderQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _backofficeOrderService.ListAsync(
            new BackofficeOrderListQueryDto
            {
                StoreId = query.StoreId,
                Status = query.Status,
                CustomerId = query.CustomerId,
                FromDate = query.FromDate,
                ToDate = query.ToDate,
                Page = query.Page,
                PageSize = query.PageSize
            },
            cancellationToken);

        return Ok(new PagedResponse<BackofficeOrderListItemResponse>
        {
            Items = result.Items.Select(MapListItem).ToArray(),
            Pagination = new PaginationResponse
            {
                Page = result.Page,
                PageSize = result.PageSize,
                TotalItems = result.TotalItems,
                TotalPages = result.TotalPages
            }
        });
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BackofficeOrderDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BackofficeOrderDetailResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _backofficeOrderService.GetByIdAsync(id, cancellationToken);

        return Ok(MapDetail(result));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(UpdateOrderStatusResponse), StatusCodes.Status200OK)] // Assuming this exists
    public async Task<ActionResult<UpdateOrderStatusResponse>> UpdateStatus(
        Guid id,
        [FromBody] BackofficeUpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _backofficeOrderService.UpdateStatusAsync(
            new BackofficeUpdateOrderStatusCommand
            {
                OrderId = id,
                TargetStatus = request.TargetStatus,
                ExpectedRowVersion = request.ExpectedRowVersion,
                Reason = request.Reason
            },
            cancellationToken);

        return Ok(new UpdateOrderStatusResponse
        {
            Id = result.Id,
            OrderNumber = result.OrderNumber,
            PreviousStatus = result.PreviousStatus,
            CurrentStatus = result.CurrentStatus,
            RowVersion = result.RowVersion,
            UpdatedAt = result.UpdatedAt
        });
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(CancelOrderResponse), StatusCodes.Status200OK)] // Assuming this exists
    public async Task<ActionResult<CancelOrderResponse>> Cancel(
        Guid id,
        [FromBody] BackofficeCancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _backofficeOrderService.CancelAsync(
            new BackofficeCancelOrderCommand
            {
                OrderId = id,
                ExpectedRowVersion = request.ExpectedRowVersion,
                CancellationReason = request.CancellationReason,
                Reason = request.Reason
            },
            cancellationToken);

        return Ok(new CancelOrderResponse
        {
            Id = result.Id,
            OrderNumber = result.OrderNumber,
            PreviousStatus = result.PreviousStatus,
            CurrentStatus = result.CurrentStatus,
            CancellationReason = result.CancellationReason,
            StockRestoreApplied = result.StockRestoreApplied,
            RowVersion = result.RowVersion,
            PaymentRefundRequired = result.PaymentRefundRequired,
            UpdatedAt = result.UpdatedAt,
            StockRestored = result.StockRestored
                .Select(item => new StockRestoredResponse
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity
                })
                .ToArray(),
            StockNotRestored = result.StockNotRestored
                .Select(item => new StockNotRestoredResponse
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Reason = item.Reason
                })
                .ToArray()
        });
    }

    private static BackofficeOrderListItemResponse MapListItem(BackofficeOrderListItemDto item)
    {
        return new BackofficeOrderListItemResponse
        {
            Id = item.Id,
            OrderNumber = item.OrderNumber,
            StoreId = item.StoreId,
            StoreName = item.StoreName,
            CustomerId = item.CustomerId,
            CustomerName = item.CustomerName,
            Status = item.Status,
            TotalAmount = item.TotalAmount,
            RowVersion = item.RowVersion,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    private static BackofficeOrderDetailResponse MapDetail(BackofficeOrderDetailDto item)
    {
        return new BackofficeOrderDetailResponse
        {
            Id = item.Id,
            OrderNumber = item.OrderNumber,
            StoreId = item.StoreId,
            StoreName = item.StoreName,
            CustomerId = item.CustomerId,
            CustomerName = item.CustomerName,
            Status = item.Status,
            ShippingAddress = item.ShippingAddress,
            TotalAmount = item.TotalAmount,
            RowVersion = item.RowVersion,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            Items = item.Items
                .Select(orderItem => new BackofficeOrderItemResponse
                {
                    ProductId = orderItem.ProductId,
                    ProductName = orderItem.ProductName,
                    Quantity = orderItem.Quantity,
                    UnitPrice = orderItem.UnitPrice,
                    LineTotal = orderItem.LineTotal
                })
                .ToArray(),
            StatusHistory = item.StatusHistory
                .Select(history => new BackofficeOrderStatusHistoryResponse
                {
                    FromStatus = history.FromStatus,
                    ToStatus = history.ToStatus,
                    Reason = history.Reason,
                    ChangedBy = history.ChangedBy,
                    ChangedAt = history.ChangedAt
                })
                .ToArray()
        };
    }
}