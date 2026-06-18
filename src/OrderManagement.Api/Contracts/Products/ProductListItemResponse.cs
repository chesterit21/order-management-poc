namespace OrderManagement.Api.Contracts.Products;

public sealed record ProductListItemResponse
{
    public Guid Id { get; init; }

    public Guid? StoreId { get; init; }

    public string? StoreName { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }

    public bool IsActive { get; init; }
}