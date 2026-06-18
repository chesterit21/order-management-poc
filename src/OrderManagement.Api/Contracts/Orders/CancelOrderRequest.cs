namespace OrderManagement.Api.Contracts.Orders;

public sealed record CancelOrderRequest
{
    public long ExpectedRowVersion { get; init; }

    public string? CancellationReason { get; init; }

    public string? Reason { get; init; }
}