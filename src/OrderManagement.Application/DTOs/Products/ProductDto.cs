namespace OrderManagement.Application.DTOs.Products;

public sealed record ProductDto
{
    public required Guid Id { get; init; }

    public Guid? StoreId { get; init; }

    public string? StoreName { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public required int StockQuantity { get; init; }

    public required decimal Price { get; init; }

    public required long RowVersion { get; init; }

    public required bool IsActive { get; init; }
}