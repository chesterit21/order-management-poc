using Microsoft.Extensions.Logging;
using NRules;
using NRules.Fluent;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Constants;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Rules.Facts;
using OrderManagement.Domain.Rules.Results;

namespace OrderManagement.Infrastructure.Rules;

public sealed class NRulesOrderRulesService : IOrderRulesService
{
    private static readonly Lazy<ISessionFactory> SessionFactory = new(CreateSessionFactory);

    private readonly ILogger<NRulesOrderRulesService> _logger;

    public NRulesOrderRulesService(ILogger<NRulesOrderRulesService> logger)
    {
        _logger = logger;
    }

    public RuleValidationResult ValidateOrderTransition(OrderTransitionFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var workingFact = new OrderTransitionFact
        {
            OrderId = fact.OrderId,
            CustomerId = fact.CustomerId,
            CurrentStatus = fact.CurrentStatus,
            TargetStatus = fact.TargetStatus,
            RequestedByUserId = fact.RequestedByUserId,
            RequestedByRole = fact.RequestedByRole
        };

        FireRules(workingFact);

        if (workingFact.IsAllowed)
        {
            _logger.LogDebug(
                "Order transition allowed. OrderId={OrderId} CurrentStatus={CurrentStatus} TargetStatus={TargetStatus}",
                workingFact.OrderId,
                workingFact.CurrentStatus,
                workingFact.TargetStatus);

            return RuleValidationResult.Allowed();
        }

        var errorCode = string.IsNullOrWhiteSpace(workingFact.ErrorCode)
            ? ErrorCodes.InvalidOrderStatusTransition
            : workingFact.ErrorCode;

        var errorMessage = string.IsNullOrWhiteSpace(workingFact.ErrorMessage)
            ? $"Order status cannot be changed from {workingFact.CurrentStatus} to {workingFact.TargetStatus}."
            : workingFact.ErrorMessage;

        _logger.LogInformation(
            "Order transition rejected. OrderId={OrderId} CurrentStatus={CurrentStatus} TargetStatus={TargetStatus} ErrorCode={ErrorCode}",
            workingFact.OrderId,
            workingFact.CurrentStatus,
            workingFact.TargetStatus,
            errorCode);

        return RuleValidationResult.Rejected(errorCode, errorMessage);
    }

    public RuleValidationResult ValidateCancel(CancelOrderFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var workingFact = new CancelOrderFact
        {
            OrderId = fact.OrderId,
            CustomerId = fact.CustomerId,
            CurrentStatus = fact.CurrentStatus,
            RequestedByUserId = fact.RequestedByUserId,
            RequestedByRole = fact.RequestedByRole
        };

        FireRules(workingFact);

        if (workingFact.IsAllowed)
        {
            _logger.LogDebug(
                "Cancel order allowed. OrderId={OrderId} CurrentStatus={CurrentStatus}",
                workingFact.OrderId,
                workingFact.CurrentStatus);

            return RuleValidationResult.Allowed();
        }

        var errorCode = string.IsNullOrWhiteSpace(workingFact.ErrorCode)
            ? ErrorCodes.InvalidOrderStatusTransition
            : workingFact.ErrorCode;

        var errorMessage = string.IsNullOrWhiteSpace(workingFact.ErrorMessage)
            ? $"Order cannot be cancelled because current status is {workingFact.CurrentStatus}."
            : workingFact.ErrorMessage;

        _logger.LogInformation(
            "Cancel order rejected. OrderId={OrderId} CurrentStatus={CurrentStatus} ErrorCode={ErrorCode}",
            workingFact.OrderId,
            workingFact.CurrentStatus,
            errorCode);

        return RuleValidationResult.Rejected(errorCode, errorMessage);
    }

    public RuleValidationResult ValidatePayment(PaymentFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);

        var workingFact = new PaymentFact
        {
            OrderId = fact.OrderId,
            CustomerId = fact.CustomerId,
            CurrentOrderStatus = fact.CurrentOrderStatus,
            RequestedByUserId = fact.RequestedByUserId,
            RequestedByRole = fact.RequestedByRole,
            HasExistingPaidPayment = fact.HasExistingPaidPayment
        };

        FireRules(workingFact);

        if (workingFact.IsAllowed)
        {
            _logger.LogDebug(
                "Payment allowed. OrderId={OrderId} CurrentOrderStatus={CurrentOrderStatus}",
                workingFact.OrderId,
                workingFact.CurrentOrderStatus);

            return RuleValidationResult.Allowed();
        }

        var errorCode = string.IsNullOrWhiteSpace(workingFact.ErrorCode)
            ? ErrorCodes.PaymentNotAllowed
            : workingFact.ErrorCode;

        var errorMessage = string.IsNullOrWhiteSpace(workingFact.ErrorMessage)
            ? "Payment is only allowed when order status is Pending."
            : workingFact.ErrorMessage;

        _logger.LogInformation(
            "Payment rejected. OrderId={OrderId} CurrentOrderStatus={CurrentOrderStatus} ErrorCode={ErrorCode}",
            workingFact.OrderId,
            workingFact.CurrentOrderStatus,
            errorCode);

        return RuleValidationResult.Rejected(errorCode, errorMessage);
    }

    private static void FireRules<TFact>(TFact fact)
        where TFact : class
    {
        var session = SessionFactory.Value.CreateSession();
        session.Insert(fact);
        session.Fire();
    }

    private static ISessionFactory CreateSessionFactory()
    {
        var repository = new RuleRepository();

        repository.Load(loadSpecification =>
        {
            loadSpecification.From(typeof(NRulesOrderRulesService).Assembly);
        });

        return repository.Compile();
    }
}