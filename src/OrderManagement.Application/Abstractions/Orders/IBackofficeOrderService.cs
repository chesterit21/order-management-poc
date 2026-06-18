using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;
using OrderManagement.Application.DTOs.Orders.Backoffice;

namespace OrderManagement.Application.Abstractions.Orders;

public interface IBackofficeOrderService
{
    Task<PagedResult<BackofficeOrderListItemDto>> ListAsync(
        BackofficeOrderListQueryDto query,
        CancellationToken cancellationToken = default);

    Task<BackofficeOrderDetailDto> GetByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<UpdateOrderStatusResult> UpdateStatusAsync(
        BackofficeUpdateOrderStatusCommand command,
        CancellationToken cancellationToken = default);

    Task<CancelOrderResult> CancelAsync(
        BackofficeCancelOrderCommand command,
        CancellationToken cancellationToken = default);
}