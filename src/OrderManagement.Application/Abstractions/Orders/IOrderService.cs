using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Application.Abstractions.Orders;

public interface IOrderService
{
    Task<CreateOrderOperationResult> CreateAsync(
        CreateOrderCommand command,
        CancellationToken cancellationToken = default);

    Task<GetOrderResult> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PagedResult<OrderListItemResult>> ListAsync(
        ListOrdersQueryDto query,
        CancellationToken cancellationToken = default);

    Task<UpdateOrderStatusResult> UpdateStatusAsync(
        UpdateOrderStatusCommand command,
        CancellationToken cancellationToken = default);

    Task<CancelOrderResult> CancelAsync(
        CancelOrderCommand command,
        CancellationToken cancellationToken = default);
}