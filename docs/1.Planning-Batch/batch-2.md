Siap bro. Kita lanjut **Batch 2: Domain Model DDD, Enums, Value Objects, Error Codes, Exceptions**.

Batch ini fokus bikin fondasi DDD:

```text
Domain/Common
Domain/Enums
Domain/ValueObjects
Domain/Entities
Domain/Rules/Facts
Domain/Rules/Results
Application/Constants/ErrorCodes
Application/Exceptions
```

Belum masuk:

```text
Controller
Repository SQL
NRules implementation
Auth JWT
Middleware
Order transaction
```

> Catatan desain: karena kita pakai **Dapper**, entity domain tetap kita buat DDD-ish dengan behavior/factory, tapi mapping database nanti bisa dilakukan manual di repository supaya tidak memaksa entity punya public setter bebas.

***

# Batch 2 — Domain Model DDD

## 1. Domain Common

***

## `src/OrderManagement.Domain/Common/Entity.cs`

Replace isi file:

```csharp
namespace OrderManagement.Domain.Common;

public abstract class Entity
{
    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; protected set; }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (Id == Guid.Empty || other.Id == Guid.Empty)
        {
            return false;
        }

        return Id == other.Id && GetType() == other.GetType();
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), Id);
    }

    public static bool operator ==(Entity? left, Entity? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Entity? left, Entity? right)
    {
        return !Equals(left, right);
    }
}
```

***

## `src/OrderManagement.Domain/Common/AuditableEntity.cs`

Replace isi file:

```csharp
namespace OrderManagement.Domain.Common;

public abstract class AuditableEntity : Entity
{
    protected AuditableEntity()
    {
    }

    protected AuditableEntity(Guid id) : base(id)
    {
    }

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    public void SetCreatedAt(DateTimeOffset createdAt)
    {
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public void SetUpdatedAt(DateTimeOffset updatedAt)
    {
        UpdatedAt = updatedAt;
    }
}
```

***

# 2. Domain Enums

***

## `src/OrderManagement.Domain/Enums/UserRole.cs`

```csharp
namespace OrderManagement.Domain.Enums;

public enum UserRole
{
    Customer = 1,
    Admin = 2,
    Ops = 3
}
```

***

## `src/OrderManagement.Domain/Enums/OrderStatus.cs`

```csharp
namespace OrderManagement.Domain.Enums;

public enum OrderStatus
{
    Pending = 1,
    Confirmed = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 5
}
```

***

## `src/OrderManagement.Domain/Enums/PaymentStatus.cs`

```csharp
namespace OrderManagement.Domain.Enums;

public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Cancelled = 4,
    RefundRequired = 5,
    Refunded = 6
}
```

***

## `src/OrderManagement.Domain/Enums/InventoryMovementType.cs`

```csharp
namespace OrderManagement.Domain.Enums;

public enum InventoryMovementType
{
    OrderCreatedDeduction = 1,
    OrderCancelledRestore = 2,
    ManualAdjustment = 3
}
```

***

## `src/OrderManagement.Domain/Enums/IdempotencyStatus.cs`

```csharp
namespace OrderManagement.Domain.Enums;

public enum IdempotencyStatus
{
    InProgress = 1,
    Completed = 2,
    Failed = 3
}
```

***

# 3. Domain Constants

***

## `src/OrderManagement.Domain/Constants/DomainConstants.cs`

```csharp
namespace OrderManagement.Domain.Constants;

public static class DomainConstants
{
    public const int MaxSkuLength = 100;
    public const int MaxProductNameLength = 200;
    public const int MaxUsernameLength = 100;
    public const int MaxDisplayNameLength = 150;
    public const int MaxOrderNumberLength = 50;
    public const int MaxIdempotencyKeyLength = 200;
    public const int MaxEndpointLength = 200;
    public const int MaxPaymentProviderLength = 100;
    public const int MaxPaymentReferenceLength = 200;

    public const string OrderNumberPrefix = "ORD";
}
```

***

# 4. Value Objects

***

## `src/OrderManagement.Domain/ValueObjects/Money.cs`

