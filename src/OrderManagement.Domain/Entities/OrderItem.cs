using OrderManagement.Domain.Common;
using OrderManagement.Domain.ValueObjects;

namespace OrderManagement.Domain.Entities;

public sealed class OrderItem : Entity
{
    private OrderItem()
    {
    }

    private OrderItem(
        Guid id,
        Guid orderId,
        Guid productId,
        string productNameSnapshot,
        decimal unitPriceSnapshot,
        int quantity,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(productNameSnapshot))
        {
            throw new ArgumentException("Product name snapshot is required.", nameof(productNameSnapshot));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        OrderId = orderId;
        ProductId = productId;
        ProductNameSnapshot = productNameSnapshot.Trim();
        UnitPriceSnapshot = Money.From(unitPriceSnapshot);
        Quantity = quantity;
        LineTotal = UnitPriceSnapshot * quantity;
        CreatedAt = createdAt;
    }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public string ProductNameSnapshot { get; private set; } = string.Empty;

    public Money UnitPriceSnapshot { get; private set; }

    public int Quantity { get; private set; }

    public Money LineTotal { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static OrderItem Create(
        Guid orderId,
        Guid productId,
        string productNameSnapshot,
        decimal unitPriceSnapshot,
        int quantity,
        DateTimeOffset now)
    {
        return new OrderItem(
            Guid.NewGuid(),
            orderId,
            productId,
            productNameSnapshot,
            unitPriceSnapshot,
            quantity,
            now);
    }

    public static OrderItem Rehydrate(
        Guid id,
        Guid orderId,
        Guid productId,
        string productNameSnapshot,
        decimal unitPriceSnapshot,
        int quantity,
        decimal lineTotal,
        DateTimeOffset createdAt)
    {
        var item = new OrderItem(
            id,
            orderId,
            productId,
            productNameSnapshot,
            unitPriceSnapshot,
            quantity,
            createdAt);

        item.LineTotal = Money.From(lineTotal);

        return item;
    }
}