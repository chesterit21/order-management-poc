namespace OrderManagement.Application.DTOs.Orders;

public sealed record UpdateOrderStatusResult
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required string PreviousStatus { get; init; }

    public required string CurrentStatus { get; init; }

    public required long RowVersion { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}