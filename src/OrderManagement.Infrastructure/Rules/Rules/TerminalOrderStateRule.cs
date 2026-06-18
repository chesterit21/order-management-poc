using NRules.Fluent.Dsl;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;

namespace OrderManagement.Infrastructure.Rules.Rules;

public sealed class TerminalOrderStateRule : Rule
{
    public override void Define()
    {
        OrderTransitionFact fact = null!;

        When()
            .Match(() => fact,
                x => IsTerminalState(x.CurrentStatus));

        Then()
            .Do(ctx => RejectTerminalState(fact));
    }

    private static bool IsTerminalState(OrderStatus status)
    {
        return status == OrderStatus.Delivered || status == OrderStatus.Cancelled;
    }

    private static void RejectTerminalState(OrderTransitionFact fact)
    {
        fact.IsAllowed = false;
        fact.ErrorCode = ErrorCodes.OrderTerminalState;
        fact.ErrorMessage = $"Order is already in terminal state {fact.CurrentStatus}.";
    }
}