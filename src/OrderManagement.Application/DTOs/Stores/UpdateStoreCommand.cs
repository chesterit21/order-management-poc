namespace OrderManagement.Application.DTOs.Stores;

public sealed record UpdateStoreCommand
{
    public required Guid StoreId { get; init; }

    public required string StoreName { get; init; }

    public string? Description { get; init; }
}