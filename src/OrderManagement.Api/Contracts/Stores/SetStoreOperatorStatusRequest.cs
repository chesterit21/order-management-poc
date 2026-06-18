namespace OrderManagement.Api.Contracts.Stores;

public sealed record SetStoreOperatorStatusRequest
{
    public bool IsActive { get; init; }
}