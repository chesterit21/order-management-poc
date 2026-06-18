using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Domain.Rules.Results;

namespace OrderManagement.Application.Abstractions.Rules;

public interface IOrderRulesService
{
    RuleValidationResult ValidateOrderTransition(OrderTransitionFact fact);

    RuleValidationResult ValidateCancel(CancelOrderFact fact);

    RuleValidationResult ValidatePayment(PaymentFact fact);
}