namespace OrderManagement.Application.DTOs.Stores;

public sealed record StoreMemberDto
{
    public required Guid Id { get; init; }

    public required Guid StoreId { get; init; }

    public required Guid UserId { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string Role { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}