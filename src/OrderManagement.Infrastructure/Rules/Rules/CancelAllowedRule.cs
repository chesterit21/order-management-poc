using NRules.Fluent.Dsl;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class CancelAllowedRule : Rule
{
    public override void Define()
    {
        CancelOrderFact fact = null!;

        When()
            .Match(() => fact);

        Then()
            .Do(ctx => ProcessCancelEligibility(fact));
    }

    private static void ProcessCancelEligibility(CancelOrderFact fact)
    {
        if (!CanCancelOrder(fact.RequestedByRole))
        {
            fact.IsAllowed = false;
            fact.ErrorCode = ErrorCodes.InvalidOrderStatusTransition;
            fact.ErrorMessage = "User role is not allowed to cancel order.";
            return;
        }

        if (fact.CurrentStatus == OrderStatus.Pending || fact.CurrentStatus == OrderStatus.Confirmed)
        {
            fact.IsAllowed = true;
            return;
        }

        fact.IsAllowed = false;
        fact.ErrorCode = fact.CurrentStatus == OrderStatus.Cancelled
            ? ErrorCodes.OrderAlreadyCancelled
            : ErrorCodes.InvalidOrderStatusTransition;

        fact.ErrorMessage = $"Order cannot be cancelled because current status is {fact.CurrentStatus}.";
    }

    private static bool CanCancelOrder(UserRole role)
    {
        return role is UserRole.Buyer
            or UserRole.SellerAdmin
            or UserRole.SellerOperator
            or UserRole.ApplicationAdmin;
    }
}