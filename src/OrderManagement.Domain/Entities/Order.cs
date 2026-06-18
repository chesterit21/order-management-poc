using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.ValueObjects;

namespace OrderManagement.Domain.Entities;

public sealed class Order : AuditableEntity
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
    }

    private Order(
        Guid id,
        string orderNumber,
        Guid customerId,
        OrderStatus status,
        string shippingAddress,
        decimal totalAmount,
        long rowVersion,
        Guid createdBy,
        Guid? updatedBy,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer id is required.", nameof(customerId));
        }

        if (string.IsNullOrWhiteSpace(shippingAddress))
        {
            throw new ArgumentException("Shipping address is required.", nameof(shippingAddress));
        }

        OrderNumber = OrderNumber.From(orderNumber);
        CustomerId = customerId;
        Status = status;
        ShippingAddress = shippingAddress.Trim();
        TotalAmount = Money.From(totalAmount);
        RowVersion = rowVersion;
        CreatedBy = createdBy;
        UpdatedBy = updatedBy;
        SetCreatedAt(createdAt);
    }

    public OrderNumber OrderNumber { get; private set; }

    public Guid CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public string ShippingAddress { get; private set; } = string.Empty;

    public Money TotalAmount { get; private set; }

    public long RowVersion { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public static Order Create(
        string orderNumber,
        Guid customerId,
        string shippingAddress,
        IEnumerable<OrderItem> items,
        Guid createdBy,
        DateTimeOffset now)
    {
        var order = new Order(
            Guid.NewGuid(),
            orderNumber,
            customerId,
            OrderStatus.Pending,
            shippingAddress,
            0,
            1,
            createdBy,
            null,
            now);

        order.AddItems(items);
        order.RecalculateTotal();

        return order;
    }

    public static Order Rehydrate(
        Guid id,
        string orderNumber,
        Guid customerId,
        OrderStatus status,
        string shippingAddress,
        decimal totalAmount,
        long rowVersion,
        Guid createdBy,
        Guid? updatedBy,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IEnumerable<OrderItem>? items = null)
    {
        var order = new Order(
            id,
            orderNumber,
            customerId,
            status,
            shippingAddress,
            totalAmount,
            rowVersion,
            createdBy,
            updatedBy,
            createdAt);

        order.SetUpdatedAt(updatedAt);

        if (items is not null)
        {
            order._items.AddRange(items);
        }

        return order;
    }

    public bool IsTerminal()
    {
        return Status is OrderStatus.Delivered or OrderStatus.Cancelled;
    }

    public bool CanBeCancelled()
    {
        return Status is OrderStatus.Pending or OrderStatus.Confirmed;
    }

    public void ChangeStatus(OrderStatus targetStatus, Guid updatedBy, DateTimeOffset now)
    {
        if (IsTerminal())
        {
            throw new InvalidOperationException($"Order is already in terminal state {Status}.");
        }

        Status = targetStatus;
        UpdatedBy = updatedBy;
        RowVersion++;
        SetUpdatedAt(now);
    }

    public void Cancel(Guid updatedBy, DateTimeOffset now)
    {
        if (!CanBeCancelled())
        {
            throw new InvalidOperationException($"Order cannot be cancelled from status {Status}.");
        }

        Status = OrderStatus.Cancelled;
        UpdatedBy = updatedBy;
        RowVersion++;
        SetUpdatedAt(now);
    }

    private void AddItems(IEnumerable<OrderItem> items)
    {
        var itemList = items.ToList();

        if (itemList.Count == 0)
        {
            throw new ArgumentException("Order must contain at least one item.", nameof(items));
        }

        _items.AddRange(itemList);
    }

    private void RecalculateTotal()
    {
        TotalAmount = _items.Aggregate(Money.Zero, (current, item) => current + item.LineTotal);
    }
}