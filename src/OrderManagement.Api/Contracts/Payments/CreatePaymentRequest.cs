namespace OrderManagement.Api.Contracts.Payments;

public sealed record CreatePaymentRequest
{
    public string Provider { get; init; } = string.Empty;

    public string SimulateResult { get; init; } = string.Empty;
}