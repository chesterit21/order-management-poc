using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IOrderRepository
{
    Task<CreateOrderResult> CreateAsync(
        CreateOrderPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<GetOrderResult?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight read-only projection of an order's ownership fields
    /// (CustomerId, Status, RowVersion, OrderNumber). Does NOT take a row lock.
    /// Used by the application service for authorization checks before
    /// entering the transactional mutation path.
    /// Returns null if the order does not exist.
    /// </summary>
    Task<OrderOwnershipResult?> GetOrderOwnershipAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PagedResult<OrderListItemResult>> ListAsync(
        ListOrdersQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        CancellationToken cancellationToken = default);

    Task<UpdateOrderStatusResult> UpdateStatusAsync(
        UpdateOrderStatusPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<CancelOrderResult> CancelAsync(
        CancelOrderPersistenceRequest request,
        CancellationToken cancellationToken = default);
}