```csharp
namespace OrderManagement.Domain.ValueObjects;

public readonly record struct Money
{
    public Money(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Money amount cannot be negative.");
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    public decimal Amount { get; }

    public static Money Zero => new(0);

    public static Money From(decimal amount)
    {
        return new Money(amount);
    }

    public static Money operator +(Money left, Money right)
    {
        return new Money(left.Amount + right.Amount);
    }

    public static Money operator -(Money left, Money right)
    {
        return new Money(left.Amount - right.Amount);
    }

    public static Money operator *(Money money, int multiplier)
    {
        if (multiplier < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier), "Multiplier cannot be negative.");
        }

        return new Money(money.Amount * multiplier);
    }

    public override string ToString()
    {
        return Amount.ToString("0.00");
    }
}
```

***

## `src/OrderManagement.Domain/ValueObjects/Sku.cs`

```csharp
using OrderManagement.Domain.Constants;

namespace OrderManagement.Domain.ValueObjects;

public readonly record struct Sku
{
    public Sku(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SKU is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length > DomainConstants.MaxSkuLength)
        {
            throw new ArgumentException($"SKU cannot be longer than {DomainConstants.MaxSkuLength} characters.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public static Sku From(string value)
    {
        return new Sku(value);
    }

    public override string ToString()
    {
        return Value;
    }
}
```

***

## `src/OrderManagement.Domain/ValueObjects/OrderNumber.cs`

```csharp
using OrderManagement.Domain.Constants;

namespace OrderManagement.Domain.ValueObjects;

public readonly record struct OrderNumber
{
    public OrderNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Order number is required.", nameof(value));
        }

        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length > DomainConstants.MaxOrderNumberLength)
        {
            throw new ArgumentException($"Order number cannot be longer than {DomainConstants.MaxOrderNumberLength} characters.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public static OrderNumber From(string value)
    {
        return new OrderNumber(value);
    }

    public static OrderNumber Generate(DateTimeOffset now, long sequence)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be greater than zero.");
        }

        var value = $"{DomainConstants.OrderNumberPrefix}-{now:yyyyMMdd}-{sequence:000000}";

        return new OrderNumber(value);
    }

    public override string ToString()
    {
        return Value;
    }
}
```

***

# 5. Domain Entities

***

## `src/OrderManagement.Domain/Entities/User.cs`

```csharp
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public sealed class User : AuditableEntity
{
    private User()
    {
    }

    private User(
        Guid id,
        string username,
        string passwordHash,
        string displayName,
        UserRole role,
        bool isActive,
        DateTimeOffset createdAt)
        : base(id)
    {
        Username = username;
        PasswordHash = passwordHash;
        DisplayName = displayName;
        Role = role;
        IsActive = isActive;
        SetCreatedAt(createdAt);
    }

    public string Username { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public static User Create(
        string username,
        string passwordHash,
        string displayName,
        UserRole role,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username is required.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        return new User(
            Guid.NewGuid(),
            username.Trim(),
            passwordHash,
            displayName.Trim(),
            role,
            true,
            now);
    }

    public static User Rehydrate(
        Guid id,
        string username,
        string passwordHash,
        string displayName,
        UserRole role,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var user = new User(
            id,
            username,
            passwordHash,
            displayName,
            role,
            isActive,
            createdAt);

        user.SetUpdatedAt(updatedAt);

        return user;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        SetUpdatedAt(now);
    }

    public bool HasRole(UserRole role)
    {
        return Role == role;
    }

    public bool IsAdminOrOps()
    {
        return Role is UserRole.Admin or UserRole.Ops;
    }
}
```

***

## `src/OrderManagement.Domain/Entities/Product.cs`

