using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.DTOs.Payments;

public sealed record CreatePaymentPersistenceRequest
{
    public required Guid OrderId { get; init; }

    public required Guid RequestedBy { get; init; }

    public required UserRole RequestedByRole { get; init; }

    public required string Provider { get; init; }

    public required PaymentSimulationResult SimulateResult { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public enum PaymentSimulationResult
{
    Success = 1,
    Failed = 2
}