namespace OrderManagement.Api.Contracts.Stores;

public sealed record UpdateStoreRequest
{
    public string StoreName { get; init; } = string.Empty;

    public string? Description { get; init; }
}