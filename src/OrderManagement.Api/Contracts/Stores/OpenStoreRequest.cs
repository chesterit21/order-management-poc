namespace OrderManagement.Api.Contracts.Stores;

public sealed record OpenStoreRequest
{
    public string StoreName { get; init; } = string.Empty;

    public string? Description { get; init; }
}