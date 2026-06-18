namespace OrderManagement.Application.DTOs.Stores;

public sealed record OpenStoreCommand
{
    public required string StoreName { get; init; }

    public string? Description { get; init; }
}