```csharp
using OrderManagement.Domain.Common;
using OrderManagement.Domain.ValueObjects;

namespace OrderManagement.Domain.Entities;

public sealed class Product : AuditableEntity
{
    private Product()
    {
    }

    private Product(
        Guid id,
        string sku,
        string name,
        int stockQuantity,
        decimal price,
        long rowVersion,
        bool isActive,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (stockQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stockQuantity), "Stock quantity cannot be negative.");
        }

        Sku = Sku.From(sku);
        Name = name;
        StockQuantity = stockQuantity;
        Price = Money.From(price);
        RowVersion = rowVersion;
        IsActive = isActive;
        SetCreatedAt(createdAt);
    }

    public Sku Sku { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int StockQuantity { get; private set; }

    public Money Price { get; private set; }

    public long RowVersion { get; private set; }

    public bool IsActive { get; private set; }

    public static Product Create(
        string sku,
        string name,
        int stockQuantity,
        decimal price,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }

        return new Product(
            Guid.NewGuid(),
            sku,
            name.Trim(),
            stockQuantity,
            price,
            1,
            true,
            now);
    }

    public static Product Rehydrate(
        Guid id,
        string sku,
        string name,
        int stockQuantity,
        decimal price,
        long rowVersion,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var product = new Product(
            id,
            sku,
            name,
            stockQuantity,
            price,
            rowVersion,
            isActive,
            createdAt);

        product.SetUpdatedAt(updatedAt);

        return product;
    }

    public bool HasEnoughStock(int requestedQuantity)
    {
        return requestedQuantity > 0 && StockQuantity >= requestedQuantity;
    }

    public void DeductStock(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (StockQuantity < quantity)
        {
            throw new InvalidOperationException("Stock is not sufficient.");
        }

        StockQuantity -= quantity;
        RowVersion++;
        SetUpdatedAt(now);
    }

    public void RestoreStock(int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        StockQuantity += quantity;
        RowVersion++;
        SetUpdatedAt(now);
    }
}
```

***

## `src/OrderManagement.Domain/Entities/OrderItem.cs`

```csharp
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
```

***

## `src/OrderManagement.Domain/Entities/Order.cs`

```csharp
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
```

> Nanti pas implement `CreateOrderService`, ada sedikit catatan: karena `OrderItem.Create()` butuh `orderId`, kita bisa generate `orderId` dulu dari application service atau kita buat overload factory. Untuk sekarang entity ini sudah cukup aman, nanti Batch 8 kita rapikan sesuai flow transaction.

***

## `src/OrderManagement.Domain/Entities/InventoryMovement.cs`

```csharp
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
```

***

## `src/OrderManagement.Domain/Entities/OrderStatusHistory.cs`

```csharp
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
```

***

## `src/OrderManagement.Domain/Entities/Payment.cs`

```csharp
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.ValueObjects;

namespace OrderManagement.Domain.Entities;

public sealed class Payment : AuditableEntity
{
    private Payment()
    {
    }

    private Payment(
        Guid id,
        Guid orderId,
        decimal amount,
        PaymentStatus status,
        string provider,
        string? paymentReference,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("Payment provider is required.", nameof(provider));
        }

        OrderId = orderId;
        Amount = Money.From(amount);
        Status = status;
        Provider = provider.Trim();
        PaymentReference = paymentReference;
        SetCreatedAt(createdAt);
    }

    public Guid OrderId { get; private set; }

    public Money Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string? PaymentReference { get; private set; }

    public static Payment CreatePaid(
        Guid orderId,
        decimal amount,
        string provider,
        string paymentReference,
        DateTimeOffset now)
    {
        return new Payment(
            Guid.NewGuid(),
            orderId,
            amount,
            PaymentStatus.Paid,
            provider,
            paymentReference,
            now);
    }

    public static Payment CreateFailed(
        Guid orderId,
        decimal amount,
        string provider,
        string? paymentReference,
        DateTimeOffset now)
    {
        return new Payment(
            Guid.NewGuid(),
            orderId,
            amount,
            PaymentStatus.Failed,
            provider,
            paymentReference,
            now);
    }

    public static Payment Rehydrate(
        Guid id,
        Guid orderId,
        decimal amount,
        PaymentStatus status,
        string provider,
        string? paymentReference,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var payment = new Payment(
            id,
            orderId,
            amount,
            status,
            provider,
            paymentReference,
            createdAt);

        payment.SetUpdatedAt(updatedAt);

        return payment;
    }

    public void MarkRefundRequired(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Paid)
        {
            throw new InvalidOperationException("Only paid payment can be marked as refund required.");
        }

        Status = PaymentStatus.RefundRequired;
        SetUpdatedAt(now);
    }

    public void MarkRefunded(DateTimeOffset now)
    {
        if (Status is not PaymentStatus.Paid and not PaymentStatus.RefundRequired)
        {
            throw new InvalidOperationException("Only paid or refund required payment can be marked as refunded.");
        }

        Status = PaymentStatus.Refunded;
        SetUpdatedAt(now);
    }
}
```

