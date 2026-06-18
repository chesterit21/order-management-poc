using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public sealed class OrderStatusHistory : Entity
{
    private OrderStatusHistory()
    {
    }

    private OrderStatusHistory(
        Guid id,
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        string? reason,
        Guid changedBy,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        if (changedBy == Guid.Empty)
        {
            throw new ArgumentException("Changed by is required.", nameof(changedBy));
        }

        OrderId = orderId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = reason;
        ChangedBy = changedBy;
        CreatedAt = createdAt;
    }

    public Guid OrderId { get; private set; }

    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public string? Reason { get; private set; }

    public Guid ChangedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static OrderStatusHistory Create(
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        string? reason,
        Guid changedBy,
        DateTimeOffset now)
    {
        return new OrderStatusHistory(
            Guid.NewGuid(),
            orderId,
            fromStatus,
            toStatus,
            reason,
            changedBy,
            now);
    }

    public static OrderStatusHistory Rehydrate(
        Guid id,
        Guid orderId,
        OrderStatus? fromStatus,
        OrderStatus toStatus,
        string? reason,
        Guid changedBy,
        DateTimeOffset createdAt)
    {
        return new OrderStatusHistory(
            id,
            orderId,
            fromStatus,
            toStatus,
            reason,
            changedBy,
            createdAt);
    }
}