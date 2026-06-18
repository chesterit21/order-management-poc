namespace OrderManagement.Api.Contracts.Orders.Backoffice;

public sealed record BackofficeCancelOrderRequest
{
    public long ExpectedRowVersion { get; init; }

    public string? CancellationReason { get; init; }

    public string? Reason { get; init; }
}