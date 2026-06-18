namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed record CreateProductRequest
{
    public Guid StoreId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public int StockQuantity { get; init; }

    public decimal Price { get; init; }
}