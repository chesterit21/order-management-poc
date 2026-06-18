namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record BackofficeProductDto
{
    public required Guid Id { get; init; }

    public required Guid StoreId { get; init; }

    public required string StoreName { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? ImageUrl { get; init; }

    public required int StockQuantity { get; init; }

    public required decimal Price { get; init; }

    public required long RowVersion { get; init; }

    public required bool IsActive { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}