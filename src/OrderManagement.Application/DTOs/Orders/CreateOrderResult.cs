namespace OrderManagement.Application.DTOs.Orders;

public sealed record CreateOrderResult
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required Guid CustomerId { get; init; }

    public required string Status { get; init; }

    public required string ShippingAddress { get; init; }

    public required decimal TotalAmount { get; init; }

    public required long RowVersion { get; init; }

    public required IReadOnlyCollection<CreateOrderItemResult> Items { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateOrderItemResult
{
    public required Guid ProductId { get; init; }

    public required string ProductName { get; init; }

    public required int Quantity { get; init; }

    public required decimal UnitPrice { get; init; }

    public required decimal LineTotal { get; init; }
}