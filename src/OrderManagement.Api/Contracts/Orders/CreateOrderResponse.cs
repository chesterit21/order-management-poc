namespace OrderManagement.Api.Contracts.Orders;

public sealed record CreateOrderResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public Guid CustomerId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string ShippingAddress { get; init; } = string.Empty;

    public decimal TotalAmount { get; init; }

    public long RowVersion { get; init; }

    public IReadOnlyCollection<CreateOrderItemResponse> Items { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record CreateOrderItemResponse
{
    public Guid ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineTotal { get; init; }
}