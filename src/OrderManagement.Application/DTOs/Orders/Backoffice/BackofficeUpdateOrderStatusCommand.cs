namespace OrderManagement.Application.DTOs.Orders.Backoffice;

public sealed record BackofficeUpdateOrderStatusCommand
{
    public required Guid OrderId { get; init; }

    public required string TargetStatus { get; init; }

    public required long ExpectedRowVersion { get; init; }

    public string? Reason { get; init; }
}