***

## `src/OrderManagement.Domain/Entities/IdempotencyRecord.cs`

```csharp
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public sealed class IdempotencyRecord : AuditableEntity
{
    private IdempotencyRecord()
    {
    }

    private IdempotencyRecord(
        Guid id,
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        IdempotencyStatus status,
        int? responseStatusCode,
        string? responseBody,
        string? resourceType,
        Guid? resourceId,
        DateTimeOffset? lockedUntil,
        DateTimeOffset createdAt)
        : base(id)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(key));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ArgumentException("Endpoint is required.", nameof(endpoint));
        }

        if (string.IsNullOrWhiteSpace(requestHash))
        {
            throw new ArgumentException("Request hash is required.", nameof(requestHash));
        }

        Key = key.Trim();
        UserId = userId;
        Endpoint = endpoint.Trim();
        RequestHash = requestHash;
        Status = status;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        ResourceType = resourceType;
        ResourceId = resourceId;
        LockedUntil = lockedUntil;
        SetCreatedAt(createdAt);
    }

    public string Key { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    public string Endpoint { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public IdempotencyStatus Status { get; private set; }

    public int? ResponseStatusCode { get; private set; }

    public string? ResponseBody { get; private set; }

    public string? ResourceType { get; private set; }

    public Guid? ResourceId { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public static IdempotencyRecord CreateInProgress(
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        DateTimeOffset lockedUntil,
        DateTimeOffset now)
    {
        return new IdempotencyRecord(
            Guid.NewGuid(),
            key,
            userId,
            endpoint,
            requestHash,
            IdempotencyStatus.InProgress,
            null,
            null,
            null,
            null,
            lockedUntil,
            now);
    }

    public static IdempotencyRecord Rehydrate(
        Guid id,
        string key,
        Guid userId,
        string endpoint,
        string requestHash,
        IdempotencyStatus status,
        int? responseStatusCode,
        string? responseBody,
        string? resourceType,
        Guid? resourceId,
        DateTimeOffset? lockedUntil,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        var record = new IdempotencyRecord(
            id,
            key,
            userId,
            endpoint,
            requestHash,
            status,
            responseStatusCode,
            responseBody,
            resourceType,
            resourceId,
            lockedUntil,
            createdAt);

        record.SetUpdatedAt(updatedAt);

        return record;
    }

    public bool HasDifferentRequestHash(string requestHash)
    {
        return !string.Equals(RequestHash, requestHash, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsInProgress(DateTimeOffset now)
    {
        if (Status != IdempotencyStatus.InProgress)
        {
            return false;
        }

        return LockedUntil is null || LockedUntil > now;
    }

    public void MarkCompleted(
        int responseStatusCode,
        string responseBody,
        string resourceType,
        Guid resourceId,
        DateTimeOffset now)
    {
        Status = IdempotencyStatus.Completed;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        ResourceType = resourceType;
        ResourceId = resourceId;
        LockedUntil = null;
        SetUpdatedAt(now);
    }

    public void MarkFailed(
        int responseStatusCode,
        string responseBody,
        DateTimeOffset now)
    {
        Status = IdempotencyStatus.Failed;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        LockedUntil = null;
        SetUpdatedAt(now);
    }
}
```

***

# 6. Domain Rule Facts

***

## `src/OrderManagement.Domain/Rules/Facts/OrderTransitionFact.cs`

