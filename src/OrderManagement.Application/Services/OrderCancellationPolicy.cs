using OrderManagement.Application.Abstractions.Orders;
using OrderManagement.Application.Constants;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class OrderCancellationPolicy() : IOrderCancellationPolicy
{
    public OrderCancellationDecision Resolve(
        string? cancellationReason,
        string? freeTextReason,
        UserRole currentRole,
        bool isBuyerInitiated)
    {
        var resolvedReason = ResolveCancellationReason(
            cancellationReason,
            currentRole,
            isBuyerInitiated);
        var restoreStock = ShouldRestoreStock(resolvedReason);

        return new OrderCancellationDecision
        {
            CancellationReason = resolvedReason,
            RestoreStock = restoreStock,
            ReasonText = BuildReasonText(freeTextReason, resolvedReason, restoreStock)
        };
    }

    private static OrderCancellationReason ResolveCancellationReason(
        string? reason,
        UserRole currentRole,
        bool isBuyerInitiated)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return isBuyerInitiated
                ? OrderCancellationReason.CustomerRequested
                : OrderCancellationReason.OperationalIssue;
        }

        if (!Enum.TryParse<OrderCancellationReason>(reason, ignoreCase: true, out var parsed))
        {
            throw new BusinessRuleAppException(
                ErrorCodes.InvalidCancellationReason,
                "Cancellation reason is invalid.");
        }

        if (isBuyerInitiated &&
            parsed != OrderCancellationReason.CustomerRequested)
        {
            throw new ForbiddenAppException("Buyer can only cancel with CustomerRequested reason.");
        }

        if (currentRole == UserRole.DevOps)
        {
            throw new ForbiddenAppException("DevOps cannot cancel order.");
        }

        return parsed;
    }


    private static bool ShouldRestoreStock(OrderCancellationReason reason)
    {
        return reason is not OrderCancellationReason.StockUnavailable
            and not OrderCancellationReason.InventoryMismatch;
    }

    private static string BuildReasonText(
        string? freeTextReason,
        OrderCancellationReason reason,
        bool restoreStock)
    {
        var stockAction = restoreStock
            ? "Stock restored."
            : "Stock was not restored because cancellation reason indicates physical stock is unavailable or mismatched.";

        if (string.IsNullOrWhiteSpace(freeTextReason))
        {
            return $"Cancellation reason: {reason}. {stockAction}";
        }

        return $"Cancellation reason: {reason}. {stockAction} Note: {freeTextReason.Trim()}";
    }
}
