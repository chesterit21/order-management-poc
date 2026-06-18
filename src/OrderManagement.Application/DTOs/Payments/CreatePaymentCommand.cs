namespace OrderManagement.Application.DTOs.Payments;

public sealed record CreatePaymentCommand
{
    public required Guid OrderId { get; init; }

    public required string Provider { get; init; }

    public required string SimulateResult { get; init; }
}