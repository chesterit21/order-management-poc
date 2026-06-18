namespace OrderManagement.Application.DTOs.Orders;

public sealed record CreateOrderPersistenceRequest
{
    public required Guid OrderId { get; init; }


    public required Guid CustomerId { get; init; }

    public required Guid CreatedBy { get; init; }

    public required string ShippingAddress { get; init; }

    public required IReadOnlyCollection<CreateOrderPersistenceItem> Items { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed record CreateOrderPersistenceItem
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }
}