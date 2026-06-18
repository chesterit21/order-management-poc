namespace OrderManagement.Application.DTOs.Orders;

public sealed record CancelOrderResult
{
    public required Guid Id { get; init; }

    public required string OrderNumber { get; init; }

    public required string PreviousStatus { get; init; }

    public required string CurrentStatus { get; init; }

    public required string CancellationReason { get; init; }

    public required bool StockRestoreApplied { get; init; }

    public required long RowVersion { get; init; }

    public required IReadOnlyCollection<StockRestoredResult> StockRestored { get; init; }

    public required IReadOnlyCollection<StockNotRestoredResult> StockNotRestored { get; init; }

    public required bool PaymentRefundRequired { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record StockRestoredResult
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }
}

public sealed record StockNotRestoredResult
{
    public required Guid ProductId { get; init; }

    public required int Quantity { get; init; }

    public required string Reason { get; init; }
}