namespace OrderManagement.Application.DTOs.Payments;

public sealed record PaymentListResult
{
    public required Guid OrderId { get; init; }

    public required IReadOnlyCollection<PaymentResult> Payments { get; init; }
}

public sealed record PaymentResult
{
    public required Guid Id { get; init; }

    public required decimal Amount { get; init; }

    public required string Status { get; init; }

    public required string Provider { get; init; }

    public string? PaymentReference { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}