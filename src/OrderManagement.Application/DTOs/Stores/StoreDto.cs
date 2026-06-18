namespace OrderManagement.Application.DTOs.Stores;

public sealed record StoreDto
{
    public required Guid Id { get; init; }

    public required Guid OwnerUserId { get; init; }

    public required string StoreName { get; init; }

    public required string Slug { get; init; }

    public string? Description { get; init; }

    public string? LogoUrl { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}