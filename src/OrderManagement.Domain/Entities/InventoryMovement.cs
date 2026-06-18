using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public sealed class InventoryMovement : Entity
{
    private InventoryMovement()
    {
    }

    private InventoryMovement(
        Guid id,
        Guid productId,
        Guid? orderId,
        InventoryMovementType movementType,
        int quantity,
        int stockBefore,
        int stockAfter,
        string? reason,
        Guid? createdBy,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (stockBefore < 0 || stockAfter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stockAfter), "Stock values cannot be negative.");
        }

        ProductId = productId;
        OrderId = orderId;
        MovementType = movementType;
        Quantity = quantity;
        StockBefore = stockBefore;
        StockAfter = stockAfter;
        Reason = reason;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid ProductId { get; private set; }

    public Guid? OrderId { get; private set; }

    public InventoryMovementType MovementType { get; private set; }

    public int Quantity { get; private set; }

    public int StockBefore { get; private set; }

    public int StockAfter { get; private set; }

    public string? Reason { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static InventoryMovement CreateOrderDeduction(
        Guid productId,
        Guid orderId,
        int quantity,
        int stockBefore,
        int stockAfter,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new InventoryMovement(
            Guid.NewGuid(),
            productId,
            orderId,
            InventoryMovementType.OrderCreatedDeduction,
            quantity,
            stockBefore,
            stockAfter,
            "Stock deducted when order was created.",
            createdBy,
            now);
    }

    public static InventoryMovement CreateOrderCancelRestore(
        Guid productId,
        Guid orderId,
        int quantity,
        int stockBefore,
        int stockAfter,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new InventoryMovement(
            Guid.NewGuid(),
            productId,
            orderId,
            InventoryMovementType.OrderCancelledRestore,
            quantity,
            stockBefore,
            stockAfter,
            "Stock restored when order was cancelled.",
            createdBy,
            now);
    }

    public static InventoryMovement Rehydrate(
        Guid id,
        Guid productId,
        Guid? orderId,
        InventoryMovementType movementType,
        int quantity,
        int stockBefore,
        int stockAfter,
        string? reason,
        Guid? createdBy,
        DateTimeOffset createdAt)
    {
        return new InventoryMovement(
            id,
            productId,
            orderId,
            movementType,
            quantity,
            stockBefore,
            stockAfter,
            reason,
            createdBy,
            createdAt);
    }
}