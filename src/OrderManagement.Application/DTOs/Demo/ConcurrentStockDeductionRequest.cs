namespace OrderManagement.Application.DTOs.Demo;

public sealed class ConcurrentStockDeductionRequest
{
    /// <summary>Product ID to order.</summary>
    public Guid ProductId { get; init; }

    /// <summary>Quantity each concurrent request will try to order.</summary>
    public int Quantity { get; init; }

    /// <summary>Buyer customer ID for the orders.</summary>
    public Guid CustomerId { get; init; }

    /// <summary>Shipping address to use for the orders.</summary>
    public string ShippingAddress { get; init; } = "Demo Address, Jakarta";
}
