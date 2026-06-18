using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Abstractions.Stores;

public interface IStoreService
{
    Task<StoreDto> OpenStoreAsync(
        OpenStoreCommand command,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreDto>> GetMyStoresAsync(
        CancellationToken cancellationToken = default);

    Task<StoreDto> GetByIdAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<StoreDto> UpdateAsync(
        UpdateStoreCommand command,
        CancellationToken cancellationToken = default);
}