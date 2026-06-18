namespace OrderManagement.Api.Contracts.Orders;

public sealed record CancelOrderResponse
{
    public Guid Id { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public string PreviousStatus { get; init; } = string.Empty;

    public string CurrentStatus { get; init; } = string.Empty;

    public string CancellationReason { get; init; } = string.Empty;

    public bool StockRestoreApplied { get; init; }

    public long RowVersion { get; init; }

    public IReadOnlyCollection<StockRestoredResponse> StockRestored { get; init; } = [];

    public IReadOnlyCollection<StockNotRestoredResponse> StockNotRestored { get; init; } = [];

    public bool PaymentRefundRequired { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record StockRestoredResponse
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }
}

public sealed record StockNotRestoredResponse
{
    public Guid ProductId { get; init; }

    public int Quantity { get; init; }

    public string Reason { get; init; } = string.Empty;
}