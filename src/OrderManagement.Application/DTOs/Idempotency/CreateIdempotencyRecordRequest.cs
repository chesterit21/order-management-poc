namespace OrderManagement.Application.DTOs.Idempotency;

public sealed record CreateIdempotencyRecordRequest
{
    public required string Key { get; init; }

    public required Guid UserId { get; init; }

    public required string Endpoint { get; init; }

    public required string RequestHash { get; init; }

    public required DateTimeOffset LockedUntil { get; init; }

    public required DateTimeOffset Now { get; init; }
}