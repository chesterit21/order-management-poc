using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.DTOs.Products.Backoffice;

public sealed record AdjustProductStockPersistenceRequest
{
    public required Guid ProductId { get; init; }

    public required StockAdjustmentType AdjustmentType { get; init; }

    public required int Quantity { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }

    public required Guid AdjustedBy { get; init; }

    public required DateTimeOffset Now { get; init; }
}