```csharp
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Rules.Facts;

public sealed class OrderTransitionFact
{
    public required Guid OrderId { get; init; }

    public required OrderStatus CurrentStatus { get; init; }

    public required OrderStatus TargetStatus { get; init; }

    public required Guid RequestedByUserId { get; init; }

    public required UserRole RequestedByRole { get; init; }

    public bool IsAllowed { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}
```

***

## `src/OrderManagement.Domain/Rules/Facts/CancelOrderFact.cs`

```csharp
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Rules.Facts;

public sealed class CancelOrderFact
{
    public required Guid OrderId { get; init; }

    public required Guid CustomerId { get; init; }

    public required OrderStatus CurrentStatus { get; init; }

    public required Guid RequestedByUserId { get; init; }

    public required UserRole RequestedByRole { get; init; }

    public bool IsAllowed { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}
```

***

## `src/OrderManagement.Domain/Rules/Facts/PaymentFact.cs`

```csharp
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Rules.Facts;

public sealed class PaymentFact
{
    public required Guid OrderId { get; init; }

    public required Guid CustomerId { get; init; }

    public required OrderStatus CurrentOrderStatus { get; init; }

    public required Guid RequestedByUserId { get; init; }

    public required UserRole RequestedByRole { get; init; }

    public bool HasExistingPaidPayment { get; init; }

    public bool IsAllowed { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}
```

***

# 7. Rule Result

***

## `src/OrderManagement.Domain/Rules/Results/RuleValidationResult.cs`

```csharp
namespace OrderManagement.Domain.Rules.Results;

public sealed class RuleValidationResult
{
    private RuleValidationResult(bool isAllowed, string? errorCode, string? errorMessage)
    {
        IsAllowed = isAllowed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsAllowed { get; }

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static RuleValidationResult Allowed()
    {
        return new RuleValidationResult(true, null, null);
    }

    public static RuleValidationResult Rejected(string errorCode, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("Error code is required.", nameof(errorCode));
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message is required.", nameof(errorMessage));
        }

        return new RuleValidationResult(false, errorCode, errorMessage);
    }
}
```

***

# 8. Application Error Codes

***

## `src/OrderManagement.Application/Constants/ErrorCodes.cs`

```csharp
namespace OrderManagement.Application.Constants;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";

    public const string UserNotFound = "USER_NOT_FOUND";
    public const string ProductNotFound = "PRODUCT_NOT_FOUND";
    public const string OrderNotFound = "ORDER_NOT_FOUND";
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";

    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string UserInactive = "USER_INACTIVE";

    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string InvalidOrderStatusTransition = "INVALID_ORDER_STATUS_TRANSITION";
    public const string OrderAlreadyCancelled = "ORDER_ALREADY_CANCELLED";
    public const string OrderTerminalState = "ORDER_TERMINAL_STATE";
    public const string ConcurrentUpdateConflict = "CONCURRENT_UPDATE_CONFLICT";

    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string RequestAlreadyInProgress = "REQUEST_ALREADY_IN_PROGRESS";
    public const string IdempotencyKeyReusedWithDifferentPayload = "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_PAYLOAD";

    public const string PaymentAlreadyPaid = "PAYMENT_ALREADY_PAID";
    public const string PaymentNotAllowed = "PAYMENT_NOT_ALLOWED";

    public const string DatabaseConstraintViolation = "DATABASE_CONSTRAINT_VIOLATION";
    public const string InternalServerError = "INTERNAL_SERVER_ERROR";
}
```

***

# 9. Application Exceptions

## Tambah file baru

Create file:

```text
src/OrderManagement.Application/Exceptions/AppErrorDetail.cs
```

Isi:

```csharp
namespace OrderManagement.Application.Exceptions;

public sealed class AppErrorDetail
{
    public AppErrorDetail(string? field, string message, object? metadata = null)
    {
        Field = field;
        Message = message;
        Metadata = metadata;
    }

    public string? Field { get; }

    public string Message { get; }

    public object? Metadata { get; }

    public static AppErrorDetail ForField(string field, string message, object? metadata = null)
    {
        return new AppErrorDetail(field, message, metadata);
    }

    public static AppErrorDetail General(string message, object? metadata = null)
    {
        return new AppErrorDetail(null, message, metadata);
    }
}
```

