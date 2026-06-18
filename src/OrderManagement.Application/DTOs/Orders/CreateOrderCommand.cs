namespace OrderManagement.Application.DTOs.Orders;

public sealed record CreateOrderCommand
{
    public required string IdempotencyKey { get; init; }

    public required string Endpoint { get; init; }

    public required Guid CustomerId { get; init; }

    public required IReadOnlyCollection<CreateOrderItemCommand> Items { get; init; }

    public required string ShippingAddress { get; init; }
}