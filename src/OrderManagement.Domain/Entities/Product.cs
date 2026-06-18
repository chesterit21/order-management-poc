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