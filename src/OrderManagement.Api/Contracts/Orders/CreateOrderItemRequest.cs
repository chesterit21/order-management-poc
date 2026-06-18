namespace OrderManagement.Api.Contracts.Orders;

public sealed record CreateOrderItemRequest
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }
}