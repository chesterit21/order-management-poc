using FluentValidation;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Abstractions.Authentication;
using OrderManagement.Application.Abstractions.Payments;
using OrderManagement.Application.Abstractions.Repositories;
using OrderManagement.Application.Abstractions.Rules;
using OrderManagement.Application.Abstractions.Time;
using OrderManagement.Application.Abstractions.ActivityLogs;
using OrderManagement.Application.DTOs.Payments;
using OrderManagement.Application.DTOs.ActivityLogs;
using OrderManagement.Application.Exceptions;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Services;

public sealed class PaymentService(
    IPaymentRepository paymentRepository,
    IOrderRepository orderRepository,
    ICurrentUserContext currentUserContext,
    IOrderRulesService orderRulesService,
    IClock clock,
    IValidator<CreatePaymentCommand> validator,
    ILogger<PaymentService> logger,
    IActivityLogWriter activityLogWriter) : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository = paymentRepository;
    private readonly IOrderRepository _orderRepository = orderRepository;
    private readonly ICurrentUserContext _currentUserContext = currentUserContext;
    private readonly IOrderRulesService _orderRulesService = orderRulesService;
    private readonly IClock _clock = clock;
    private readonly IValidator<CreatePaymentCommand> _validator = validator;
    private readonly ILogger<PaymentService> _logger = logger;
    private readonly IActivityLogWriter _activityLogWriter = activityLogWriter;

    public async Task<CreatePaymentResult> CreateAsync(
        CreatePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationAppException(
                "Create payment request validation failed.",
                validationResult.Errors
                    .Select(error => AppErrorDetail.ForField(error.PropertyName, error.ErrorMessage))
                    .ToArray());
        }

        var currentUserId = GetRequiredCurrentUserId();
        var currentRole = GetRequiredCurrentUserRole();

        if (currentRole == UserRole.SellerOperator)
        {
            throw new ForbiddenAppException("Seller operator cannot create buyer payment.");
        }

        if (currentRole == UserRole.DevOps)
        {
            throw new ForbiddenAppException("DevOps cannot create payment.");
        }

        if (currentRole is not (
                UserRole.Buyer or
                UserRole.SellerAdmin or
                UserRole.ApplicationAdmin))
        {
            throw new ForbiddenAppException("User is not allowed to create payment.");
        }

        var order = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            throw NotFoundAppException.Order(command.OrderId);
        }

        var paymentFact = new Domain.Rules.Facts.PaymentFact
        {
            OrderId = command.OrderId,
            CustomerId = order.CustomerId,
            CurrentOrderStatus = Enum.Parse<OrderStatus>(order.Status, ignoreCase: true),
            RequestedByUserId = currentUserId,
            RequestedByRole = currentRole,
            HasExistingPaidPayment = order.HasPaidPayment
        };

        var validationResultFromRules = _orderRulesService.ValidatePayment(paymentFact);
        
        if (!validationResultFromRules.IsAllowed)
        {
            throw new BusinessRuleAppException(
                validationResultFromRules.ErrorCode ?? "PaymentValidationFailed",
                validationResultFromRules.ErrorMessage ?? "Payment is not allowed for this order.");
        }

        if (currentRole is UserRole.Buyer or UserRole.SellerAdmin &&
            order.CustomerId != currentUserId)
        {
            throw new ForbiddenAppException("Buyer can only pay their own order.");
        }

        _activityLogWriter.TryWrite(
            ActivityLogTypes.PaymentCreateStarted,
            orderId: command.OrderId,
            metadata: new
            {
                provider = command.Provider,
                simulateResult = command.SimulateResult,
                requestedBy = currentUserId,
                requestedByRole = currentRole.ToString(),
                stage = "Attempt"
            });

        var simulateResult = Enum.Parse<PaymentSimulationResult>(
            command.SimulateResult,
            ignoreCase: true);

        var result = await _paymentRepository.CreateAsync(
            new CreatePaymentPersistenceRequest
            {
                OrderId = command.OrderId,
                RequestedBy = currentUserId,
                RequestedByRole = currentRole,
                Provider = command.Provider.Trim(),
                SimulateResult = simulateResult,
                Now = _clock.UtcNow
            },
            cancellationToken);

        _logger.LogInformation(
            "Payment created. PaymentId={PaymentId} OrderId={OrderId} PaymentStatus={PaymentStatus} OrderStatus={OrderStatus} RequestedBy={RequestedBy}",
            result.PaymentId,
            result.OrderId,
            result.Status,
            result.OrderStatus,
            currentUserId);

        return result;
    }

    public async Task<PaymentListResult> ListByOrderIdAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty)
        {
            throw new ValidationAppException(
                "Order id validation failed.",
                [AppErrorDetail.ForField("orderId", "Order id is required.")]);
        }

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);

        if (order is null)
        {
            throw NotFoundAppException.Order(orderId);
        }

        EnsureCanAccessOrder(order.CustomerId);

        return await _paymentRepository.ListByOrderIdAsync(orderId, cancellationToken);
    }

    private Guid GetRequiredCurrentUserId()
    {
        if (!_currentUserContext.IsAuthenticated || _currentUserContext.UserId is null)
        {
            throw new UnauthorizedAppException("Authentication is required.");
        }

        return _currentUserContext.UserId.Value;
    }

    private UserRole GetRequiredCurrentUserRole()
    {
        return _currentUserContext.Role
            ?? throw new ForbiddenAppException("User role claim is missing.");
    }

    private void EnsureCanAccessOrder(Guid customerId)
    {
        if (_currentUserContext.IsAdminOrOps())
        {
            return;
        }

        if (_currentUserContext.Role is UserRole.Buyer or UserRole.SellerAdmin &&
            _currentUserContext.UserId == customerId)
        {
            return;
        }

        throw new ForbiddenAppException("You do not have permission to access this order.");
    }
}
