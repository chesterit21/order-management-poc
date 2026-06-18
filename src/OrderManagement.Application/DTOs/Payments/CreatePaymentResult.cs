namespace OrderManagement.Application.DTOs.Payments;

public sealed record CreatePaymentResult
{
    public required Guid PaymentId { get; init; }

    public required Guid OrderId { get; init; }

    public required decimal Amount { get; init; }

    public required string Status { get; init; }

    public required string OrderStatus { get; init; }

    public required string Provider { get; init; }

    public string? PaymentReference { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}