namespace OrderManagement.Api.Contracts.Orders.Backoffice;

public sealed record BackofficeUpdateOrderStatusRequest
{
    public string TargetStatus { get; init; } = string.Empty;

    public long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }
}