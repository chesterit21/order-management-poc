namespace OrderManagement.Api.Contracts.Stores;

public sealed record StoreResponse
{
    public Guid Id { get; init; }

    public Guid OwnerUserId { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? LogoUrl { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}