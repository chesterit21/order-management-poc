using OrderManagement.Application.DTOs.Stores;

namespace OrderManagement.Application.Abstractions.Stores;

public interface IStoreOperatorService
{
    Task<IReadOnlyCollection<StoreMemberDto>> ListOperatorsAsync(
        Guid storeId,
        CancellationToken cancellationToken = default);

    Task<StoreMemberDto> CreateOperatorAsync(
        CreateStoreOperatorCommand command,
        CancellationToken cancellationToken = default);

    Task<StoreMemberDto> SetOperatorStatusAsync(
        SetStoreOperatorStatusCommand command,
        CancellationToken cancellationToken = default);
}