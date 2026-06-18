namespace OrderManagement.Api.Contracts.Payments;

public sealed record PaymentListResponse
{
    public Guid OrderId { get; init; }

    public IReadOnlyCollection<PaymentResponse> Payments { get; init; } = [];
}

public sealed record PaymentResponse
{
    public Guid Id { get; init; }

    public decimal Amount { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? PaymentReference { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
