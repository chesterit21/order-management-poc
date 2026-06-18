using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Rules.Facts;

public sealed class PaymentFact
{
    public required Guid OrderId { get; init; }

    public required Guid CustomerId { get; init; }

    public required OrderStatus CurrentOrderStatus { get; init; }

    public required Guid RequestedByUserId { get; init; }

    public required UserRole RequestedByRole { get; init; }

    public bool HasExistingPaidPayment { get; init; }

    public bool IsAllowed { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}