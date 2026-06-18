using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Abstractions.Orders;

public interface IOrderCancellationPolicy
{
    OrderCancellationDecision Resolve(
        string? cancellationReason,
        string? freeTextReason,
        UserRole currentRole,
        bool isBuyerInitiated);
}

public sealed class OrderCancellationDecision
{
    public required OrderCancellationReason CancellationReason { get; init; }

    public required bool RestoreStock { get; init; }

    public required string ReasonText { get; init; }
}