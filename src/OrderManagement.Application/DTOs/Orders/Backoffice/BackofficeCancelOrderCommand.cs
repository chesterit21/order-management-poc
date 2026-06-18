namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed record BackofficeCancelOrderCommand
{
    public required Guid OrderId { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? CancellationReason { get; init; }

    public string? Reason { get; init; }
}