***

## `src/OrderManagement.Application/Exceptions/AppException.cs`

```csharp
namespace OrderManagement.Application.Exceptions;

public abstract class AppException : Exception
{
    protected AppException(
        string code,
        string message,
        int statusCode,
        IReadOnlyCollection<AppErrorDetail>? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details ?? [];
    }

    public string Code { get; }

    public int StatusCode { get; }

    public IReadOnlyCollection<AppErrorDetail> Details { get; }
}
```

***

## `src/OrderManagement.Application/Exceptions/ValidationAppException.cs`

```csharp
using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class ValidationAppException : AppException
{
    public ValidationAppException(
        string message = "Validation failed.",
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(ErrorCodes.ValidationError, message, StatusCodes.UnprocessableEntity, details)
    {
    }
}

internal static class StatusCodes
{
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int UnprocessableEntity = 422;
    public const int InternalServerError = 500;
}
```

> Kita taruh `StatusCodes` internal di sini supaya Application layer tidak bergantung ke `Microsoft.AspNetCore.Http`.

***

## `src/OrderManagement.Application/Exceptions/NotFoundAppException.cs`

```csharp
using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class NotFoundAppException : AppException
{
    public NotFoundAppException(
        string message,
        string code = ErrorCodes.NotFound,
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(code, message, StatusCodes.NotFound, details)
    {
    }

    public static NotFoundAppException User(Guid userId)
    {
        return new NotFoundAppException(
            "User was not found.",
            ErrorCodes.UserNotFound,
            [AppErrorDetail.ForField("userId", "User id does not exist.", new { userId })]);
    }

    public static NotFoundAppException Product(Guid productId)
    {
        return new NotFoundAppException(
            "Product was not found.",
            ErrorCodes.ProductNotFound,
            [AppErrorDetail.ForField("productId", "Product id does not exist.", new { productId })]);
    }

    public static NotFoundAppException Order(Guid orderId)
    {
        return new NotFoundAppException(
            "Order was not found.",
            ErrorCodes.OrderNotFound,
            [AppErrorDetail.ForField("orderId", "Order id does not exist.", new { orderId })]);
    }

    public static NotFoundAppException Payment(Guid paymentId)
    {
        return new NotFoundAppException(
            "Payment was not found.",
            ErrorCodes.PaymentNotFound,
            [AppErrorDetail.ForField("paymentId", "Payment id does not exist.", new { paymentId })]);
    }
}
```

***

## `src/OrderManagement.Application/Exceptions/ConflictAppException.cs`

```csharp
using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class ConflictAppException : AppException
{
    public ConflictAppException(
        string code,
        string message,
        IReadOnlyCollection<AppErrorDetail>? details = null,
        Exception? innerException = null)
        : base(code, message, StatusCodes.Conflict, details, innerException)
    {
    }

    public static ConflictAppException InsufficientStock(
        Guid productId,
        string productName,
        int requestedQuantity,
        int availableQuantity,
        string field)
    {
        return new ConflictAppException(
            ErrorCodes.InsufficientStock,
            $"Stock has changed. Product {productName} currently has only {availableQuantity} units available.",
            [
                AppErrorDetail.ForField(
                    field,
                    "Requested quantity exceeds available stock.",
                    new
                    {
                        productId,
                        requestedQuantity,
                        availableQuantity
                    })
            ]);
    }

    public static ConflictAppException RequestAlreadyInProgress()
    {
        return new ConflictAppException(
            ErrorCodes.RequestAlreadyInProgress,
            "A request with the same idempotency key is still being processed.");
    }

    public static ConflictAppException IdempotencyKeyReusedWithDifferentPayload()
    {
        return new ConflictAppException(
            ErrorCodes.IdempotencyKeyReusedWithDifferentPayload,
            "This idempotency key has already been used with a different request payload.");
    }
}
```

***

## `src/OrderManagement.Application/Exceptions/BusinessRuleAppException.cs`

