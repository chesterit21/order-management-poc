namespace OrderManagement.Api.Contracts.Orders;

public sealed record CreateOrderRequest
{
    public Guid CustomerId { get; init; }

    public IReadOnlyCollection<CreateOrderItemRequest> Items { get; init; } = [];

    public string ShippingAddress { get; init; } = string.Empty;
}