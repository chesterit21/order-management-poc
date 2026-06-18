using OrderManagement.Application.DTOs.Common;
using OrderManagement.Application.DTOs.Orders.Backoffice;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IBackofficeOrderRepository
{
    Task<PagedResult<BackofficeOrderListItemDto>> ListAsync(
        BackofficeOrderListQueryDto query,
        IReadOnlyCollection<Guid>? allowedStoreIds,
        CancellationToken cancellationToken = default);

    Task<BackofficeOrderDetailDto?> GetDetailByIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<BackofficeOrderAccessDto?> GetAccessAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);
}