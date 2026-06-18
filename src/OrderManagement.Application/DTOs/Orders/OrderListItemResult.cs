namespace OrderManagement.Application.DTOs.Orders;

public sealed record OrderListItemResult
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public required string Status { get; init; }

    public required decimal TotalAmount { get; init; }

    public required long RowVersion { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}