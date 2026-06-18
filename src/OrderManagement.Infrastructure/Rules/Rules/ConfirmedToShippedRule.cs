using NRules.Fluent.Dsl;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class ConfirmedToShippedRule : Rule
{
    public override void Define()
    {
        OrderTransitionFact fact = null!;

        When()
            .Match(() => fact,
                x => x.CurrentStatus == OrderStatus.Confirmed,
                x => x.TargetStatus == OrderStatus.Shipped);

        Then()
            .Do(ctx => ProcessOrderTransition(fact));
    }

    private static void ProcessOrderTransition(OrderTransitionFact fact)
    {
        if (!CanMutateBusinessOrder(fact.RequestedByRole))
        {
            fact.IsAllowed = false;
            fact.ErrorCode = ErrorCodes.InvalidOrderStatusTransition;
            fact.ErrorMessage = "User role is not allowed to update order status.";
            return;
        }

        fact.IsAllowed = true;
    }

    private static bool CanMutateBusinessOrder(UserRole role)
    {
        return role is UserRole.SellerAdmin
            or UserRole.SellerOperator
            or UserRole.ApplicationAdmin;
    }
}