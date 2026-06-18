namespace OrderManagement.Api.Contracts.Payments;

public sealed record CreatePaymentResponse
{
    public Guid PaymentId { get; init; }

    public Guid OrderId { get; init; }

    public decimal Amount { get; init; }

    public string Status { get; init; } = string.Empty;

    public string OrderStatus { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? PaymentReference { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}