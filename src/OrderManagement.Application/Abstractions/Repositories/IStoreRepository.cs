using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Abstractions.Repositories;

public interface IStoreRepository
{
    Task<bool> UserHasOwnedStoreAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken = default);

    Task<StoreDto?> GetByIdAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreDto>> ListByUserMembershipAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreDto>> ListAllAsync(
        CancellationToken cancellationToken = default);

    Task<bool> IsStoreOwnerAsync(
        Guid storeId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> IsStoreOperatorAsync(
        Guid storeId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<StoreDto> OpenStoreAsync(
        OpenStorePersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<StoreDto> UpdateStoreAsync(
        UpdateStorePersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<StoreMemberDto>> ListOperatorsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<StoreMemberDto> CreateOperatorAsync(
        CreateStoreOperatorPersistenceRequest request,
        CancellationToken cancellationToken = default);

    Task<StoreMemberDto> SetOperatorStatusAsync(
        SetStoreOperatorStatusPersistenceRequest request,
        CancellationToken cancellationToken = default);
}