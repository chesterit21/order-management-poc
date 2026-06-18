namespace OrderManagement.Application.DTOs.Orders;

public sealed record CreateOrderItemCommand
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }
}