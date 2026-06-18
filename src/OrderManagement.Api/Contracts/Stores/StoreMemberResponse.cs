namespace OrderManagement.Api.Contracts.Stores;

public sealed record StoreMemberResponse
{
    public Guid Id { get; init; }

    public Guid StoreId { get; init; }

    public Guid UserId { get; init; }

    public string Username { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}