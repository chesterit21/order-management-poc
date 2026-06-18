namespace OrderManagement.Api.Contracts.Orders;

/// <summary>
/// Request body for PATCH /api/v1/orders/{id}/status.
/// Used by buyer/customer-facing endpoint to transition an order's status.
/// </summary>
public sealed class UpdateOrderStatusRequest
{
    /// <summary>
    /// Target order status. Valid values: Confirmed, Shipped, Delivered.
    /// Use the cancel endpoint to cancel an order.
    /// </summary>
    public string TargetStatus { get; init; } = string.Empty;

    /// <summary>
    /// Optimistic concurrency token. Must match the current RowVersion of the order.
    /// </summary>
    public long ExpectedRowVersion { get; init; }

    /// <summary>
    /// Optional human-readable reason for the status change.
    /// </summary>
    public string? Reason { get; init; }
}
