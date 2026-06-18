namespace OrderManagement.Application.DTOs.Stores;

public sealed record CreateStoreOperatorCommand
{
    public required Guid StoreId { get; init; }

    public required string Username { get; init; }

    public required string Password { get; init; }

    public required string DisplayName { get; init; }
}