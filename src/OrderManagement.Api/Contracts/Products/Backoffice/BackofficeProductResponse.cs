namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed record BackofficeProductResponse
{
    public Guid Id { get; init; }

    public Guid StoreId { get; init; }

    public string StoreName { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }

    public long RowVersion { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}