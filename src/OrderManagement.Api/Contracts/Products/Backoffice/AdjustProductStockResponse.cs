using OrderManagement.Domain.Enums;
using System;

namespace OrderManagement.Api.Contracts.Products.Backoffice;

public sealed record AdjustProductStockResponse
{
    public Guid ProductId { get; init; }

    public Guid StoreId { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public StockAdjustmentType AdjustmentType { get; init; }

    public int Quantity { get; init; }

    public int StockBefore { get; init; }

    public int StockAfter { get; init; }

    public long RowVersion { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}