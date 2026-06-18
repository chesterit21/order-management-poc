using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderManagement.Api.Contracts.Common;
using OrderManagement.Api.Contracts.Orders;
using OrderManagement.Api.Extensions;
using OrderManagement.Api.Filters;
using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
[Route("api/v1/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost]
    [RequireIdempotencyKeyFilter]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderService.CreateAsync(
            new CreateOrderCommand
            {
                IdempotencyKey = Request.GetRequiredIdempotencyKey(),
                Endpoint = Request.GetNormalizedEndpoint(),
                CustomerId = request.CustomerId,
                ShippingAddress = request.ShippingAddress,
                Items = request.Items
                    .Select(item => new CreateOrderItemCommand
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity
                    })
                    .ToArray()
            },
            cancellationToken);

        if (result.IsStoredResponse)
        {
            return new ContentResult
            {
                StatusCode = result.StatusCode,
                Content = result.StoredResponseBody,
                ContentType = "application/json"
            };
        }

        var response = MapCreateResponse(result.Response!);

        return CreatedAtAction(
            nameof(GetById),
            new { id = response.Id },
            response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderDetailResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _orderService.GetByIdAsync(id, cancellationToken);

        return Ok(MapDetailResponse(result));
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OrderListItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<OrderListItemResponse>>> List(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _orderService.ListAsync(
            new ListOrdersQueryDto
            {
                Status = query.Status,
                CustomerId = query.CustomerId,
                FromDate = query.FromDate,
                ToDate = query.ToDate,
                Page = query.Page,
                PageSize = query.PageSize
            },
            cancellationToken);

        return Ok(new PagedResponse<OrderListItemResponse>
        {
            Items = result.Items
                .Select(item => new OrderListItemResponse
                {
                    Id = item.Id,
                    OrderNumber = item.OrderNumber,
                    CustomerId = item.CustomerId,
                    CustomerName = item.CustomerName,
                    Status = item.Status,
                    TotalAmount = item.TotalAmount,
                    RowVersion = item.RowVersion,
                    CreatedAt = item.CreatedAt,
                    UpdatedAt = item.UpdatedAt
                })
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

    /// <summary>
    /// Update the status of an order (customer/buyer-facing endpoint).
    /// Only ApplicationAdmin can call this endpoint; seller users must use
    /// the backoffice order endpoint.
    /// Valid transitions:
    ///   Pending → Confirmed | Cancelled (cancel via dedicated endpoint)
    ///   Confirmed → Shipped | Cancelled (cancel via dedicated endpoint)
    ///   Shipped → Delivered
    ///   Delivered / Cancelled → terminal (no further changes)
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(UpdateOrderStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UpdateOrderStatusResponse>> UpdateStatus(
        Guid id,
        [FromBody] UpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderService.UpdateStatusAsync(
            new UpdateOrderStatusCommand
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

    /// <summary>
    /// Cancel an order (customer/buyer-facing endpoint).
    /// Only allowed when the order is still Pending or Confirmed.
    /// Stock is restored atomically within the same transaction.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(CancelOrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CancelOrderResponse>> Cancel(
        Guid id,
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _orderService.CancelAsync(
            new CancelOrderCommand
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

    private static CreateOrderResponse MapCreateResponse(CreateOrderResult result)
    {
        return new CreateOrderResponse
        {
            Id = result.Id,
            OrderNumber = result.OrderNumber,
            CustomerId = result.CustomerId,
            Status = result.Status,
            ShippingAddress = result.ShippingAddress,
            TotalAmount = result.TotalAmount,
            RowVersion = result.RowVersion,
            CreatedAt = result.CreatedAt,
            Items = result.Items
                .Select(item => new CreateOrderItemResponse
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal
                })
                .ToArray()
        };
    }

    private static OrderDetailResponse MapDetailResponse(GetOrderResult result)
    {
        return new OrderDetailResponse
        {
            Id = result.Id,
            OrderNumber = result.OrderNumber,
            CustomerId = result.CustomerId,
            CustomerName = result.CustomerName,
            Status = result.Status,
            ShippingAddress = result.ShippingAddress,
            TotalAmount = result.TotalAmount,
            RowVersion = result.RowVersion,
            CreatedAt = result.CreatedAt,
            UpdatedAt = result.UpdatedAt,
            Items = result.Items
                .Select(item => new OrderItemResponse
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal
                })
                .ToArray(),
            StatusHistory = result.StatusHistory
                .Select(history => new OrderStatusHistoryResponse
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