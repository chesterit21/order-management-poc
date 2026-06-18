using NRules.Fluent.Dsl;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class PaymentAllowedRule : Rule
{
    public override void Define()
    {
        PaymentFact fact = null!;

        When()
            .Match(() => fact);

        Then()
            .Do(ctx => ProcessPaymentEligibility(fact));
    }

    private static void ProcessPaymentEligibility(PaymentFact fact)
    {
        if (!CanPayOrder(fact.RequestedByRole))
        {
            fact.IsAllowed = false;
            fact.ErrorCode = ErrorCodes.PaymentNotAllowed;
            fact.ErrorMessage = "User role is not allowed to create payment.";
            return;
        }

        if (fact.CurrentOrderStatus != OrderStatus.Pending)
        {
            fact.IsAllowed = false;
            fact.ErrorCode = ErrorCodes.PaymentNotAllowed;
            fact.ErrorMessage = "Payment is only allowed when order status is Pending.";
            return;
        }

        if (fact.HasExistingPaidPayment)
        {
            fact.IsAllowed = false;
            fact.ErrorCode = ErrorCodes.PaymentAlreadyPaid;
            fact.ErrorMessage = "Order already has a paid payment.";
            return;
        }

        fact.IsAllowed = true;
    }

    private static bool CanPayOrder(UserRole role)
    {
        return role is UserRole.Buyer
            or UserRole.SellerAdmin
            or UserRole.ApplicationAdmin;
    }
}