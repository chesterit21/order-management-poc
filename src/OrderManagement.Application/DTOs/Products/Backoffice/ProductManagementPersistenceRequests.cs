namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record CreateProductPersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required Guid StoreId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required int StockQuantity { get; init; }

    public required decimal Price { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed record UpdateProductPersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required decimal Price { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed record SetProductStatusPersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required bool IsActive { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public required DateTimeOffset Now { get; init; }
}