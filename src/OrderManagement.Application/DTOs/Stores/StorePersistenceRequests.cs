namespace OrderManagement.Application.DTOs.Stores;

public sealed record OpenStorePersistenceRequest
{
    public required Guid StoreId { get; init; }

    public required Guid OwnerUserId { get; init; }

    public required string StoreName { get; init; }

    public required string Slug { get; init; }

    public string? Description { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed record UpdateStorePersistenceRequest
{
    public required Guid StoreId { get; init; }

    public required string StoreName { get; init; }

    public string? Description { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed record CreateStoreOperatorPersistenceRequest
{
    public required Guid StoreId { get; init; }

    public required Guid OperatorUserId { get; init; }

    public required string Username { get; init; }

    public required string PasswordHash { get; init; }

    public required string DisplayName { get; init; }

    public required Guid CreatedBy { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed record SetStoreOperatorStatusPersistenceRequest
{
    public required Guid StoreId { get; init; }

    public required Guid OperatorUserId { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset Now { get; init; }
}