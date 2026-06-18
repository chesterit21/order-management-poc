namespace OrderManagement.Application.DTOs.Orders;

public sealed record GetOrderResult
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required Guid CustomerId { get; init; }

    public required string CustomerName { get; init; }

    public required string Status { get; init; }

    public required string ShippingAddress { get; init; }

    public required decimal TotalAmount { get; init; }

    public required long RowVersion { get; init; }

    public required IReadOnlyCollection<OrderItemResult> Items { get; init; }

    public required IReadOnlyCollection<OrderStatusHistoryResult> StatusHistory { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public bool HasPaidPayment { get; init; }
}

public sealed record OrderItemResult
{
    public required Guid ProductId { get; init; }

    public required string ProductName { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }
}

public sealed record OrderStatusHistoryResult
{
    public string? FromStatus { get; init; }

    public required string ToStatus { get; init; }

    public string? Reason { get; init; }

    public required Guid ChangedBy { get; init; }

    public required DateTimeOffset ChangedAt { get; init; }
}