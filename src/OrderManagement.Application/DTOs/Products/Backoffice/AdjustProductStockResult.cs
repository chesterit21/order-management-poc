namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record AdjustProductStockResult
{
    public required Guid ProductId { get; init; }

    public required Guid StoreId { get; init; }

    public required string Sku { get; init; }

    public required string Name { get; init; }

    public required string AdjustmentType { get; init; }

    public required int Quantity { get; init; }

    public required int StockBefore { get; init; }

    public required int StockAfter { get; init; }

    public required long RowVersion { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}