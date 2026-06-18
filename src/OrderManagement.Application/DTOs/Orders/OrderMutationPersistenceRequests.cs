using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.DTOs.Orders;

public sealed record UpdateOrderStatusPersistenceRequest
{
    public required Guid OrderId { get; init; }

    public required OrderStatus TargetStatus { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public required Guid UpdatedBy { get; init; }

    public required UserRole UpdatedByRole { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed record CancelOrderPersistenceRequest
{
    public required Guid OrderId { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public required Guid CancelledBy { get; init; }

    public required UserRole CancelledByRole { get; init; }

    public required OrderCancellationReason CancellationReason { get; init; }

    public required bool RestoreStock { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset Now { get; init; }
}