```csharp
using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class BusinessRuleAppException : AppException
{
    public BusinessRuleAppException(
        string code,
        string message,
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(code, message, StatusCodes.UnprocessableEntity, details)
    {
    }

    public static BusinessRuleAppException InvalidOrderTransition(
        string currentStatus,
        string targetStatus)
    {
        return new BusinessRuleAppException(
            ErrorCodes.InvalidOrderStatusTransition,
            $"Order status cannot be changed from {currentStatus} to {targetStatus}.",
            [
                AppErrorDetail.General(
                    "Invalid order status transition.",
                    new
                    {
                        currentStatus,
                        targetStatus
                    })
            ]);
    }

    public static BusinessRuleAppException PaymentNotAllowed(string currentStatus)
    {
        return new BusinessRuleAppException(
            ErrorCodes.PaymentNotAllowed,
            "Payment is only allowed when order status is Pending.",
            [
                AppErrorDetail.General(
                    "Payment cannot be processed for current order status.",
                    new
                    {
                        currentStatus
                    })
            ]);
    }
}
```

***

## `src/OrderManagement.Application/Exceptions/UnauthorizedAppException.cs`

```csharp
using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class UnauthorizedAppException : AppException
{
    public UnauthorizedAppException(
        string message = "Unauthorized.",
        string code = ErrorCodes.Unauthorized,
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(code, message, StatusCodes.Unauthorized, details)
    {
    }

    public static UnauthorizedAppException InvalidCredentials()
    {
        return new UnauthorizedAppException(
            "Invalid username or password.",
            ErrorCodes.InvalidCredentials);
    }
}
```

***

## `src/OrderManagement.Application/Exceptions/ForbiddenAppException.cs`

```csharp
using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class ForbiddenAppException : AppException
{
    public ForbiddenAppException(
        string message = "Forbidden.",
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(ErrorCodes.Forbidden, message, StatusCodes.Forbidden, details)
    {
    }
}
```

***

## `src/OrderManagement.Application/Exceptions/ConcurrencyAppException.cs`

```csharp
using OrderManagement.Application.Constants;

namespace OrderManagement.Application.Exceptions;

public sealed class ConcurrencyAppException : AppException
{
    public ConcurrencyAppException(
        string message,
        IReadOnlyCollection<AppErrorDetail>? details = null)
        : base(ErrorCodes.ConcurrentUpdateConflict, message, StatusCodes.Conflict, details)
    {
    }

    public static ConcurrencyAppException RowVersionMismatch(
        long expected,
        long current)
    {
        return new ConcurrencyAppException(
            "Order has been modified by another user. Please refresh and try again.",
            [
                AppErrorDetail.ForField(
                    "expectedRowVersion",
                    "Expected row version does not match current row version.",
                    new
                    {
                        expected,
                        current
                    })
            ]);
    }
}
```

***

# 10. Build Check

Setelah semua file di atas diganti/ditambah, run:

```bash
dotnet build
```

Kalau sukses, lanjut:

```bash
dotnet test
```

***

# 11. Catatan Potensi Compile Issue

Ada satu hal penting bro: kalau di Batch 1 lu punya file kosong lain yang namanya bentrok atau package belum restore, build bisa gagal bukan karena Batch 2 logic.

Pastikan:

```bash
dotnet restore
dotnet build
```

Kalau error muncul karena file `.cs` kosong, biasanya aman. Empty `.cs` valid. Tapi kalau file lama template masih ada dan refer namespace hilang, hapus aja.

***

# 12. Commit Batch 2

```bash
git add .
git commit -m "feat: add domain model and application exceptions"
```

***

# 13. Next Batch

Next kita masuk:

```text
Batch 3: Global Exception Handling, Correlation ID, Logging Middleware
```

Di Batch 3 kita akan isi:

```text
Api/Contracts/Common/*
Api/Middleware/CorrelationIdMiddleware.cs
Api/Middleware/GlobalExceptionHandlingMiddleware.cs
Api/Middleware/RequestLoggingMiddleware.cs
Api/Extensions/MiddlewareExtensions.cs
Program.cs update pipeline
```

Batch 3 penting karena semua exception yang barusan kita buat akan mulai keluar sebagai JSON error response konsisten.
