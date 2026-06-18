namespace OrderManagement.Application.DTOs.Stores;

public sealed record SetStoreOperatorStatusCommand
{
    public required Guid StoreId { get; init; }

    public required Guid OperatorUserId { get; init; }

    public required bool IsActive { get; init; }
}